// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/HarvestJobRunner.cs
// PURPOSE:  Orchestrates one harvest job: reads files, composes a document,
//           writes via IOutputSink. Passes the user's filename choice.
// RELATION: Created by OutputOptionsViewModel.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class HarvestJobRunner
{
    private readonly IContentReader _reader;
    private readonly IBundleComposer _composer;
    private readonly IOutputSink _sink;

    public HarvestJobRunner(
        IContentReader reader,
        IBundleComposer composer,
        IOutputSink sink)
    {
        _reader   = reader   ?? throw new ArgumentNullException(nameof(reader));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _sink     = sink     ?? throw new ArgumentNullException(nameof(sink));
    }

    public async Task<SaveResult> RunAsync(
        IReadOnlyList<ResolvedFile> files,
        HarvestOptions options,
        IProgress<HarvestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        if (files.Count == 0)
        {
            return SaveResult.Fail(
                options.DestinationFolder ?? ".",
                "No files to harvest.");
        }

        progress?.Report(new HarvestProgress("Reading files...", 0, files.Count));

        IReadOnlyList<FileContent> contents;
        try
        {
            var readProgress = new Progress<ReadProgress>(rp =>
                progress?.Report(new HarvestProgress(
                    $"Reading {rp.Processed} of {rp.Total}...",
                    rp.Processed,
                    rp.Total)));

            contents = await _reader.ReadManyAsync(files, readProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail(
                options.DestinationFolder ?? ".",
                $"Read failed: {ex.Message}");
        }

        progress?.Report(new HarvestProgress("Composing...", files.Count, files.Count));

        ComposedDocument document;
        try
        {
            var composerOptions = new ComposerOptions(
                Mode: options.Mode,
                DelimiterTemplate: options.DelimiterTemplate,
                TwoBlankLines: options.TwoBlankLines,
                IncludeTableOfContents: options.IncludeTableOfContents,
                IncludeManifest: options.IncludeManifest,
                PreserveFolderStructure: options.PreserveFolderStructure,
                IncludeHeaders: options.IncludeHeaders,
                IncludeCodeFences: options.IncludeCodeFences);

            document = _composer.Compose(contents, composerOptions);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail(
                options.DestinationFolder ?? ".",
                $"Compose failed: {ex.Message}");
        }

        progress?.Report(new HarvestProgress("Writing...", files.Count, files.Count));

        var sinkOptions = new OutputSinkOptions(
            DestinationFolder: options.DestinationFolder,
            Extension: options.Extension,
            FileName: options.FileName,
            OpenFolderAfterSave: true);

        return await _sink.SaveAsync(document, sinkOptions, progress: null, cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed record HarvestOptions(
    string? DestinationFolder,
    string? FileName,
    string Extension,
    OutputMode Mode,
    string DelimiterTemplate,
    bool TwoBlankLines,
    bool IncludeTableOfContents,
    bool IncludeManifest,
    bool PreserveFolderStructure,
    bool IncludeHeaders,
    bool IncludeCodeFences);

public sealed record HarvestProgress(
    string Stage,
    int Completed,
    int Total);
