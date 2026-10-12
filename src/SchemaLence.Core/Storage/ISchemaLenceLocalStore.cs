using SchemaLence.Core.Connections;

namespace SchemaLence.Core.Storage;

public sealed record BackupHistoryEntry(
    string Id,
    string ConnectionName,
    string DatabaseName,
    string Scope,
    string Format,
    string Destination,
    long FileSize,
    DateTimeOffset CreatedAt);

public interface ISchemaLenceLocalStore
{
    Task<IReadOnlyList<DatabaseConnectionProfile>> GetConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task SaveConnectionAsync(
        DatabaseConnectionProfile profile,
        CancellationToken cancellationToken = default);

    Task DeleteConnectionAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task AddBackupHistoryAsync(
        BackupHistoryEntry entry,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupHistoryEntry>> GetBackupHistoryAsync(
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
