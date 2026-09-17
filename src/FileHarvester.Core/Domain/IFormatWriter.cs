// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/IFormatWriter.cs
// PURPOSE:  Plugin contract for every output format File-Ferry supports.
//           Each format is one class implementing this interface; the UI's
//           format dropdown is populated by enumerating the registered
//           implementations (Phase 3.3).
// RELATION: Consumed by IOutputSink (Phase 6.0), which selects the writer
//           for the user-chosen format and invokes it. Implementations
//           arrive in Phase 4.0 (txt, md, json, html), Phase 4.1 (docx,
//           pdf, xlsx), and Phase 4.2 (zip, csv).
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Writes a ComposedDocument in one specific output format.
/// </summary>
/// <remarks>
/// Implementations are stateless and thread-safe. The same instance may
/// be used concurrently for different documents. Any per-call state must
/// live on the stack, not on the instance.
/// </remarks>
public interface IFormatWriter
{
    /// <summary>
    /// The file extension this writer produces, including the leading dot.
    /// Used by the UI to build the format dropdown and to compute output
    /// file names. Examples: ".txt", ".md", ".docx", ".pdf".
    /// </summary>
    string Extension { get; }

    /// <summary>
    /// Human-readable name shown in the format dropdown. Examples:
    /// "Plain Text", "Markdown", "Word Document", "PDF".
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// The MIME type this writer produces. Used by the OS file picker's
    /// filter and (later) by any HTTP response layer. Examples:
    /// "text/plain", "text/markdown",
    /// "application/vnd.openxmlformats-officedocument.wordprocessingml.document".
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Whether this writer can stream very large documents without
    /// holding the entire output in memory. Writers that answer
    /// <c>false</c> (e.g., PDF, XLSX) may impose a size ceiling that the
    /// UI surfaces when the document is very large.
    /// </summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Writes the document to the given stream. The caller owns the
    /// stream and is responsible for closing it; the writer must not
    /// close it.
    /// </summary>
    /// <param name="document">
    /// The composed document to render. Never null.
    /// </param>
    /// <param name="output">
    /// The destination stream. Writable. Positioned at the start.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the write. Implementations should honour this at
    /// coarse-grained boundaries (per file, per chapter) rather than
    /// per byte; extreme granularity is not required.
    /// </param>
    Task WriteAsync(
        ComposedDocument document,
        Stream output,
        CancellationToken cancellationToken = default);
}
