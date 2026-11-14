// ============================================================================
// FILE:     src/Prompt.Platform/Composition/PromptStoreServiceCollectionExtensions.cs
// PURPOSE:  Registers the four Prompt store implementations and their
//           interfaces in the DI container. Called by the shell's
//           App.axaml.cs AFTER AddPromptFeature (resilience) and AFTER
//           AddFileHarvester (the app's IResilienceExecutor).
// RELATION: Consumes PromptSchemaBootstrap, SqliteCategoryStore,
//           SqlitePromptStore, SqlitePromptVersionStore. Registered by
//           App.axaml.cs. Resilience is provided by AddPromptFeature and
//           AddFileHarvester - this extension does not re-register it.
// NOTES:    - Registration order: AddPromptFeature (resilience policies)
//             must run BEFORE AddFileHarvester so the policies ride the
//             executor's policy store (P0.2 inversion). AddPromptStores
//             runs AFTER AddFileHarvester so the stores resolve the
//             executor it registered.
//           - PromptSchemaBootstrap is a singleton - every store shares it.
//           - The database path is ApplicationData/Prompt/prompt.db. The
//             parent directory is created by PromptSchemaBootstrap's
//             constructor (Directory.CreateDirectory).
//           - All store singletons are stateless apart from their
//             bootstrap reference; sharing them across VMs is correct.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Microsoft.Extensions.DependencyInjection;
using Prompt.Platform.Storage;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Export;
using Prompt.Platform.Import;
using Prompt.Platform.Observability;

namespace Prompt.Platform.Composition;

/// <summary>
/// DI registration for the Prompt App's four SQLite stores.
/// </summary>
public static class PromptStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers PromptSchemaBootstrap, SqliteCategoryStore, SqlitePromptStore,
    /// and SqlitePromptVersionStore as singletons against their interfaces.
    /// Call after AddPromptFeature (resilience) and AddFileHarvester (the
    /// app's IResilienceExecutor).
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <param name="databasePath">Absolute path of the Prompt database file.</param>
    public static IServiceCollection AddPromptStores(
        this IServiceCollection services,
        string databasePath,
        ILogSink? logSink = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        // P5.1: the emitter. When the caller supplies a log sink (the app
        // does, via App.LogSink), events flow to the Log Drawer. When it
        // does not (tests, headless composition), NullPromptEventEmitter
        // discards them so the stores can emit unconditionally.
        if (logSink is not null)
        {
            services.AddSingleton<IPromptEventEmitter>(new PromptEventEmitter(new[] { logSink }));
        }
        else
        {
            services.AddSingleton<IPromptEventEmitter>(NullPromptEventEmitter.Instance);
        }

        services.AddSingleton(sp => new PromptSchemaBootstrap(
            databasePath,
            sp.GetRequiredService<IResilienceExecutor>()));

        services.AddSingleton(sp => new SqlitePromptVersionStore(
            sp.GetRequiredService<PromptSchemaBootstrap>(),
            sp.GetRequiredService<IResilienceExecutor>()));

        services.AddSingleton<IPromptVersionStore>(sp =>
            sp.GetRequiredService<SqlitePromptVersionStore>());

        services.AddSingleton(sp => new SqliteCategoryStore(
            sp.GetRequiredService<PromptSchemaBootstrap>(),
            sp.GetRequiredService<IResilienceExecutor>()));

        services.AddSingleton<ICategoryStore>(sp =>
            sp.GetRequiredService<SqliteCategoryStore>());

        services.AddSingleton<ISubCategoryStore>(sp =>
            sp.GetRequiredService<SqliteCategoryStore>());

        services.AddSingleton(sp => new SqlitePromptStore(
            sp.GetRequiredService<PromptSchemaBootstrap>(),
            sp.GetRequiredService<IResilienceExecutor>(),
            sp.GetRequiredService<IPromptVersionStore>()));

        services.AddSingleton<IPromptStore>(sp =>
            sp.GetRequiredService<SqlitePromptStore>());

        // P3.4: the IPromptExporter. Five dependencies: the writer registry
        // and the overwrite confirmer (both from the harvester's
        // AddFileHarvester), the resilience executor (also from the
        // harvester), and the two category stores (already registered above).
        services.AddSingleton<IPromptExporter>(sp => new PromptExporter(
            sp.GetRequiredService<IFormatWriterRegistry>(),
            sp.GetRequiredService<IResilienceExecutor>(),
            sp.GetRequiredService<IOverwriteConfirmer>(),
            sp.GetRequiredService<ICategoryStore>(),
            sp.GetRequiredService<ISubCategoryStore>()));

        // P3.5: the PDF text extractor and the importer. The importer's
        // .txt/.md/.docx paths are direct; the .pdf path calls the extractor.
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<IPromptImporter>(sp => new PromptImporter(
            sp.GetRequiredService<IPromptStore>(),
            sp.GetRequiredService<IPdfTextExtractor>()));

        return services;
    }
}