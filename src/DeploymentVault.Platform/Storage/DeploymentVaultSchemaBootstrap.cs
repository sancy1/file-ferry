// ============================================================================
// filepath: src/DeploymentVault.Platform/Storage/DeploymentVaultSchemaBootstrap.cs
// layer:    Infrastructure
// module:   DeploymentVault.Platform.Storage
// since:    Phase A (Deployment Vault registry)
// purpose:  Creates and migrates the Deployment Vault SQLite schema. Owns
//           the PRAGMA user_version gate and the CREATE TABLE block for the
//           two Phase A tables (applications, infrastructure_records).
//           One class; called by the store before every operation.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : Microsoft.Data.Sqlite
//   Used by    : SqliteDeploymentVaultStore (one instance, shared)
//   See also   : SqliteDeploymentVaultStore, PromptSchemaBootstrap
//
// NOTES
//   - Pattern mirrors Prompt.Platform.Storage.PromptSchemaBootstrap, minus
//     the Portfolio.Resilience executor. The vault has no resilience policy
//     in Phase A; every failure throws directly to the caller, which is
//     correct for a local file: a broken SQLite file is not a transient
//     network failure.
//   - Schema is created once, lazily, on first OpenAsync. Subsequent calls
//     take the fast path (a volatile bool check) and do not take the lock.
//   - PRAGMA user_version is the schema version. A forward version (a newer
//     database opened by an older app) throws instead of silently
//     corrupting. The message is specific so the user knows what to do.
//   - Phase A creates two tables: applications and infrastructure_records.
//     The secrets and attachments tables are Phase B/C. Do not create empty
//     tables in advance; the phase that uses a table creates it.
//   - The partial unique index on LOWER(name) WHERE deleted_at_utc IS NULL
//     enforces case-insensitive uniqueness among active applications only.
//     An archived-then-recreated same-name application is allowed.
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DeploymentVault.Platform.Storage;

/// <summary>
/// Creates and migrates the Deployment Vault SQLite schema.
/// </summary>
/// <remarks>
/// Idempotent. The schema is created once, on first open. A forward schema
/// version (a newer database opened by an older app) throws instead of
/// silently corrupting. No connection is held open between calls; every
/// OpenAsync returns a fresh connection the caller owns.
/// </remarks>
public sealed class DeploymentVaultSchemaBootstrap : IDisposable
{
    /// <summary>The schema version this build creates and expects.</summary>
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    /// <summary>
    /// Builds a bootstrap instance for a database at <paramref name="databasePath"/>.
    /// </summary>
    /// <param name="databasePath">
    /// Absolute path of the SQLite database file. The parent directory is
    /// created if missing. Never null or whitespace.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="databasePath"/> is null, empty, or whitespace.
    /// </exception>
    public DeploymentVaultSchemaBootstrap(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    /// <summary>
    /// Disposes the initialization lock. Held no connections are closed
    /// here; the bootstrap does not hold any.
    /// </summary>
    public void Dispose() => _initializeLock.Dispose();

    /// <summary>
    /// Opens a new connection. Ensures the schema exists first. The caller
    /// owns the connection and is responsible for closing it. PRAGMA
    /// foreign_keys is enabled on every opened connection so the cascade
    /// from infrastructure_records to applications is enforced.
    /// </summary>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>An open <see cref="SqliteConnection"/>. Never null.</returns>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return connection;
    }

    /// <summary>
    /// Ensures the schema exists. Idempotent. Called by
    /// <see cref="OpenAsync"/> before every connection; the fast path
    /// (already initialized) takes a single volatile read.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }
        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var currentVersion = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (currentVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    "The local Deployment Vault database was created by a newer app version.");
            }
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS applications (
                    id TEXT PRIMARY KEY NOT NULL,
                    name TEXT NOT NULL,
                    description TEXT NULL,
                    owner TEXT NULL,
                    repository_url TEXT NULL,
                    application_url TEXT NULL,
                    status TEXT NOT NULL DEFAULT 'Active',
                    tags TEXT NULL,
                    notes TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    deleted_at_utc TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_applications_name_lower
                    ON applications (LOWER(name))
                    WHERE deleted_at_utc IS NULL;
                CREATE TABLE IF NOT EXISTS infrastructure_records (
                    id TEXT PRIMARY KEY NOT NULL,
                    application_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    provider TEXT NULL,
                    service_category TEXT NULL,
                    environment TEXT NULL,
                    dashboard_url TEXT NULL,
                    login_email TEXT NULL,
                    region TEXT NULL,
                    resource_id TEXT NULL,
                    status TEXT NOT NULL DEFAULT 'Active',
                    notes TEXT NULL,
                    tags TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    deleted_at_utc TEXT NULL,
                    FOREIGN KEY (application_id) REFERENCES applications(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_infra_app_id
                    ON infrastructure_records (application_id);
                CREATE INDEX IF NOT EXISTS ix_infra_status
                    ON infrastructure_records (status)
                    WHERE deleted_at_utc IS NULL;
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }
}