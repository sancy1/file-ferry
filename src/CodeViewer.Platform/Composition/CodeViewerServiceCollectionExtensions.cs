// ============================================================================
// FILE:     src/CodeViewer.Platform/Composition/CodeViewerServiceCollectionExtensions.cs
// PURPOSE:  The single DI entry point for CodeViewer. Creates the CodeViewer
//           workspace-state directory under the supplied application data
//           root and registers the services CodeViewer owns. Each later
//           phase adds its own registrations here so this method stays the
//           one CodeViewer composition point.
// RELATION: Called by FileHarvester.App/App.axaml.cs immediately after
//           AddImageTextify. Mirrors the shape of
//           SchemaLenceServiceCollectionExtensions.AddSchemaLence and
//           ImageTextifyServiceCollectionExtensions.AddImageTextify, with
//           one deliberate difference: directory creation is guarded so a
//           persistence failure can never prevent File-Ferry from starting.
// ============================================================================

using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;

namespace CodeViewer.Platform.Composition;

/// <summary>
/// Composition entry point for CodeViewer.
/// </summary>
public static class CodeViewerServiceCollectionExtensions
{
    /// <summary>
    /// The policy label attached to the log event emitted when the
    /// workspace-state directory cannot be created. Not a registered
    /// resilience policy; it only identifies the event in the Log drawer.
    /// </summary>
    private const string StorageStartupEventPolicy = "code-viewer-storage";

    /// <summary>
    /// Registers CodeViewer with the service collection and ensures the
    /// CodeViewer workspace-state directory exists under the supplied
    /// application data root.
    /// </summary>
    /// <param name="services">
    /// The service collection to register into. Not null.
    /// </param>
    /// <param name="applicationDataDirectory">
    /// The application-owned data root. CodeViewer creates its own
    /// <c>FileHarvester/CodeViewer</c> subfolder under this path and owns
    /// that subfolder. The caller owns the parent directory. Null, empty,
    /// or whitespace is rejected.
    /// </param>
    /// <param name="logSink">
    /// Optional sink that receives a single event when the storage
    /// directory cannot be created. The App passes its ObservableLogSink
    /// so the failure appears in the Log drawer.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance, for fluent chaining.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="applicationDataDirectory"/> is null, empty,
    /// or whitespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Startup safety.</b> Unlike the SchemaLence and ImageTextify
    /// composition roots, which treat <see cref="Directory.CreateDirectory"/>
    /// as an accepted startup exception (finding F-009 in
    /// docs/resilience-usage.md), CodeViewer guards the call. Workspace
    /// persistence is an enhancement, not a requirement: if the directory
    /// cannot be created because of permissions, disk errors, or another
    /// filesystem issue, the failure is logged and CodeViewer continues
    /// with an in-memory-only <see cref="CodeViewerStorageRoot"/>. A
    /// persistence problem must never prevent File-Ferry from starting.
    /// </para>
    /// <para>
    /// <b>Resilience.</b> CodeViewer consumes the Portfolio.Resilience
    /// registration already installed by AddFileHarvester. It does not
    /// re-register resilience. The guarded CreateDirectory call runs
    /// outside a resilience policy: it runs once at startup on a directory
    /// the application owns, and its failure modes are not transient
    /// failures a retry could recover from.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCodeViewer(
        this IServiceCollection services,
        string applicationDataDirectory,
        ILogSink? logSink = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);

        CodeViewerStorageRoot storageRoot;
        try
        {
            var root = Path.Combine(
                applicationDataDirectory, "FileHarvester", "CodeViewer");
            Directory.CreateDirectory(root);
            storageRoot = new CodeViewerStorageRoot(root);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            // Persistence is unavailable; continue in memory only. The
            // workspace state store checks IsPersistent before every
            // disk access, so no later code path can trip over the
            // missing directory.
            storageRoot = CodeViewerStorageRoot.InMemory;
            logSink?.Emit(new ResilienceEvent
            {
                EventType     = ResilienceEventType.CallFailed,
                PolicyName    = StorageStartupEventPolicy,
                CorrelationId = Guid.NewGuid().ToString("N"),
                TimestampUtc  = DateTime.UtcNow,
                ErrorType     = ex.GetType().FullName ?? ex.GetType().Name,
                ErrorMessage  = "CodeViewer workspace persistence is disabled " +
                                "for this session; state will be kept in memory " +
                                $"only. Reason: {ex.Message}",
            });
        }

        services.AddSingleton(storageRoot);

        // Phase 3: the lazy project tree enumerator and the streaming
        // file reader. Both consume the IResilienceExecutor installed by
        // AddFileHarvester.
        services.AddSingleton<ProjectTreeEnumerator>();
        services.AddSingleton<ProjectFileReader>();

        // Phase 4: the JSON workspace state store. It receives the
        // guarded storage root (in-memory when persistence is
        // unavailable) and the app's log sink for failure reporting.
        services.AddSingleton<IWorkspaceStateStore>(sp => new JsonWorkspaceStateStore(
            sp.GetRequiredService<CodeViewerStorageRoot>(),
            sp.GetRequiredService<IResilienceExecutor>(),
            logSink));

        // Global exclusion feature: the JSON store for the persisted rules,
        // and the cross-tab holder that lets Save & Apply broadcast to every
        // open ProjectTabViewModel. Both are app-lifetime singletons.
        services.AddSingleton<IGlobalExclusionStore>(sp => new JsonGlobalExclusionStore(
            sp.GetRequiredService<CodeViewerStorageRoot>(),
            sp.GetRequiredService<IResilienceExecutor>(),
            logSink));
        services.AddSingleton<GlobalExclusionsHolder>();

        return services;
    }
}
