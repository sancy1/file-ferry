// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/IOutputSink.cs
// PURPOSE:  Contract for writing a composed document to disk.
// RELATION: Implemented by OutputSink. Consumed by HarvestJobRunner.
// ============================================================================

namespace FileHarvester.Core.Domain;

public interface IOutputSink
{
    Task<SaveResult> SaveAsync(
        ComposedDocument document,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Where and how to write the output.
/// </summary>
/// <param name="DestinationFolder">
/// Absolute path. Null/empty means "timestamped folder in Downloads".
/// </param>
/// <param name="Extension">Output extension including dot, e.g. ".md".</param>
/// <param name="FileName">
/// Base name for the merged output file, without extension. Null or empty
/// means the sink uses the first source file's base name, or "bundle" as a
/// final fallback. Ignored in SeparateFiles mode, where each output is
/// named from its source.
/// </param>
/// <param name="OpenFolderAfterSave">
/// When true, the caller (UI) opens the destination folder on success.
/// </param>
public sealed record OutputSinkOptions(
    string? DestinationFolder,
    string Extension,
    string? FileName = null,
    bool OpenFolderAfterSave = true);

public sealed record SaveProgress(
    string Stage,
    int BytesWritten,
    int BytesTotal);
