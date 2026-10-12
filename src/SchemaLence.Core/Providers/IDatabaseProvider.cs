using SchemaLence.Core.Connections;
using SchemaLence.Core.Metadata;

namespace SchemaLence.Core.Providers;

public interface IDatabaseProvider
{
    string ProviderId { get; }
    ProviderCapabilities Capabilities { get; }

    Task<ConnectionTestResult> TestConnectionAsync(
        DatabaseConnectionInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseNamespace>> ListNamespacesAsync(
        DatabaseConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseObject>> ListObjectsAsync(
        DatabaseConnectionProfile profile,
        string password,
        string? namespaceFilter,
        CancellationToken cancellationToken = default);

    Task<DatabaseObjectStructure> GetStructureAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        CancellationToken cancellationToken = default);

    Task<DatabasePage> GetPageAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        int pageIndex,
        int pageSize,
        string? sortColumn,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<long> CountRowsAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        CancellationToken cancellationToken = default);

    Task StreamRowsAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        int batchSize,
        Func<IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string?>>, Task> writeBatchAsync,
        CancellationToken cancellationToken = default);
}

public interface IPostgresBackupService
{
    Task CreateCustomArchiveAsync(
        DatabaseConnectionProfile profile,
        string password,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
