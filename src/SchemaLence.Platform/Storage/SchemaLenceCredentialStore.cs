using FileHarvester.Platform.Cloud.Credentials;
using SchemaLence.Core.Connections;

namespace SchemaLence.Platform.Storage;

public sealed class SchemaLenceCredentialStore : ISchemaLenceCredentialStore
{
    private const string ProfileId = "schema-lence-postgresql";
    private readonly ICredentialStore _store;

    public SchemaLenceCredentialStore(ICredentialStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task SavePasswordAsync(
        string connectionId,
        string password,
        CancellationToken cancellationToken = default) =>
        _store.WriteAsync(Key(connectionId), password, cancellationToken);

    public Task<string?> GetPasswordAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(Key(connectionId), cancellationToken);

    public Task DeletePasswordAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(Key(connectionId), cancellationToken);

    private static CredentialKey Key(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (connectionId.Contains('/'))
        {
            throw new ArgumentException("Connection ID is invalid.", nameof(connectionId));
        }
        return new CredentialKey(ProfileId, connectionId);
    }
}
