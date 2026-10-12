// ============================================================================
// FILE:     src\FileHarvester.Core/Implementation/OutputSink.cs
// PURPOSE:  Writes a composed document to disk. Handles both SingleMerged
//           (one output file, named via options.FileName or source base
//           name) and SeparateFiles (one output per source file). Every
//           write runs through the "bundle-write" resilience policy.
//           Each file is written to a temp path in the same directory and
//           renamed into place on success, so a failed or cancelled write
//           never leaves a partial file at the final path.
// RELATION: Implements IOutputSink. Registered in AddFileHarvester.
// ============================================================================

using System.Globalization;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace FileHarvester.Core.Implementation;

public sealed class OutputSink : IOutputSink, ILocalOutputSink
{
    private static readonly char[] InvalidFileChars =
        Path.GetInvalidFileNameChars();

    private readonly IFormatWriterRegistry _writers;
    private readonly IDefaultPathsProvider _paths;
    private readonly IResilienceExecutor _resilience;
    private readonly IOverwriteConfirmer _overwriteConfirmer;

    public OutputSink(
        IFormatWriterRegistry writers,
        IDefaultPathsProvider paths,
        IResilienceExecutor resilience,
        IOverwriteConfirmer overwriteConfirmer)
    {
        _writers    = writers    ?? throw new ArgumentNullException(nameof(writers));
        _paths      = paths      ?? throw new ArgumentNullException(nameof(paths));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        _overwriteConfirmer = overwriteConfirmer ?? throw new ArgumentNullException(nameof(overwriteConfirmer));
    }

    /// <inheritdoc/>
    public async Task<SaveResult> SaveAsync(
        ComposedDocument document,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var folder = ResolveDestinationFolder(options.DestinationFolder);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail(folder, $"Cannot create destination folder: {ex.Message}");
        }

        var writer = _writers.GetByExtension(options.Extension);
        if (writer is null)
        {
            return SaveResult.Fail(
                folder,
                $"No format writer registered for extension '{options.Extension}'.");
        }

        try
        {
            return await _resilience.ExecuteAsync(
                policyName: PolicyNames.BundleWrite,
                operation: async token => await WriteCoreAsync(
                    document, writer, folder, options, progress, token)
                    .ConfigureAwait(false),
                fallback: _ => Task.FromResult(SaveResult.Fail(
                    folder, "Write failed (circuit open or timeout).")),
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (ResilienceException ex)
        {
            return SaveResult.Fail(
                folder,
                $"{ex.Category}: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    // ------------------------------------------------------------------

    private Task<SaveResult> WriteCoreAsync(
        ComposedDocument document,
        IFormatWriter writer,
        string folder,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress,
        CancellationToken cancellationToken) =>
        document.Mode == OutputMode.SingleMerged
            ? WriteMergedAsync(document, writer, folder, options, progress, cancellationToken)
            : WriteSeparateAsync(document, writer, folder, options, progress, cancellationToken);

    // ------------------------------------------------------------------
    // Single merged mode: one output file, user-nameable.
    // ------------------------------------------------------------------

    private async Task<SaveResult> WriteMergedAsync(
        ComposedDocument document,
        IFormatWriter writer,
        string folder,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var baseName = ResolveOutputName(document, options);
        var fileName = baseName + writer.Extension;
        var outputPath = Path.Combine(folder, fileName);
        // Overwrite guard. Runs before any file is created so an existing
        // file is never silently truncated. The existence check is a
        // filesystem read and runs through the file-read policy. The
        // policy fallback returns true (fail closed): if the check cannot
        // be completed, treat the target as existing and prompt.
        var exists = await _resilience.ExecuteAsync(
            policyName: PolicyNames.FileRead,
            operation: _ => Task.FromResult(File.Exists(outputPath)),
            fallback: _ => Task.FromResult(true),
            ct: cancellationToken).ConfigureAwait(false);

        if (exists)
        {
            var prompt = await _overwriteConfirmer.ConfirmAsync(
                outputPath, cancellationToken).ConfigureAwait(false);

            if (prompt.Decision != OverwriteDecision.Replace)
            {
                return SaveResult.Fail(
                    folder,
                    "Cancelled by user: file exists");
            }
        }

        // The confirmation has been resolved. Write to a temp file in the
        // same directory, then rename into place. The finally deletes the
        // temp on success or failure; on success the temp has already been
        // renamed, so the delete is a no-op.
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var bytes = 0L;
        try
        {
            await using (var stream = new FileStream(
                path: tempPath,
                mode: FileMode.Create,
                access: FileAccess.Write,
                share: FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                await writer.WriteAsync(document, stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                bytes = stream.Position;
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }

        progress?.Report(new SaveProgress(
            Stage: "Complete",
            BytesWritten: (int)bytes,
            BytesTotal: (int)bytes));

        return SaveResult.Ok(folder, outputPath, document.Files.Count, bytes);
    }

    /// <summary>
    /// Determines the base name for the merged output:
    ///   1. options.FileName (stripped of extension, sanitized)
    ///   2. first source file's base name (sanitized)
    ///   3. "bundle" fallback
    /// </summary>
    private static string ResolveOutputName(
        ComposedDocument document,
        OutputSinkOptions options)
    {
        // 1. Explicit name from the user.
        if (!string.IsNullOrWhiteSpace(options.FileName))
        {
            var raw = options.FileName.Trim();

            // Strip the extension if the user included one.
            var extension = Path.GetExtension(raw);
            if (!string.IsNullOrEmpty(extension))
            {
                raw = Path.GetFileNameWithoutExtension(raw);
            }

            var sanitized = SanitizeName(raw);
            if (sanitized.Length > 0)
            {
                return sanitized;
            }
        }

        // 2. First source file's base name.
        if (document.Files.Count > 0)
        {
            var first = document.Files[0];
            var baseName = Path.GetFileNameWithoutExtension(first.File.AbsolutePath);
            var sanitized = SanitizeName(baseName);
            if (sanitized.Length > 0)
            {
                return sanitized;
            }
        }

        // 3. Fallback.
        return "bundle";
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(InvalidFileChars, chars[i]) >= 0 || chars[i] < 32)
            {
                chars[i] = '_';
            }
        }

        return new string(chars).Trim();
    }

    // ------------------------------------------------------------------
    // Separate files mode: one output per source file.
    // ------------------------------------------------------------------

    private async Task<SaveResult> WriteSeparateAsync(
        ComposedDocument document,
        IFormatWriter writer,
        string folder,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = 0L;
        var written = 0;
        var firstPath = (string?)null;

        var commonRoot = ComputeCommonRoot(document);

        // Overwrite decision cache for this operation only. Reset for
        // every WriteSeparateAsync invocation; never persisted across
        // save operations. null means "not yet decided".
        bool? applyToAll = null;

        foreach (var fc in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subfolder = folder;

            if (document.PreserveFolderStructure && !string.IsNullOrEmpty(commonRoot))
            {
                try
                {
                    var relativeDir = Path.GetDirectoryName(fc.File.AbsolutePath);
                    if (!string.IsNullOrEmpty(relativeDir))
                    {
                        var rel = Path.GetRelativePath(commonRoot!, relativeDir);
                        if (rel != "." && !rel.StartsWith("..", StringComparison.Ordinal))
                        {
                            subfolder = Path.Combine(folder, rel);
                        }
                    }
                }
                catch (ArgumentException) { }
            }

            Directory.CreateDirectory(subfolder);

            var baseName = Path.GetFileNameWithoutExtension(fc.File.AbsolutePath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = $"file-{written + 1:D3}";
            }
            baseName = SanitizeName(baseName);

            // If the user supplied a name for Separate Files, use it as
            // a prefix so each output keeps its source identity but is
            // tagged with the user's chosen label.
            if (!string.IsNullOrWhiteSpace(options.FileName))
            {
                var prefix = SanitizeName(Path.GetFileNameWithoutExtension(
                    options.FileName.Trim()));

                if (prefix.Length > 0)
                {
                    baseName = $"{prefix}-{baseName}";
                }
            }

            var outputName = baseName + writer.Extension;
            var outputPath = Path.Combine(subfolder, outputName);
            // Overwrite guard. Runs before any file is created so an
            // existing file is never silently truncated. The existence
            // check runs through the file-read policy. The policy
            // fallback returns true (fail closed).
            bool needsPrompt;
            if (applyToAll == false)
            {
                // The user already cancelled "all remaining" for this
                // operation; do not write this or any later file.
                return SaveResult.Fail(
                    folder,
                    "Cancelled by user: file exists (" + outputPath + ")");
            }
            else if (applyToAll == true)
            {
                // The user chose Replace for all remaining; overwrite
                // without asking again.
                needsPrompt = false;
            }
            else
            {
                var exists = await _resilience.ExecuteAsync(
                    policyName: PolicyNames.FileRead,
                    operation: _ => Task.FromResult(File.Exists(outputPath)),
                    fallback: _ => Task.FromResult(true),
                    ct: cancellationToken).ConfigureAwait(false);
                needsPrompt = exists;
            }

            if (needsPrompt)
            {
                var prompt = await _overwriteConfirmer.ConfirmAsync(
                    outputPath, cancellationToken).ConfigureAwait(false);

                if (prompt.Decision != OverwriteDecision.Replace)
                {
                    return SaveResult.Fail(
                        folder,
                        "Cancelled by user: file exists (" + outputPath + ")");
                }

                if (prompt.ApplyToAllRemaining)
                {
                    applyToAll = true;
                }
            }

            var singleDoc = new ComposedDocument(
                Files: new[] { fc },
                Mode: OutputMode.SingleMerged,
                DelimiterTemplate: string.Empty,
                TwoBlankLines: false,
                IncludeTableOfContents: false,
                IncludeManifest: false);

            // The confirmation has been resolved. Write to a temp file in
            // the same directory, then rename into place.
            var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
            long bytes;
            try
            {
                await using (var stream = new FileStream(
                    path: tempPath,
                    mode: FileMode.Create,
                    access: FileAccess.Write,
                    share: FileShare.None,
                    bufferSize: 64 * 1024,
                    useAsync: true))
                {
                    await writer.WriteAsync(singleDoc, stream, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    bytes = stream.Position;
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                TryDeleteTemp(tempPath);
            }

            totalBytes += bytes;
            written++;
            firstPath ??= outputPath;

            progress?.Report(new SaveProgress(
                Stage: $"Wrote {written} of {document.Files.Count}",
                BytesWritten: (int)totalBytes,
                BytesTotal: (int)totalBytes));
        }

        return SaveResult.Ok(folder, firstPath ?? folder, written, totalBytes);
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Best-effort deletion of a temporary output file. Swallows only its
    /// own cleanup failure so the primary write, cancellation, or rename
    /// exception is never masked.
    /// </summary>
    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort cleanup. A failure to delete the temp file must
            // not mask the primary error.
        }
    }

    private string ResolveDestinationFolder(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested!;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(_paths.DownloadsFolder, $"FileHarvest_{stamp}");
    }

    private static string? ComputeCommonRoot(ComposedDocument document)
    {
        if (document.Files.Count == 0) return null;

        var dirs = document.Files
            .Select(f => Path.GetDirectoryName(f.File.AbsolutePath))
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!)
            .ToList();

        if (dirs.Count == 0) return null;

        var common = dirs[0];
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (var dir in dirs.Skip(1))
        {
            while (!string.IsNullOrEmpty(common) && !dir.StartsWith(common, comparison))
            {
                var parent = Path.GetDirectoryName(common);
                if (string.IsNullOrEmpty(parent) || parent == common)
                {
                    return null;
                }
                common = parent;
            }
        }

        return common;
    }
}
