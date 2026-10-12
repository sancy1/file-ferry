// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/CloudOutputSink.cs
// PURPOSE:  ICloudOutputSink implementation. Writes a composed document
//           to a cloud destination via ICloudProvider. Mirrors
//           OutputSink overwrite-confirmation logic: prompt on collision,
//           honor Replace/Cancel, apply "Replace for all" per operation.
// RELATION: Implements ICloudOutputSink. Consumed by DispatchingOutputSink.
//           Provider selection is by scheme and optional instance, via an
//           async factory injected at construction.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Portfolio.Resilience.Errors;

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// Writes composed documents to cloud destinations.
/// </summary>
public sealed class CloudOutputSink : ICloudOutputSink
{
    private readonly Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>> _providerFactory;
    private readonly IFormatWriterRegistry _writers;
    private readonly IOverwriteConfirmer _overwriteConfirmer;

    /// <summary>Creates a sink.</summary>
    /// <param name="providerFactory">
    /// Resolves the provider for a scheme and optional instance. Never null.
    /// </param>
    /// <param name="writers">The format writer registry. Never null.</param>
    /// <param name="overwriteConfirmer">
    /// The overwrite confirmer. Never null.
    /// </param>
    public CloudOutputSink(
        Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>> providerFactory,
        IFormatWriterRegistry writers,
        IOverwriteConfirmer overwriteConfirmer)
    {
        _providerFactory = providerFactory ??
            throw new ArgumentNullException(nameof(providerFactory));
        _writers = writers ??
            throw new ArgumentNullException(nameof(writers));
        _overwriteConfirmer = overwriteConfirmer ??
            throw new ArgumentNullException(nameof(overwriteConfirmer));
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

        var folder = options.DestinationFolder ?? string.Empty;

        if (string.IsNullOrWhiteSpace(folder))
        {
            return SaveResult.Fail(folder, "Cloud destination is empty.");
        }

        if (!PathScheme.IsCloudPath(folder))
        {
            return SaveResult.Fail(folder, "Cloud sink received a non-cloud destination: " + folder);
        }

        var writer = _writers.GetByExtension(options.Extension);
        if (writer is null)
        {
            return SaveResult.Fail(folder, "No format writer registered for extension.");
        }

        try
        {
            return document.Mode == OutputMode.SingleMerged
                ? await WriteMergedAsync(document, writer, folder, options, progress, cancellationToken).ConfigureAwait(false)
                : await WriteSeparateAsync(document, writer, folder, options, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (ResilienceException ex)
        {
            return SaveResult.Fail(folder, ex.Category + ": " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

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
        var outputUri = CloudPath.Combine(folder, fileName);

        PathSchemeInfo scheme;
        try
        {
            scheme = PathScheme.Parse(outputUri);
        }
        catch (FormatException)
        {
            return SaveResult.Fail(
                destinationFolder: folder,
                reason: "Malformed cloud destination: " + outputUri);
        }

        var resolution = await _providerFactory(
            scheme.Kind, scheme.Instance, cancellationToken).ConfigureAwait(false);

        if (!resolution.IsResolved)
        {
            return SaveResult.Fail(
                destinationFolder: folder,
                reason: CloudFileResolver.BuildFailureNote(scheme, resolution.Failure));
        }

        var provider = resolution.Provider!;

        var exists = await provider.ExistsAsync(outputUri, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            var prompt = await _overwriteConfirmer.ConfirmAsync(
                outputUri, cancellationToken).ConfigureAwait(false);

            if (prompt.Decision != OverwriteDecision.Replace)
            {
                return SaveResult.Fail(
                    destinationFolder: folder,
                    reason: "Cancelled by user: file exists");
            }
        }

        var handle = await provider.OpenWriteAsync(
            outputUri, contentType: null, cancellationToken).ConfigureAwait(false);

        try
        {
            var counting = new CountingStream(handle.Stream);
            await writer.WriteAsync(document, counting, cancellationToken)
                .ConfigureAwait(false);
            await counting.FlushAsync(cancellationToken).ConfigureAwait(false);

            var bytes = counting.BytesWritten;

            await handle.CompleteAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new SaveProgress(
                Stage: "Complete",
                BytesWritten: (int)bytes,
                BytesTotal: (int)bytes));

            return SaveResult.Ok(
                destinationFolder: folder,
                outputPath: outputUri,
                fileCount: document.Files.Count,
                bytesWritten: bytes);
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

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
        var firstUri = (string?)null;
        bool? applyToAll = null;

        foreach (var fc in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseName = CloudPath.GetFileNameWithoutExtension(fc.File.AbsolutePath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "file-" + (written + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(options.FileName))
            {
                var prefix = SanitizeName(
                    CloudPath.GetFileNameWithoutExtension(options.FileName.Trim()));
                if (prefix.Length > 0)
                {
                    baseName = prefix + "-" + baseName;
                }
            }

            var outputName = baseName + writer.Extension;
            var outputUri = CloudPath.Combine(folder, outputName);

            PathSchemeInfo scheme;
            try
            {
                scheme = PathScheme.Parse(outputUri);
            }
            catch (FormatException)
            {
                return SaveResult.Fail(
                    destinationFolder: folder,
                    reason: "Malformed cloud destination: " + outputUri);
            }

            var resolution = await _providerFactory(
                scheme.Kind, scheme.Instance, cancellationToken).ConfigureAwait(false);

            if (!resolution.IsResolved)
            {
                return SaveResult.Fail(
                    destinationFolder: folder,
                    reason: CloudFileResolver.BuildFailureNote(scheme, resolution.Failure));
            }

            var provider = resolution.Provider!;

            bool needsPrompt;
            if (applyToAll == false)
            {
                return SaveResult.Fail(
                    destinationFolder: folder,
                    reason: "Cancelled by user: file exists (" + outputUri + ")");
            }
            else if (applyToAll == true)
            {
                needsPrompt = false;
            }
            else
            {
                var exists = await provider.ExistsAsync(outputUri, cancellationToken)
                    .ConfigureAwait(false);
                needsPrompt = exists;
            }

            if (needsPrompt)
            {
                var prompt = await _overwriteConfirmer.ConfirmAsync(
                    outputUri, cancellationToken).ConfigureAwait(false);

                if (prompt.Decision != OverwriteDecision.Replace)
                {
                    return SaveResult.Fail(
                        destinationFolder: folder,
                        reason: "Cancelled by user: file exists (" + outputUri + ")");
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

            var handle = await provider.OpenWriteAsync(
                outputUri, contentType: null, cancellationToken).ConfigureAwait(false);

            try
            {
                var counting = new CountingStream(handle.Stream);
                await writer.WriteAsync(singleDoc, counting, cancellationToken)
                    .ConfigureAwait(false);
                await counting.FlushAsync(cancellationToken).ConfigureAwait(false);

                var bytes = counting.BytesWritten;
                totalBytes += bytes;
                written++;
                firstUri ??= outputUri;

                await handle.CompleteAsync(cancellationToken).ConfigureAwait(false);

                progress?.Report(new SaveProgress(
                    Stage: "Wrote " + written + " of " + document.Files.Count,
                    BytesWritten: (int)totalBytes,
                    BytesTotal: (int)totalBytes));
            }
            finally
            {
                await handle.DisposeAsync().ConfigureAwait(false);
            }
        }

        return SaveResult.Ok(
            destinationFolder: folder,
            outputPath: firstUri ?? folder,
            fileCount: written,
            bytesWritten: totalBytes);
    }

    private static string ResolveOutputName(
        ComposedDocument document,
        OutputSinkOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FileName))
        {
            var raw = options.FileName.Trim();
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

        if (document.Files.Count > 0)
        {
            var first = document.Files[0];
            var baseName = CloudPath.GetFileNameWithoutExtension(first.File.AbsolutePath);
            var sanitized = SanitizeName(baseName);
            if (sanitized.Length > 0)
            {
                return sanitized;
            }
        }

        return "bundle";
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] < 32)
            {
                chars[i] = (char)95;
            }
        }

        return new string(chars).Trim();
    }
}
