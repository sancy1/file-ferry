// ============================================================================
// FILE:     src/CodeViewer.Platform/State/JsonWorkspaceStateStore.cs
// PURPOSE:  JSON implementation of IWorkspaceStateStore. One file per
//           project under %AppData%/FileHarvester/CodeViewer/, named by the
//           SHA-256 of the normalized project path. Writes are atomic
//           (temp file + File.Move) inside the bundle-write resilience
//           policy. Corrupt or foreign files are quarantined, never
//           applied. When persistence is unavailable the store runs in
//           memory only. No method throws to callers.
// RELATION: Registered by AddCodeViewer. Uses CodeViewerStorageRoot for
//           the directory and ILogSink to report failures to the Log
//           drawer. Mirrors the atomic-write pattern in LinkJoinerPage.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;

namespace CodeViewer.Platform.State;

/// <summary>
/// JSON per-project workspace state store.
/// </summary>
public sealed class JsonWorkspaceStateStore : IWorkspaceStateStore
{
    private const string EventPolicy = "code-viewer-state";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CodeViewerStorageRoot _storageRoot;
    private readonly IResilienceExecutor _resilience;
    private readonly ILogSink? _logSink;

    // In-memory fallback used when persistence is unavailable, so a
    // session still round-trips state even with no disk access.
    private readonly Dictionary<string, WorkspaceState> _memory = new(StringComparer.Ordinal);

    public JsonWorkspaceStateStore(
        CodeViewerStorageRoot storageRoot,
        IResilienceExecutor resilience,
        ILogSink? logSink = null)
    {
        ArgumentNullException.ThrowIfNull(storageRoot);
        ArgumentNullException.ThrowIfNull(resilience);
        _storageRoot = storageRoot;
        _resilience = resilience;
        _logSink = logSink;
    }

    /// <inheritdoc/>
    public async Task<WorkspaceState?> LoadAsync(
        string projectPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var key = ProjectPathNormalizer.ComputeKey(projectPath);

        if (!_storageRoot.IsPersistent)
        {
            return _memory.TryGetValue(key, out var mem) ? mem : null;
        }

        var file = FilePathFor(key);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions);
            if (state is null)
            {
                Quarantine(file, "deserialized to null");
                return null;
            }

            // The embedded path must match the key the file was saved
            // under; otherwise the file belongs to another project.
            var embeddedKey = state.ProjectPath.Length == 0
                ? string.Empty
                : ProjectPathNormalizer.ComputeKey(state.ProjectPath);
            if (!embeddedKey.Equals(key, StringComparison.Ordinal))
            {
                Quarantine(file, "embedded path does not match the file key");
                return null;
            }

            if (state.SchemaVersion > WorkspaceState.CurrentSchemaVersion)
            {
                // Written by a newer build; do not apply what we do not
                // understand. Keep the file for the newer build.
                Log($"Workspace state schema {state.SchemaVersion} is newer " +
                    $"than supported {WorkspaceState.CurrentSchemaVersion}; ignoring.");
                return null;
            }

            return state;
        }
        catch (JsonException ex)
        {
            Quarantine(file, $"corrupt JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not read workspace state: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SaveAsync(WorkspaceState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Normalize before keying so the file always matches its key.
        state.ProjectPath = ProjectPathNormalizer.NormalizeForDisplay(state.ProjectPath);
        var key = ProjectPathNormalizer.ComputeKey(state.ProjectPath);

        if (!_storageRoot.IsPersistent)
        {
            _memory[key] = state;
            return true;
        }

        var file = FilePathFor(key);
        var temp = file + ".part";

        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);

            // Atomic write: write a temp file, then move it over the
            // target, inside the bundle-write policy. A crash between the
            // two leaves either the old file or a .part file, never a
            // half-written target.
            return await _resilience.ExecuteAsync<bool>(
                policyName: PolicyNames.BundleWrite,
                operation: async token =>
                {
                    await File.WriteAllTextAsync(temp, json, token).ConfigureAwait(false);
                    File.Move(temp, file, overwrite: true);
                    return true;
                },
                fallback: _ => Task.FromResult(false),
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or PathTooLongException)
        {
            Log($"Could not save workspace state: {ex.Message}");
            TryDeleteTemp(temp);
            return false;
        }
    }

    // ------------------------------------------------------------------

    private string FilePathFor(string key)
        => Path.Combine(_storageRoot.RootDirectory!, key + ".json");

    private void Quarantine(string file, string reason)
    {
        try
        {
            var quarantined = file + ".corrupt-" +
                DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture);
            File.Move(file, quarantined, overwrite: true);
            Log($"Quarantined workspace state '{Path.GetFileName(file)}': {reason}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not quarantine corrupt workspace state: {ex.Message}");
        }
    }

    private static void TryDeleteTemp(string temp)
    {
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private void Log(string message)
    {
        _logSink?.Emit(new ResilienceEvent
        {
            EventType     = ResilienceEventType.CallFailed,
            PolicyName    = EventPolicy,
            CorrelationId = Guid.NewGuid().ToString("N"),
            TimestampUtc  = DateTime.UtcNow,
            ErrorType     = "WorkspaceState",
            ErrorMessage  = message,
        });
    }
}
