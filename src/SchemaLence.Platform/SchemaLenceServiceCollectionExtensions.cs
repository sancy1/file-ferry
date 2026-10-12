using Microsoft.Extensions.DependencyInjection;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Providers;
using SchemaLence.Core.Storage;
using SchemaLence.Platform.Postgres;
using SchemaLence.Platform.Storage;
using FileHarvester.Platform.Cloud.Credentials;

namespace SchemaLence.Platform;

public static class SchemaLenceServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaLence(
        this IServiceCollection services,
        string applicationDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);

        var localDirectory = Path.Combine(applicationDataDirectory, "SchemaLence");
        Directory.CreateDirectory(localDirectory);

        services.AddSingleton<ISchemaLenceLocalStore>(_ =>
            new SqliteSchemaLenceLocalStore(
                Path.Combine(localDirectory, "schema-lence.db")));
        services.AddSingleton<ISchemaLenceCredentialStore>(provider =>
            new SchemaLenceCredentialStore(
                provider.GetRequiredService<ICredentialStore>()));
        services.AddSingleton<IDatabaseProvider, PostgresDatabaseProvider>();
        services.AddSingleton<IPostgresBackupService, PostgresBackupService>();
        return services;
    }
}
