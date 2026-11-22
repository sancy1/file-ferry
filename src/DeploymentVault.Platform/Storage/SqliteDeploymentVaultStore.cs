// ============================================================================
// filepath: src/DeploymentVault.Platform/Storage/SqliteDeploymentVaultStore.cs
// layer:    Infrastructure
// module:   DeploymentVault.Platform.Storage
// since:    Phase A (Deployment Vault registry)
// purpose:  SQLite implementation of IDeploymentVaultStore. Full CRUD on
//           Application and InfrastructureRecord.
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using DeploymentVault.Core.Abstractions;
using DeploymentVault.Core.Domain;

namespace DeploymentVault.Platform.Storage;

/// <summary>
/// SQLite implementation of <see cref="IDeploymentVaultStore"/>.
/// </summary>
public sealed class SqliteDeploymentVaultStore : IDeploymentVaultStore
{
    private readonly DeploymentVaultSchemaBootstrap _bootstrap;

    public SqliteDeploymentVaultStore(DeploymentVaultSchemaBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        _bootstrap = bootstrap;
    }

    // ========================================================================
    // Applications (methods 1-8)
    // ========================================================================

    public async Task<IReadOnlyList<Application>> ListApplicationsAsync(
        VaultQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePaging(query);
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        var sql = BuildApplicationSearchSql(query, out var parameters);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value);
        var rows = new List<Application>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadApplicationRow(reader));
        }
        return (IReadOnlyList<Application>)rows;
    }

    public async Task<Application?> GetApplicationAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadApplicationAsync(conn, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Application> CreateApplicationAsync(
        Application application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertApplicationAsync(conn, (SqliteTransaction)tx, application, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return application;
    }

    public async Task<Application> UpdateApplicationAsync(
        Application application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpdateApplicationRowAsync(conn, (SqliteTransaction)tx, application, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return application;
    }

    public Task SoftDeleteApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        => SetApplicationTombstoneAsync(id, DateTimeOffset.UtcNow, cancellationToken);

    public Task RestoreApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        => SetApplicationTombstoneAsync(id, null, cancellationToken);

    public async Task HardDeleteApplicationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM applications WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountRecordsAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM infrastructure_records WHERE application_id = $aid AND deleted_at_utc IS NULL;";
        cmd.Parameters.AddWithValue("$aid", applicationId.ToString());
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    // ========================================================================
    // Helpers (applications + shared)
    // ========================================================================

    private static void ValidatePaging(VaultQuery query)
    {
        if (query.Skip < 0) throw new ArgumentOutOfRangeException(nameof(query), "Skip must be >= 0.");
    }

    private static int EffectiveTake(VaultQuery query)
    {
        if (query.Take <= 0) return 100;
        return query.Take > 500 ? 500 : query.Take;
    }

    private static string SortClause(VaultSortField sort) => sort switch
    {
        VaultSortField.UpdatedAtDesc => "ORDER BY updated_at_utc DESC",
        VaultSortField.UpdatedAtAsc  => "ORDER BY updated_at_utc ASC",
        VaultSortField.NameAsc       => "ORDER BY LOWER(name) ASC",
        VaultSortField.NameDesc      => "ORDER BY LOWER(name) DESC",
        VaultSortField.CreatedAtDesc => "ORDER BY created_at_utc DESC",
        _                            => "ORDER BY updated_at_utc DESC",
    };

    private static string BuildApplicationSearchSql(VaultQuery query, out List<KeyValuePair<string, object>> parameters)
    {
        parameters = new List<KeyValuePair<string, object>>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT id, name, description, owner, repository_url, application_url, status, tags, notes, created_at_utc, updated_at_utc, deleted_at_utc");
        sb.AppendLine("FROM applications");
        sb.AppendLine("WHERE 1 = 1");
        if (!query.IncludeDeleted) sb.AppendLine("  AND deleted_at_utc IS NULL");
        if (query.StatusFilter.HasValue)
        {
            sb.AppendLine("  AND status = $status");
            parameters.Add(new KeyValuePair<string, object>("$status", query.StatusFilter.Value.ToString()));
        }
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            sb.AppendLine("  AND (LOWER(name) LIKE $text OR LOWER(COALESCE(description,'')) LIKE $text OR LOWER(COALESCE(owner,'')) LIKE $text OR LOWER(COALESCE(repository_url,'')) LIKE $text OR LOWER(COALESCE(application_url,'')) LIKE $text OR LOWER(COALESCE(notes,'')) LIKE $text OR LOWER(COALESCE(tags,'')) LIKE $text)");
            parameters.Add(new KeyValuePair<string, object>("$text", "%" + query.SearchText!.ToLowerInvariant() + "%"));
        }
        sb.AppendLine(SortClause(query.SortBy));
        sb.AppendLine("LIMIT $take OFFSET $skip");
        parameters.Add(new KeyValuePair<string, object>("$take", EffectiveTake(query)));
        parameters.Add(new KeyValuePair<string, object>("$skip", query.Skip));
        return sb.ToString();
    }

    private static Application ReadApplicationRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        ParseStatus(reader.GetString(6)),
        ParseTags(reader.IsDBNull(7) ? null : reader.GetString(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
        reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture));

    private static async Task<Application?> ReadApplicationAsync(SqliteConnection conn, Guid id, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, owner, repository_url, application_url, status, tags, notes, created_at_utc, updated_at_utc, deleted_at_utc FROM applications WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadApplicationRow(reader);
    }

    private static async Task InsertApplicationAsync(
        SqliteConnection conn, SqliteTransaction tx, Application a, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO applications (id, name, description, owner, repository_url, application_url, status, tags, notes, created_at_utc, updated_at_utc, deleted_at_utc) VALUES ($id, $name, $desc, $owner, $repo, $appurl, $status, $tags, $notes, $created, $updated, $deleted);";
        cmd.Parameters.AddWithValue("$id", a.Id.ToString());
        cmd.Parameters.AddWithValue("$name", a.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)a.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$owner", (object?)a.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$repo", (object?)a.RepositoryUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$appurl", (object?)a.ApplicationUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", a.Status.ToString());
        cmd.Parameters.AddWithValue("$tags", (object?)SerializeTags(a.Tags) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)a.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", a.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", a.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$deleted", a.DeletedAtUtc.HasValue ? a.DeletedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpdateApplicationRowAsync(
        SqliteConnection conn, SqliteTransaction tx, Application a, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE applications SET name = $name, description = $desc, owner = $owner, repository_url = $repo, application_url = $appurl, status = $status, tags = $tags, notes = $notes, updated_at_utc = $updated WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", a.Id.ToString());
        cmd.Parameters.AddWithValue("$name", a.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)a.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$owner", (object?)a.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$repo", (object?)a.RepositoryUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$appurl", (object?)a.ApplicationUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", a.Status.ToString());
        cmd.Parameters.AddWithValue("$tags", (object?)SerializeTags(a.Tags) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)a.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", a.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task SetApplicationTombstoneAsync(Guid id, DateTimeOffset? when, CancellationToken ct)
    {
        await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE applications SET deleted_at_utc = $d, updated_at_utc = $u WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", when.HasValue ? when.Value.ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static RecordStatus ParseStatus(string s) =>
        Enum.TryParse<RecordStatus>(s, ignoreCase: true, out var r) ? r : RecordStatus.Active;

    private static IReadOnlyList<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var list = new List<string>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var s = e.GetString();
                if (s is not null) list.Add(s);
            }
            return list;
        }
        catch (System.Text.Json.JsonException) { return Array.Empty<string>(); }
    }

    private static string? SerializeTags(IReadOnlyList<string> tags)
    {
        if (tags is null || tags.Count == 0) return null;
        return System.Text.Json.JsonSerializer.Serialize(tags);
    }

    // ---- Records methods (part 2) ----
    public async Task<IReadOnlyList<InfrastructureRecord>> ListRecordsAsync(
        Guid applicationId, VaultQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePaging(query);
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        var sql = BuildRecordSearchSql(applicationId, query, out var parameters);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value);
        var rows = new List<InfrastructureRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadRecordRow(reader));
        }
        return (IReadOnlyList<InfrastructureRecord>)rows;
    }

    public async Task<InfrastructureRecord?> GetRecordAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadRecordAsync(conn, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InfrastructureRecord> CreateRecordAsync(
        InfrastructureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertRecordAsync(conn, (SqliteTransaction)tx, record, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    // ---- Records methods (part 2b) ----
    public async Task<InfrastructureRecord> UpdateRecordAsync(
        InfrastructureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpdateRecordRowAsync(conn, (SqliteTransaction)tx, record, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public Task SoftDeleteRecordAsync(Guid id, CancellationToken cancellationToken = default)
        => SetRecordTombstoneAsync(id, DateTimeOffset.UtcNow, cancellationToken);

    public Task RestoreRecordAsync(Guid id, CancellationToken cancellationToken = default)
        => SetRecordTombstoneAsync(id, null, cancellationToken);

    public async Task HardDeleteRecordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await _bootstrap.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM infrastructure_records WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetRecordTombstoneAsync(Guid id, DateTimeOffset? when, CancellationToken ct)
    {
        await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE infrastructure_records SET deleted_at_utc = $d, updated_at_utc = $u WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", when.HasValue ? when.Value.ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ---- Records helpers (part 2b-ii) ----
    private static string BuildRecordSearchSql(
        Guid applicationId, VaultQuery query, out List<KeyValuePair<string, object>> parameters)
    {
        parameters = new List<KeyValuePair<string, object>>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT id, application_id, name, provider, service_category, environment, dashboard_url, login_email, region, resource_id, status, notes, tags, created_at_utc, updated_at_utc, deleted_at_utc");
        sb.AppendLine("FROM infrastructure_records");
        sb.AppendLine("WHERE application_id = $aid");
        parameters.Add(new KeyValuePair<string, object>("$aid", applicationId.ToString()));
        if (!query.IncludeDeleted) sb.AppendLine("  AND deleted_at_utc IS NULL");
        if (query.StatusFilter.HasValue)
        {
            sb.AppendLine("  AND status = $status");
            parameters.Add(new KeyValuePair<string, object>("$status", query.StatusFilter.Value.ToString()));
        }
        if (query.CategoryFilter.HasValue)
        {
            sb.AppendLine("  AND service_category = $cat");
            parameters.Add(new KeyValuePair<string, object>("$cat", query.CategoryFilter.Value.ToString()));
        }
        if (query.EnvironmentFilter.HasValue)
        {
            sb.AppendLine("  AND environment = $env");
            parameters.Add(new KeyValuePair<string, object>("$env", query.EnvironmentFilter.Value.ToString()));
        }
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            sb.AppendLine("  AND (LOWER(name) LIKE $text OR LOWER(COALESCE(provider,'')) LIKE $text OR LOWER(COALESCE(dashboard_url,'')) LIKE $text OR LOWER(COALESCE(login_email,'')) LIKE $text OR LOWER(COALESCE(region,'')) LIKE $text OR LOWER(COALESCE(resource_id,'')) LIKE $text OR LOWER(COALESCE(notes,'')) LIKE $text OR LOWER(COALESCE(tags,'')) LIKE $text)");
            parameters.Add(new KeyValuePair<string, object>("$text", "%" + query.SearchText!.ToLowerInvariant() + "%"));
        }
        sb.AppendLine(SortClause(query.SortBy));
        sb.AppendLine("LIMIT $take OFFSET $skip");
        parameters.Add(new KeyValuePair<string, object>("$take", EffectiveTake(query)));
        parameters.Add(new KeyValuePair<string, object>("$skip", query.Skip));
        return sb.ToString();
    }

    private static InfrastructureRecord ReadRecordRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? ServiceCategory.Other : ParseCategory(reader.GetString(4)),
        reader.IsDBNull(5) ? EnvironmentKind.Other : ParseEnv(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        ParseStatus(reader.GetString(10)),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        ParseTags(reader.IsDBNull(12) ? null : reader.GetString(12)),
        DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture),
        reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture));

    private static ServiceCategory ParseCategory(string s) =>
        Enum.TryParse<ServiceCategory>(s, ignoreCase: true, out var r) ? r : ServiceCategory.Other;

    private static EnvironmentKind ParseEnv(string s) =>
        Enum.TryParse<EnvironmentKind>(s, ignoreCase: true, out var r) ? r : EnvironmentKind.Other;

    private static async Task<InfrastructureRecord?> ReadRecordAsync(SqliteConnection conn, Guid id, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, application_id, name, provider, service_category, environment, dashboard_url, login_email, region, resource_id, status, notes, tags, created_at_utc, updated_at_utc, deleted_at_utc FROM infrastructure_records WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadRecordRow(reader);
    }

    private static async Task InsertRecordAsync(
        SqliteConnection conn, SqliteTransaction tx, InfrastructureRecord r, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO infrastructure_records (id, application_id, name, provider, service_category, environment, dashboard_url, login_email, region, resource_id, status, notes, tags, created_at_utc, updated_at_utc, deleted_at_utc) VALUES ($id, $aid, $name, $prov, $cat, $env, $dash, $email, $region, $rid, $status, $notes, $tags, $created, $updated, $deleted);";
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$aid", r.ApplicationId.ToString());
        cmd.Parameters.AddWithValue("$name", r.Name);
        cmd.Parameters.AddWithValue("$prov", (object?)r.Provider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", r.ServiceCategory.ToString());
        cmd.Parameters.AddWithValue("$env", r.Environment.ToString());
        cmd.Parameters.AddWithValue("$dash", (object?)r.DashboardUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$email", (object?)r.LoginEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$region", (object?)r.Region ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rid", (object?)r.ResourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", r.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", (object?)r.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", (object?)SerializeTags(r.Tags) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", r.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", r.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$deleted", r.DeletedAtUtc.HasValue ? r.DeletedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpdateRecordRowAsync(
        SqliteConnection conn, SqliteTransaction tx, InfrastructureRecord r, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE infrastructure_records SET name = $name, provider = $prov, service_category = $cat, environment = $env, dashboard_url = $dash, login_email = $email, region = $region, resource_id = $rid, status = $status, notes = $notes, tags = $tags, updated_at_utc = $updated WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$name", r.Name);
        cmd.Parameters.AddWithValue("$prov", (object?)r.Provider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", r.ServiceCategory.ToString());
        cmd.Parameters.AddWithValue("$env", r.Environment.ToString());
        cmd.Parameters.AddWithValue("$dash", (object?)r.DashboardUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$email", (object?)r.LoginEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$region", (object?)r.Region ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rid", (object?)r.ResourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", r.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", (object?)r.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", (object?)SerializeTags(r.Tags) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", r.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}