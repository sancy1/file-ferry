// ============================================================================
// FILE:     src\FileHarvester.App/Services/LogFileManager.cs
// PURPOSE:  Manages the JSON-Lines file written by Portfolio.Resilience's
//           FileLogSink. Handles: (1) permanent clear via a marker file
//           that is processed on next startup (because the file is locked
//           while the app is running), (2) rotation at 5 MB, (3) pruning
//           of archives older than 7 days.
// RELATION: Constructed in App.axaml.cs and passed to LogDrawerViewModel.
//           The library writes; we only read, truncate, rename, and delete.
// ============================================================================

namespace FileHarvester.App.Services;

/// <summary>
/// Manages the on-disk JSON-Lines log file: clear, rotation, pruning.
/// </summary>
/// <remarks>
/// Windows locks open files against write operations. While the app is
/// running, Portfolio.Resilience's FileLogSink holds the log file open,
/// so we cannot truncate it. To give the user a "permanent clear", the
/// Clear button writes a marker file. On the next app startup (before
/// the library opens the log for writing), <see cref="ProcessPendingClear"/>
/// truncates the log file and removes the marker.
/// </remarks>
public sealed class LogFileManager
{
    public const long RotationThresholdBytes = 5 * 1024 * 1024; // 5 MB
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly string _logDirectory;
    private readonly string _baseName;

    public LogFileManager(string logDirectory, string baseName = "resilience")
    {
        _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
        _baseName = baseName ?? throw new ArgumentNullException(nameof(baseName));

        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>Absolute path to today's active log file.</summary>
    public string CurrentFilePath =>
        Path.Combine(_logDirectory, $"{_baseName}-{DateTime.Now:yyyy-MM-dd}.jsonl");

    /// <summary>Absolute path to the clear-requested marker file.</summary>
    public string ClearMarkerPath =>
        Path.Combine(_logDirectory, $"{_baseName}-clear-requested.flag");

    /// <summary>
    /// Marks the log for permanent clearing on next startup. Called by the
    /// Clear button. Does not modify the log file (which is locked).
    /// </summary>
    public void RequestClear()
    {
        try
        {
            File.WriteAllText(ClearMarkerPath, DateTime.UtcNow.ToString("o"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// If a clear was requested during the last session, truncate the log
    /// file now (before the library opens it) and remove the marker.
    /// Called at app startup.
    /// </summary>
    public void ProcessPendingClear()
    {
        if (!File.Exists(ClearMarkerPath))
        {
            return;
        }

        // Truncate every log file in the directory (current + archives).
        // A permanent clear is permanent.
        try
        {
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.jsonl"))
            {
                try
                {
                    File.WriteAllText(file, string.Empty);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (DirectoryNotFoundException) { }

        // Remove the marker.
        try
        {
            File.Delete(ClearMarkerPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Legacy direct truncate. Works only when the file is not locked.
    /// Kept for cases where we know the file is free (e.g., before the
    /// library writes its first event of the session).
    /// </summary>
    public void Truncate()
    {
        var path = CurrentFilePath;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite);

            stream.SetLength(0);
            stream.Flush(true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void RotateIfNeeded()
    {
        var current = CurrentFilePath;

        if (!File.Exists(current)) return;

        try
        {
            var info = new FileInfo(current);
            if (info.Length < RotationThresholdBytes) return;

            var archivedName = $"{_baseName}-{DateTime.Now:yyyy-MM-dd-HHmmss}.jsonl";
            var archivedPath = Path.Combine(_logDirectory, archivedName);

            File.Move(current, archivedPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void PruneOldArchives()
    {
        try
        {
            var cutoff = DateTime.UtcNow - Retention;

            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.jsonl"))
            {
                if (string.Equals(file, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (DirectoryNotFoundException) { }
    }
}
