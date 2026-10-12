namespace SchemaLence.Core.Connections;

public interface ISchemaLenceCredentialStore
{
    Task SavePasswordAsync(
        string connectionId,
        string password,
        CancellationToken cancellationToken = default);

    Task<string?> GetPasswordAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task DeletePasswordAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
