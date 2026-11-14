// ============================================================================
// FILE:     src/CodeViewer.Platform/FileSystem/ProjectTreeEnumerator.cs
// PURPOSE:  Lazily enumerates one directory level at a time for the
//           Project Browser and the Auto Display walk. Enumeration is
//           asynchronous and cancellable, runs each directory call inside
//           the code-viewer-scan resilience policy, and tolerates
//           per-entry filesystem errors so one unreadable entry never
//           fails a scan.
// RELATION: Called by the App view models. Produces ProjectNode children
//           that ProjectTreeState.SetChildren attaches. Never performs a
//           recursive scan itself; recursion is driven by the caller
//           expanding directories or by the Auto Display orchestrator.
// ============================================================================

using System.Runtime.CompilerServices;
using CodeViewer.Core.Domain;
using CodeViewer.Platform.Composition;
using Portfolio.Resilience.Abstractions;

namespace CodeViewer.Platform.FileSystem;

/// <summary>
/// Enumerates project directories one level at a time.
/// </summary>
public sealed class ProjectTreeEnumerator
{
    private readonly IResilienceExecutor _resilience;

    public ProjectTreeEnumerator(IResilienceExecutor resilience)
    {
        ArgumentNullException.ThrowIfNull(resilience);
        _resilience = resilience;
    }

    /// <summary>
    /// Enumerates the immediate children of a directory, applying the
    /// OS-clutter exclusion rules. Directories and files are returned in
    /// enumeration order; <see cref="CodeViewer.Core.Tree.ProjectTreeState.SetChildren"/>
    /// sorts them for display.
    /// </summary>
    /// <param name="directory">The directory node being expanded.</param>
    /// <param name="showSystemFiles">The per-project "Show system files" toggle.</param>
    /// <param name="exclusions">
    /// Optional user exclusion rules. When non-null, entries whose name matches a
    /// rule of the matching kind (Folder for directories, File for files) are
    /// skipped before a ProjectNode is created, so excluded folders are never
    /// yielded and therefore never expanded or enumerated further. Null (the
    /// default) preserves the pre-exclusion enumeration behavior exactly.
    /// </param>
    /// <param name="cancellationToken">
    /// Checked between entries so cancelling a large scan stays responsive.
    /// </param>
    /// <returns>
    /// The children found. Entries that cannot be accessed (permission
    /// denied, path too long, deleted mid-scan) are skipped, not fatal.
    /// </returns>
    public async IAsyncEnumerable<ProjectNode> EnumerateChildrenAsync(
        ProjectNode directory,
        bool showSystemFiles,
        GlobalExclusions? exclusions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);

        // Subdirectories first, then files, each enumerated under the
        // code-viewer-scan policy. The policy wraps this single directory
        // call only; per-entry errors are tolerated below.
        var subdirectories = await EnumerateEntryPathsAsync(
                directory.FullPath, searchPattern: "*", directories: true, cancellationToken)
            .ConfigureAwait(false);

        foreach (var path in subdirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.Length == 0 || ExclusionRules.IsHidden(name, showSystemFiles))
            {
                continue;
            }

            if (exclusions is not null && exclusions.IsExcluded(name, ExclusionKind.Folder))
            {
                continue;
            }

            yield return new ProjectNode(NodeKind.Directory, name, path, directory);
        }

        var files = await EnumerateEntryPathsAsync(
                directory.FullPath, searchPattern: "*", directories: false, cancellationToken)
            .ConfigureAwait(false);

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.Length == 0 || ExclusionRules.IsHidden(name, showSystemFiles))
            {
                continue;
            }

            if (exclusions is not null && exclusions.IsExcluded(name, ExclusionKind.File))
            {
                continue;
            }

            yield return new ProjectNode(NodeKind.File, name, path, directory);
        }
    }

    // ------------------------------------------------------------------
    // One directory call inside the resilience policy, with entry-level
    // tolerance for the expected filesystem failures.
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> EnumerateEntryPathsAsync(
        string directoryPath,
        string searchPattern,
        bool directories,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _resilience.ExecuteAsync<IReadOnlyList<string>>(
                policyName: CodeViewerPolicyNames.DirectoryScan,
                operation: token => Task.FromResult<IReadOnlyList<string>>(
                    EnumerateCore(directoryPath, directories, token)),
                fallback: _ => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()),
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or System.Security.SecurityException
            or PathTooLongException
            or DirectoryNotFoundException
            or IOException)
        {
            // Entry-level tolerance: a directory that cannot be enumerated
            // yields no children but never fails the scan. The node stays
            // visible in the browser and is marked Failed by the caller.
            _ = ex;
            return Array.Empty<string>();
        }
    }

    private static List<string> EnumerateCore(
        string directoryPath, bool directories, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<string>();
        var options = new EnumerationOptions
        {
            // Do not recurse here; the caller drives recursion. Do not
            // follow reparse points (symlinks/junctions) so a link cycle
            // cannot loop the walk.
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            // We tolerate per-entry errors ourselves; do not let the
            // enumerator throw on an inaccessible entry.
            IgnoreInaccessible = true,
        };

        IEnumerable<string> entries = directories
            ? Directory.EnumerateDirectories(directoryPath, "*", options)
            : Directory.EnumerateFiles(directoryPath, "*", options);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(entry);
        }

        return results;
    }
}
