// ============================================================================
// FILE:     src/Prompt.Platform/Storage/PromptSchemaBootstrap.cs
// PURPOSE:  Creates and migrates the Prompt App SQLite schema. Owns the
//           PRAGMA user_version gate and the CREATE TABLE IF NOT EXISTS
//           block for the seven Prompt tables, the FTS5 virtual table, and
//           its three sync triggers. One class, called by every store
//           before its first operation.
// RELATION: Referenced by SqliteCategoryStore (P1.2), SqlitePromptStore
//           (P1.3), SqlitePromptVersionStore (P1.4), and the search path
//           (P1.5). Pattern mirrors SqliteSchemaLenceLocalStore.cs.
// NOTES:    - Seven tables, not six (F-077): Categories, SubCategories,
//             Prompts, Tags, PromptTags, PromptVersions, PromptDrafts.
//           - FTS5 virtual table plus three triggers keep PromptsFts in
//             sync with Prompts (no Content= clause; the virtual table
//             holds copies).
//           - Schema creation is idempotent (CREATE TABLE IF NOT EXISTS)
//             and wrapped in the prompt-store resilience policy.
//           - Forward-version throw (currentVersion > CurrentSchemaVersion)
//             prevents an older app from opening a newer schema.
//           - Store classes call EnsureInitializedAsync before their first
//             query. This class is the only place schema SQL lives.
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Platform.Composition;

namespace Prompt.Platform.Storage;

/// <summary>
/// Creates and migrates the Prompt App SQLite schema.
/// </summary>
/// <remarks>
/// The schema is created once, lazily, on the first store operation. A
/// forward schema version (a newer database opened by an older app) throws
/// instead of silently corrupting. The schema creation is wrapped in the
/// <c>prompt-store</c> resilience policy; the operation is idempotent
/// (<c>CREATE TABLE IF NOT EXISTS</c>) so retries are safe.
/// </remarks>
public sealed class PromptSchemaBootstrap : IDisposable
{
    /// <summary>The schema version this build creates and expects.</summary>
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly IResilienceExecutor _executor;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Builds a bootstrap instance for a database at <paramref name="databasePath"/>.
    /// </summary>
    /// <param name="databasePath">
    /// Absolute path of the SQLite database file. The parent directory is
    /// created if missing. Never null or whitespace.
    /// </param>
    /// <param name="executor">
    /// The resilience executor used to wrap the schema-creation operation
    /// under the <c>prompt-store</c> policy. Never null.
    /// </param>
    public PromptSchemaBootstrap(string databasePath, IResilienceExecutor executor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(executor);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        _executor = executor;
    }

    /// <summary>Disposes the initialization lock.</summary>
    public void Dispose() => _initializeLock.Dispose();

    /// <summary>
    /// Ensures the schema exists. Idempotent. Called by every store before
    /// its first query.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }
        await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await EnsureInitializedInternalAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null,
            idempotencyKey: null,
            timeBudgetMs: null,
            ct: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a new connection to the database. The caller owns the connection
    /// and is responsible for closing it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
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

    private async Task EnsureInitializedInternalAsync(CancellationToken cancellationToken)
    {
        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var currentVersion = await ReadSchemaVersionAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            if (currentVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    "The local Prompt database was created by a newer app version.");
            }
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Categories (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS SubCategories (
                    Id TEXT PRIMARY KEY NOT NULL,
                    CategoryId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS Prompts (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Title TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    Format INTEGER NOT NULL,
                    CategoryId TEXT NULL,
                    SubCategoryId TEXT NULL,
                    TargetModel TEXT NULL,
                    IsFavorite INTEGER NOT NULL,
                    IsDeleted INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE SET NULL,
                    FOREIGN KEY (SubCategoryId) REFERENCES SubCategories(Id) ON DELETE SET NULL
                );
                CREATE TABLE IF NOT EXISTS Tags (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Name TEXT NOT NULL UNIQUE
                );
                CREATE TABLE IF NOT EXISTS PromptTags (
                    PromptId TEXT NOT NULL,
                    TagId TEXT NOT NULL,
                    PRIMARY KEY (PromptId, TagId),
                    FOREIGN KEY (PromptId) REFERENCES Prompts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS PromptVersions (
                    Id TEXT PRIMARY KEY NOT NULL,
                    PromptId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    SavedAt TEXT NOT NULL,
                    Note TEXT NULL,
                    FOREIGN KEY (PromptId) REFERENCES Prompts(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS PromptDrafts (
                    PromptId TEXT PRIMARY KEY NOT NULL,
                    Title TEXT NULL,
                    Body TEXT NULL,
                    SavedAt TEXT NOT NULL
                );
                CREATE VIRTUAL TABLE IF NOT EXISTS PromptsFts USING fts5(
                    Title,
                    Body,
                    tokenize = 'unicode61'
                );
                CREATE TRIGGER IF NOT EXISTS PromptsFtsInsert AFTER INSERT ON Prompts BEGIN
                    INSERT INTO PromptsFts(rowid, Title, Body) VALUES (new.rowid, new.Title, new.Body);
                END;
                CREATE TRIGGER IF NOT EXISTS PromptsFtsUpdate AFTER UPDATE ON Prompts BEGIN
                    DELETE FROM PromptsFts WHERE rowid = old.rowid;
                    INSERT INTO PromptsFts(rowid, Title, Body) VALUES (new.rowid, new.Title, new.Body);
                END;
                CREATE TRIGGER IF NOT EXISTS PromptsFtsDelete AFTER DELETE ON Prompts BEGIN
                    DELETE FROM PromptsFts WHERE rowid = old.rowid;
                END;
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
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }
}