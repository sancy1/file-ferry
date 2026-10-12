using Microsoft.Data.Sqlite;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Storage;

namespace SchemaLence.Platform.Storage;

public sealed class SqliteSchemaLenceLocalStore : ISchemaLenceLocalStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public SqliteSchemaLenceLocalStore(string databasePath)
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

    public void Dispose() => _initializeLock.Dispose();

    public async Task<IReadOnlyList<DatabaseConnectionProfile>> GetConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, provider_id, host, port, database_name,
                   username, ssl_mode
            FROM saved_connections
            ORDER BY name COLLATE NOCASE, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var profiles = new List<DatabaseConnectionProfile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(new DatabaseConnectionProfile(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }
        return profiles;
    }

    public async Task SaveConnectionAsync(
        DatabaseConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO saved_connections
                (id, name, provider_id, host, port, database_name, username, ssl_mode)
            VALUES
                ($id, $name, $provider, $host, $port, $database, $username, $ssl)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                provider_id = excluded.provider_id,
                host = excluded.host,
                port = excluded.port,
                database_name = excluded.database_name,
                username = excluded.username,
                ssl_mode = excluded.ssl_mode;
            """;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$provider", profile.ProviderId);
        command.Parameters.AddWithValue("$host", profile.Host);
        command.Parameters.AddWithValue("$port", profile.Port);
        command.Parameters.AddWithValue("$database", profile.Database);
        command.Parameters.AddWithValue("$username", profile.Username);
        command.Parameters.AddWithValue("$ssl", profile.SslMode);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteConnectionAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM saved_connections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", profileId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddBackupHistoryAsync(
        BackupHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO backup_history
                (id, connection_name, database_name, scope, format, destination, file_size, created_at)
            VALUES
                ($id, $connection, $database, $scope, $format, $destination, $size, $created);
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$connection", entry.ConnectionName);
        command.Parameters.AddWithValue("$database", entry.DatabaseName);
        command.Parameters.AddWithValue("$scope", entry.Scope);
        command.Parameters.AddWithValue("$format", entry.Format);
        command.Parameters.AddWithValue("$destination", entry.Destination);
        command.Parameters.AddWithValue("$size", entry.FileSize);
        command.Parameters.AddWithValue("$created", entry.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BackupHistoryEntry>> GetBackupHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, connection_name, database_name, scope, format,
                   destination, file_size, created_at
            FROM backup_history
            ORDER BY created_at DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<BackupHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new BackupHistoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                DateTimeOffset.Parse(
                    reader.GetString(7),
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
        return entries;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM saved_connections; DELETE FROM backup_history;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
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
            var currentVersion = await ReadSchemaVersionAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            if (currentVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    "The local SchemaLence database was created by a newer app version.");
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS saved_connections (
                    id TEXT PRIMARY KEY NOT NULL,
                    name TEXT NOT NULL,
                    provider_id TEXT NOT NULL,
                    host TEXT NOT NULL,
                    port INTEGER NOT NULL,
                    database_name TEXT NOT NULL,
                    username TEXT NOT NULL,
                    ssl_mode TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS backup_history (
                    id TEXT PRIMARY KEY NOT NULL,
                    connection_name TEXT NOT NULL,
                    database_name TEXT NOT NULL,
                    scope TEXT NOT NULL,
                    format TEXT NOT NULL,
                    destination TEXT NOT NULL,
                    file_size INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );
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
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
