// ============================================================================
// FILE:     src/Prompt.Platform/Storage/SqlitePromptStore.cs
// PURPOSE:  SQLite implementation of IPromptStore. CRUD with PUT (full
//           replace) and PATCH (via PromptPatch - F-001), soft delete,
//           restore, hard delete, and tag attach/detach. Every operation
//           wrapped in the prompt-store resilience policy.
// RELATION: Implements IPromptStore. Consumed by PromptListViewModel (P2.3),
//           PromptEditorViewModel (P2.5), PromptViewerViewModel (P2.4).
//           Uses PromptSchemaBootstrap (P1.1) for connections.
// NOTES:    - PATCH is via PromptPatch, not Action<Prompt> (F-001).
//           - The record type Prompt.Core.Domain.Prompt collides with the
//             root namespace Prompt inside namespace Prompt.Platform.Storage,
//             so it is imported under the alias PromptRecord (F-084).
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Observability;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace Prompt.Platform.Storage;

/// <summary>
/// SQLite implementation of <see cref="IPromptStore"/>.
/// </summary>
public sealed class SqlitePromptStore : IPromptStore
{
    private readonly PromptSchemaBootstrap _bootstrap;
    private readonly IResilienceExecutor _executor;
    private readonly IPromptVersionStore _versionStore;

    /// <summary>
    /// Builds a store against the given bootstrap and executor.
    /// </summary>
    /// <summary>
    /// The operation-event emitter. Never null; see the constructor.
    /// </summary>
    private readonly IPromptEventEmitter _events;

    public SqlitePromptStore(
        PromptSchemaBootstrap bootstrap,
        IResilienceExecutor executor,
        IPromptVersionStore versionStore,
        IPromptEventEmitter? events = null)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(versionStore);
        _bootstrap = bootstrap;
        _executor = executor;
        _versionStore = versionStore;
        // WHAT: default the emitter to the no-op so the store never null-checks.
        // WHY:  preserves every existing direct construction; the app supplies
        //       a real emitter via AddPromptStores.
        // NOT:  a required parameter, which would break every existing caller.
        _events = events ?? NullPromptEventEmitter.Instance;
    }

    /// <inheritdoc/>
    public async Task<PromptRecord> CreateAsync(PromptRecord prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var created = await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                var previous = await ReadPromptAsync(conn, prompt.Id, ct).ConfigureAwait(false);
                if (previous is not null)
                {
                    await ((IPromptVersionStoreTransactional)_versionStore).SnapshotAsync(conn, (SqliteTransaction)tx, previous, null, ct).ConfigureAwait(false);
                }
                await InsertOrReplacePromptAsync(conn, (SqliteTransaction)tx, prompt, ct).ConfigureAwait(false);
                await ReplaceTagsAsync(conn, (SqliteTransaction)tx, prompt.Id, prompt.Tags, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return prompt;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        // WHAT: emit after the outer resilience call reports success.
        // WHY:  an event on a retry-then-fail call would lie about success.
        _events.Emit(PromptEventNames.Created, created.Id);
        return created;
    }

    /// <inheritdoc/>
    public Task<PromptRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                return await ReadPromptAsync(conn, id, ct).ConfigureAwait(false);
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PromptRecord> UpdateAsync(PromptRecord prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var updated = await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                var previous = await ReadPromptAsync(conn, prompt.Id, ct).ConfigureAwait(false);
                if (previous is not null)
                {
                    await ((IPromptVersionStoreTransactional)_versionStore).SnapshotAsync(conn, (SqliteTransaction)tx, previous, null, ct).ConfigureAwait(false);
                }
                await InsertOrReplacePromptAsync(conn, (SqliteTransaction)tx, prompt, ct).ConfigureAwait(false);
                await ReplaceTagsAsync(conn, (SqliteTransaction)tx, prompt.Id, prompt.Tags, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return prompt;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        // See CreateAsync for the emit-after-await rationale.
        _events.Emit(PromptEventNames.Updated, updated.Id);
        return updated;
    }

    /// <inheritdoc/>
    public async Task<PromptRecord> PatchAsync(Guid id, PromptPatch patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var patchedRecord = await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

                var existing = await ReadPromptAsync(conn, id, ct).ConfigureAwait(false);
                if (existing is null)
                {
                    throw new InvalidOperationException($"Prompt {id} does not exist.");
                }

                await ((IPromptVersionStoreTransactional)_versionStore).SnapshotAsync(conn, (SqliteTransaction)tx, existing, null, ct).ConfigureAwait(false);

                var patched = ApplyPatch(existing, patch);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = (SqliteTransaction)tx;
                    cmd.CommandText = """
                        UPDATE Prompts SET
                            Title = $title,
                            Body = $body,
                            Format = $format,
                            CategoryId = $cat,
                            SubCategoryId = $subcat,
                            TargetModel = $target,
                            IsFavorite = $fav,
                            UpdatedAt = $updated
                        WHERE Id = $id;
                        """;
                    cmd.Parameters.AddWithValue("$title", patched.Title);
                    cmd.Parameters.AddWithValue("$body", patched.Body);
                    cmd.Parameters.AddWithValue("$format", (int)patched.Format);
                    cmd.Parameters.AddWithValue("$cat", (object?)patched.CategoryId?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$subcat", (object?)patched.SubCategoryId?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$target", (object?)patched.TargetModel ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$fav", patched.IsFavorite ? 1 : 0);
                    cmd.Parameters.AddWithValue("$updated", patched.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("$id", id.ToString());
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                if (patch.Tags.IsSpecified)
                {
                    await ReplaceTagsAsync(conn, (SqliteTransaction)tx, id, patched.Tags, ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return patched;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        _events.Emit(PromptEventNames.Updated, patchedRecord.Id);
        return patchedRecord;
    }
    /// <inheritdoc/>
    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Prompts SET IsDeleted = 1, UpdatedAt = $updated WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        _events.Emit(PromptEventNames.Deleted, id);
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Prompts SET IsDeleted = 0, UpdatedAt = $updated WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        _events.Emit(PromptEventNames.Restored, id);
    }

    /// <inheritdoc/>
    public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Prompts WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task AttachTagAsync(Guid promptId, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                await using (var tagCmd = conn.CreateCommand())
                {
                    tagCmd.Transaction = (SqliteTransaction)tx;
                    tagCmd.CommandText = "INSERT OR IGNORE INTO Tags (Id, Name) VALUES ($id, $name);";
                    tagCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                    tagCmd.Parameters.AddWithValue("$name", tag);
                    await tagCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await using (var linkCmd = conn.CreateCommand())
                {
                    linkCmd.Transaction = (SqliteTransaction)tx;
                    linkCmd.CommandText = "INSERT OR IGNORE INTO PromptTags (PromptId, TagId) SELECT $pid, Id FROM Tags WHERE Name = $name;";
                    linkCmd.Parameters.AddWithValue("$pid", promptId.ToString());
                    linkCmd.Parameters.AddWithValue("$name", tag);
                    await linkCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task DetachTagAsync(Guid promptId, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM PromptTags WHERE PromptId = $pid AND TagId = (SELECT Id FROM Tags WHERE Name = $name);";
                cmd.Parameters.AddWithValue("$pid", promptId.ToString());
                cmd.Parameters.AddWithValue("$name", tag);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    // ---- private helpers ----

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PromptRecord>> SearchAsync(PromptQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var results = await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                var sql = BuildSearchSql(query, out var parameters);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value);
                var rows = new List<PromptRecord>();
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rows.Add(ReadPromptRow(reader));
                }
                var result = new List<PromptRecord>(rows.Count);
                foreach (var row in rows)
                {
                    var tags = await LoadTagsAsync(conn, row.Id, ct).ConfigureAwait(false);
                    result.Add(row with { Tags = tags });
                }
                return (IReadOnlyList<PromptRecord>)result;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        // Search is not single-prompt, so the id is omitted.
        _events.Emit(PromptEventNames.SearchExecuted);
        return results;
    }

    private static async Task InsertOrReplacePromptAsync(
        SqliteConnection conn, SqliteTransaction tx, PromptRecord p, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO Prompts
                (Id, Title, Body, Format, CategoryId, SubCategoryId, TargetModel,
                 IsFavorite, IsDeleted, CreatedAt, UpdatedAt)
            VALUES
                ($id, $title, $body, $format, $cat, $subcat, $target, $fav, $del, $created, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Body = excluded.Body,
                Format = excluded.Format,
                CategoryId = excluded.CategoryId,
                SubCategoryId = excluded.SubCategoryId,
                TargetModel = excluded.TargetModel,
                IsFavorite = excluded.IsFavorite,
                UpdatedAt = excluded.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$id", p.Id.ToString());
        cmd.Parameters.AddWithValue("$title", p.Title);
        cmd.Parameters.AddWithValue("$body", p.Body);
        cmd.Parameters.AddWithValue("$format", (int)p.Format);
        cmd.Parameters.AddWithValue("$cat", (object?)p.CategoryId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$subcat", (object?)p.SubCategoryId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$target", (object?)p.TargetModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fav", p.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$del", p.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", p.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", p.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task ReplaceTagsAsync(
        SqliteConnection conn, SqliteTransaction tx, Guid promptId, IReadOnlyList<string> tags, CancellationToken ct)
    {
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM PromptTags WHERE PromptId = $pid;";
            del.Parameters.AddWithValue("$pid", promptId.ToString());
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var tag in tags)
        {
            await using var tagCmd = conn.CreateCommand();
            tagCmd.Transaction = tx;
            tagCmd.CommandText = "INSERT OR IGNORE INTO Tags (Id, Name) VALUES ($id, $name);";
            tagCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            tagCmd.Parameters.AddWithValue("$name", tag);
            await tagCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var linkCmd = conn.CreateCommand();
            linkCmd.Transaction = tx;
            linkCmd.CommandText = "INSERT OR IGNORE INTO PromptTags (PromptId, TagId) SELECT $pid, Id FROM Tags WHERE Name = $name;";
            linkCmd.Parameters.AddWithValue("$pid", promptId.ToString());
            linkCmd.Parameters.AddWithValue("$name", tag);
            await linkCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static string BuildSearchSql(PromptQuery query, out List<KeyValuePair<string, object>> parameters)
    {
        parameters = new List<KeyValuePair<string, object>>();
        var hasText = !string.IsNullOrWhiteSpace(query.Text);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT p.Id, p.Title, p.Body, p.Format, p.CategoryId, p.SubCategoryId, p.TargetModel, p.IsFavorite, p.IsDeleted, p.CreatedAt, p.UpdatedAt");
        if (hasText)
        {
            sb.AppendLine("FROM Prompts p INNER JOIN PromptsFts ON PromptsFts.rowid = p.rowid");
            sb.AppendLine("WHERE PromptsFts MATCH $text");
            parameters.Add(new KeyValuePair<string, object>("$text", query.Text! + "*"));
        }
        else
        {
            sb.AppendLine("FROM Prompts p");
            sb.AppendLine("WHERE 1 = 1");
        }
        if (query.CategoryId.HasValue)
        {
            sb.AppendLine("  AND p.CategoryId = $cat");
            parameters.Add(new KeyValuePair<string, object>("$cat", query.CategoryId.Value.ToString()));
        }
        if (query.SubCategoryId.HasValue)
        {
            sb.AppendLine("  AND p.SubCategoryId = $subcat");
            parameters.Add(new KeyValuePair<string, object>("$subcat", query.SubCategoryId.Value.ToString()));
        }
        if (query.FavoritesOnly) sb.AppendLine("  AND p.IsFavorite = 1");
        if (!query.IncludeDeleted) sb.AppendLine("  AND p.IsDeleted = 0");
        if (query.Tags is { Count: > 0 })
        {
            var placeholders = new List<string>();
            for (int i = 0; i < query.Tags.Count; i++)
            {
                var name = $"$tag{i}";
                placeholders.Add(name);
                parameters.Add(new KeyValuePair<string, object>(name, query.Tags[i]));
            }
            sb.AppendLine(CultureInfo.InvariantCulture, $"  AND p.Id IN (SELECT pt.PromptId FROM PromptTags pt INNER JOIN Tags t ON t.Id = pt.TagId WHERE t.Name IN ({string.Join(", ", placeholders)}))");
        }
        var limit = query.Take > 0 ? query.Take : 100;
        sb.AppendLine(hasText ? "ORDER BY rank" : "ORDER BY p.UpdatedAt DESC");
        sb.AppendLine("LIMIT $take OFFSET $skip");
        parameters.Add(new KeyValuePair<string, object>("$take", limit));
        parameters.Add(new KeyValuePair<string, object>("$skip", query.Skip));
        return sb.ToString();
    }

    private static PromptRecord ReadPromptRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        (BodyFormat)reader.GetInt32(3),
        reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
        reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetInt32(7) != 0,
        reader.GetInt32(8) != 0,
        Array.Empty<string>(),
        DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture));

    private static async Task<List<string>> LoadTagsAsync(SqliteConnection conn, Guid promptId, CancellationToken ct)
    {
        var tags = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT t.Name FROM Tags t INNER JOIN PromptTags pt ON pt.TagId = t.Id WHERE pt.PromptId = $pid ORDER BY t.Name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$pid", promptId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            tags.Add(reader.GetString(0));
        }
        return tags;
    }

    private static async Task<PromptRecord?> ReadPromptAsync(
        SqliteConnection conn, Guid id, CancellationToken ct)
    {
        PromptRecord? prompt = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, Title, Body, Format, CategoryId, SubCategoryId, TargetModel,
                       IsFavorite, IsDeleted, CreatedAt, UpdatedAt
                FROM Prompts WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                prompt = new PromptRecord(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    (BodyFormat)reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt32(7) != 0,
                    reader.GetInt32(8) != 0,
                    Array.Empty<string>(),
                    DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture));
            }
        }
        if (prompt is null) return null;
        var tags = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT t.Name FROM Tags t INNER JOIN PromptTags pt ON pt.TagId = t.Id WHERE pt.PromptId = $pid ORDER BY t.Name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$pid", id.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) tags.Add(reader.GetString(0));
        }
        return prompt with { Tags = tags };
    }

    private static PromptRecord ApplyPatch(PromptRecord existing, PromptPatch patch) => existing with
    {
        Title = patch.Title.IsSpecified ? patch.Title.Value! : existing.Title,
        Body = patch.Body.IsSpecified ? patch.Body.Value! : existing.Body,
        Format = patch.Format.IsSpecified ? patch.Format.Value : existing.Format,
        CategoryId = patch.CategoryId.IsSpecified ? patch.CategoryId.Value : existing.CategoryId,
        SubCategoryId = patch.SubCategoryId.IsSpecified ? patch.SubCategoryId.Value : existing.SubCategoryId,
        TargetModel = patch.TargetModel.IsSpecified ? patch.TargetModel.Value : existing.TargetModel,
        IsFavorite = patch.IsFavorite.IsSpecified ? patch.IsFavorite.Value : existing.IsFavorite,
        Tags = patch.Tags.IsSpecified ? (patch.Tags.Value ?? Array.Empty<string>()) : existing.Tags,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <inheritdoc/>
    public Task SaveDraftAsync(PromptDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO PromptDrafts (PromptId, Title, Body, SavedAt) VALUES ($pid, $title, $body, $saved);";
                cmd.Parameters.AddWithValue("$pid", draft.PromptId.ToString());
                cmd.Parameters.AddWithValue("$title", (object?)draft.Title ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$body", (object?)draft.Body ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$saved", draft.SavedAt.ToString("O", CultureInfo.InvariantCulture));
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<PromptDraft?> GetDraftAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT PromptId, Title, Body, SavedAt FROM PromptDrafts WHERE PromptId = $pid;";
                cmd.Parameters.AddWithValue("$pid", promptId.ToString());
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return (PromptDraft?)null;
                return new PromptDraft(
                    Guid.Parse(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task ClearDraftAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM PromptDrafts WHERE PromptId = $pid;";
                cmd.Parameters.AddWithValue("$pid", promptId.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }
}