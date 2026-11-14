// ============================================================================
// FILE:     src/CodeViewer.Platform/FileSystem/ProjectFileReadResult.cs
// PURPOSE:  The outcome of reading one file for the Code Content viewer.
//           Carries the decoded text for previewable text files, or a
//           no-preview reason for binary, unsupported, or unreadable files.
//           A file that cannot be previewed still appears in the Project
//           Browser and Project Tree Structure; only its code card shows
//           the notice.
// RELATION: Returned by ProjectFileReader.ReadAsync. Consumed by the
//           CodeFileCardViewModel.
// ============================================================================

namespace CodeViewer.Platform.FileSystem;

/// <summary>
/// Why a file has no code preview.
/// </summary>
public enum NoPreviewReason
{
    /// <summary>No preview issue; the file has content.</summary>
    None,

    /// <summary>The file appears to be binary (null bytes in the probe).</summary>
    Binary,

    /// <summary>The file could not be read (permissions, I/O error, deleted).</summary>
    Unreadable,
}

/// <summary>
/// The result of reading a file for preview.
/// </summary>
public sealed class ProjectFileReadResult
{
    private ProjectFileReadResult(
        string text, int lineCount, NoPreviewReason reason, string? error)
    {
        Text = text;
        LineCount = lineCount;
        Reason = reason;
        Error = error;
    }

    /// <summary>The decoded text. Empty when there is no preview.</summary>
    public string Text { get; }

    /// <summary>The number of lines in <see cref="Text"/>.</summary>
    public int LineCount { get; }

    /// <summary>Why there is no preview; <see cref="NoPreviewReason.None"/> on success.</summary>
    public NoPreviewReason Reason { get; }

    /// <summary>The underlying error message when <see cref="Reason"/> is Unreadable.</summary>
    public string? Error { get; }

    /// <summary>True when the file has previewable text.</summary>
    public bool HasPreview => Reason == NoPreviewReason.None;

    /// <summary>A successful text preview.</summary>
    public static ProjectFileReadResult Success(string text, int lineCount)
        => new(text, lineCount, NoPreviewReason.None, error: null);

    /// <summary>A binary file: no preview, but the file stays visible.</summary>
    public static ProjectFileReadResult Binary()
        => new(string.Empty, 0, NoPreviewReason.Binary, error: null);

    /// <summary>An unreadable file: no preview, but the file stays visible.</summary>
    public static ProjectFileReadResult Unreadable(string error)
        => new(string.Empty, 0, NoPreviewReason.Unreadable, error);
}
