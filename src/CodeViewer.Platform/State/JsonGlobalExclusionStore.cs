// filepath: src/CodeViewer.Platform/State/JsonGlobalExclusionStore.cs
// ============================================================================
// PURPOSE:  JSON implementation of IGlobalExclusionStore. Single file
//           'global-exclusions.json' under the CodeViewer storage root,
//           with a one-generation backup at 'global-exclusions.backup.json'
//           written before each successful save. Atomic writes (temp file +
//           File.Move) inside the bundle-write resilience policy. Corrupt or
//           unsupported files are quarantined, never deleted. When
//           persistence is unavailable the store runs in memory only and
//           logs the reason; no method throws to callers.
// RELATION: Registered by AddCodeViewer. Uses CodeViewerStorageRoot for the
//           directory and ILogSink to report failures to the Log drawer.
//           Mirrors JsonWorkspaceStateStore's patterns exactly.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using CodeViewer.Core.Domain;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;

namespace CodeViewer.Platform.State;

/// <summary>
/// JSON global-exclusion rule store.
/// </summary>
public sealed class JsonGlobalExclusionStore : IGlobalExclusionStore
{
    /// <summary>The file name persisted under <c>CodeViewerStorageRoot.RootDirectory</c>.</summary>
    public const string FileName = "global-exclusions.json";

    /// <summary>The backup file name written before each successful save.</summary>
    public const string BackupFileName = "global-exclusions.backup.json";

    /// <summary>The current schema version of the persisted document.</summary>
    public const int CurrentSchemaVersion = 1;

    private const string EventPolicy = "code-viewer-exclusions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CodeViewerStorageRoot _storageRoot;
    private readonly IResilienceExecutor _resilience;
    private readonly ILogSink? _logSink;

    /// <summary>In-memory fallback used when persistence is unavailable.</summary>
    private GlobalExclusions _memory = GlobalExclusions.Empty;

    /// <summary>Creates the store.</summary>
    public JsonGlobalExclusionStore(
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
    public async Task<GlobalExclusions> LoadAsync(CancellationToken ct = default)
    {
        if (!_storageRoot.IsPersistent)
        {
            return _memory;
        }

        var file = FilePathFor();
        if (!File.Exists(file))
        {
            return GlobalExclusions.Empty;
        }

        try
        {
            var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var doc = JsonSerializer.Deserialize<PersistedDocument>(json, JsonOptions);
            if (doc is null)
            {
                Quarantine(file, "deserialized to null");
                return GlobalExclusions.Empty;
            }

            if (doc.SchemaVersion > CurrentSchemaVersion)
            {
                // Written by a newer build. Do not apply what we do not
                // understand; keep the file for the newer build.
                Log($"Exclusion schema {doc.SchemaVersion} is newer than supported " +
                    $"{CurrentSchemaVersion}; ignoring.");
                return GlobalExclusions.Empty;
            }

            var rules = GlobalExclusions.FromRules(
                ((IEnumerable<PersistedRule>?)doc.Rules ?? Enumerable.Empty<PersistedRule>())
                    .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => new ExclusionRule(r.Name!, r.Kind)));

            return rules;
        }
        catch (JsonException ex)
        {
            Quarantine(file, $"corrupt JSON: {ex.Message}");
            return GlobalExclusions.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not read global exclusions: {ex.Message}");
            return GlobalExclusions.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SaveAsync(GlobalExclusions rules, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (!_storageRoot.IsPersistent)
        {
            _memory = rules;
            return true;
        }

        var file = FilePathFor();
        var backup = BackupPathFor();
        var temp = file + ".part";

        try
        {
            var doc = new PersistedDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Rules = rules.Rules
                    .OrderBy(r => r.Kind)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new PersistedRule { Name = r.Name, Kind = r.Kind })
                    .ToList(),
            };
            var json = JsonSerializer.Serialize(doc, JsonOptions);

            // Write to temp, then atomically move into place, inside the
            // bundle-write policy. A crash between the two leaves either the
            // old file or a .part file, never a half-written target.
            return await _resilience.ExecuteAsync<bool>(
                policyName: PolicyNames.BundleWrite,
                operation: async token =>
                {
                    // Back up the current file (if any) before overwriting.
                    // Backup failure is tolerated; it must never block save.
                    TryBackupCurrentFile(file, backup);

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
            Log($"Could not save global exclusions: {ex.Message}");
            TryDeleteTemp(temp);
            return false;
        }
    }

    // ------------------------------------------------------------------

    private string FilePathFor()
        => Path.Combine(_storageRoot.RootDirectory!, FileName);

    private string BackupPathFor()
        => Path.Combine(_storageRoot.RootDirectory!, BackupFileName);

    private static void TryBackupCurrentFile(string currentFile, string backupFile)
    {
        try
        {
            if (File.Exists(currentFile))
            {
                File.Copy(currentFile, backupFile, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Backup is a nice-to-have, never a save blocker.
            _ = ex;
        }
    }

    private void Quarantine(string file, string reason)
    {
        try
        {
            var quarantined = file + ".corrupt-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture);
            File.Move(file, quarantined, overwrite: true);
            Log($"Quarantined global exclusions '{Path.GetFileName(file)}': {reason}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not quarantine corrupt global exclusions: {ex.Message}");
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
            ErrorType     = "GlobalExclusions",
            ErrorMessage  = message,
        });
    }

    // ------------------------------------------------------------------
    // On-disk shape. Kept private: schema evolution is an internal concern.
    // ------------------------------------------------------------------

    private sealed class PersistedDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<PersistedRule>? Rules { get; set; }
    }

    private sealed class PersistedRule
    {
        public string? Name { get; set; }
        public ExclusionKind Kind { get; set; }
    }
}