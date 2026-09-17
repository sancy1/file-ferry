// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/FileContent.cs
// PURPOSE:  Domain record representing the bytes and decoded text of one
//           file, plus the metadata needed to reconstruct it byte-for-byte
//           or embed it in a bundle.
// RELATION: Produced by IContentReader (Phase 1.2) from a ResolvedFile.
//           Consumed by IBundleComposer (Phase 1.3) which assembles the
//           output. The Unreadable factory creates a placeholder that the
//           composer can render as a note without breaking the bundle.
// ============================================================================

using System.Text;

namespace FileHarvester.Core.Domain;

/// <summary>
/// The contents of a file that the reader has successfully loaded.
/// </summary>
/// <param name="File">
/// The ResolvedFile this content came from. Carries the original request
/// and the absolute path.
/// </param>
/// <param name="RawBytes">
/// The exact bytes read from disk, unchanged. For text files these are
/// the bytes the reader decoded; for binary files they are the raw
/// payload. Used by the ZIP writer (Phase 4.2) to preserve fidelity.
/// </param>
/// <param name="DecodedText">
/// The decoded text of the file, using the detected encoding. For binary
/// files this is an empty string. Line endings are preserved exactly as
/// they appeared in the original bytes.
/// </param>
/// <param name="Encoding">
/// The encoding the reader detected and used to produce the decoded text.
/// </param>
/// <param name="IsBinary">
/// True when the reader detected binary content.
/// </param>
/// <param name="Sha256">
/// Lowercase hexadecimal SHA-256 hash of the raw bytes.
/// </param>
/// <param name="ReadError">
/// Non-null when the file could not be read at all.
/// </param>
public sealed record FileContent(
    ResolvedFile File,
    byte[] RawBytes,
    string DecodedText,
    Encoding Encoding,
    bool IsBinary,
    string Sha256,
    string? ReadError = null)
{
    /// <summary>
    /// True when the reader could not load this file. The composer uses
    /// this to render a note instead of content.
    /// </summary>
    public bool IsUnreadable => ReadError is not null;

    /// <summary>
    /// Builds an unreadable placeholder for a file that could not be read.
    /// All content fields are empty; only the path and reason are retained.
    /// </summary>
    public static FileContent Unreadable(ResolvedFile file, string reason)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(reason);

        return new FileContent(
            File: file,
            RawBytes: Array.Empty<byte>(),
            DecodedText: string.Empty,
            Encoding: Encoding.UTF8,
            IsBinary: false,
            Sha256: string.Empty,
            ReadError: reason);
    }
}
