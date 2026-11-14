// ============================================================================
// FILE:     src/Prompt.Platform/Export/PromptDocumentAdapter.cs
// PURPOSE:  Adapts a Prompt to the harvester's ComposedDocument for a given
//           IFormatWriter. The prompt's body becomes one FileContent; the
//           ComposedDocument is SingleMerged; the code-fences flag is ON only
//           when the body is Markdown and the writer is not .md (the raw
//           markdown is preserved as a code block in the non-markdown output).
// RELATION: Produced for P3.1 (Phase 3 - import/export). Consumes
//           IFormatWriter, ComposedDocument, FileContent, ResolvedFile,
//           FileRequest (FileHarvester.Core.Domain), and Prompt, BodyFormat
//           (Prompt.Core.Domain). Consumed by PromptExporter (P3.2), which
//           invokes IFormatWriter.WriteAsync with the result.
// NOTES:    - The FileRequest/ResolvedFile/FileContent are synthetic: the
//             prompt is not a file on disk. The AbsolutePath is the
//             sanitized title + the writer's extension; the confidence is
//             Exact (the body is in memory).
//           - The SHA-256 is the body's UTF-8 bytes, lowercase hex.
//           - The code-fences heuristic: Markdown body + non-.md writer ->
//             true (the raw markdown is preserved as a code block); any other
//             combination -> false. The MarkdownFormatWriter wraps the body
//             in ```<language>``` when the flag is on, with the language
//             derived from the AbsolutePath's extension.
//           - The FileContent's ResolvedFile, FileRequest, ResolutionConfidence,
//             and the Prompt record's shape are read-verified from their
//             source files; no reconstruction from memory (F-260).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace Prompt.Platform.Export;

/// <summary>
/// Adapts a Prompt to a ComposedDocument for the given IFormatWriter.
/// </summary>
public static class PromptDocumentAdapter
{
    /// <summary>
    /// Builds the ComposedDocument for a single prompt and writer: one
    /// FileContent, SingleMerged mode, no per-file headers, and the
    /// code-fences flag set by the markdown-to-non-markdown heuristic.
    /// </summary>
    /// <param name="prompt">The prompt to adapt. Never null.</param>
    /// <param name="writer">The format writer. Never null.</param>
    /// <returns>The ComposedDocument.</returns>
    public static ComposedDocument ToComposedDocument(PromptRecord prompt, IFormatWriter writer)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(writer);

        // The synthetic FileRequest - index 0, the prompt's title, no
        // hinted directory.
        var request = new FileRequest(0, prompt.Title);

        // The synthetic absolute path - the sanitized title + the writer's
        // extension. The writer never touches disk; the path is the output
        // file name the writer would produce.
        var fileName = SanitizeFileName(prompt.Title) + writer.Extension;

        // The body's UTF-8 bytes and the lowercase-hex SHA-256.
        var rawBytes = Encoding.UTF8.GetBytes(prompt.Body);
        var sha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();

        // The synthetic ResolvedFile - Exact confidence (the body is in memory).
        var resolvedFile = new ResolvedFile(
            Request: request,
            AbsolutePath: fileName,
            SizeBytes: rawBytes.Length,
            LastModifiedUtc: prompt.UpdatedAt.UtcDateTime,
            Confidence: ResolutionConfidence.Exact);

        // The FileContent - the prompt's body as the text, the UTF-8 encoding,
        // not binary, the SHA-256.
        var fileContent = new FileContent(
            File: resolvedFile,
            RawBytes: rawBytes,
            DecodedText: prompt.Body,
            Encoding: Encoding.UTF8,
            IsBinary: false,
            Sha256: sha256);

        // The ComposedDocument - SingleMerged, then the format overrides.
        return ComposedDocument.ForSingleMerged(new[] { fileContent }) with
        {
            IncludeHeaders = false,
            IncludeCodeFences = ShouldIncludeCodeFences(prompt, writer),
        };
    }

    /// <summary>
    /// The code-fences flag: ON only when the body is Markdown and the
    /// writer is not .md (the raw markdown preserved as a code block in
    /// the non-markdown output; a .md writer already renders the body as
    /// markdown).
    /// </summary>
    private static bool ShouldIncludeCodeFences(PromptRecord prompt, IFormatWriter writer) =>
        prompt.Format == BodyFormat.Markdown && writer.Extension != ".md";

    /// <summary>
    /// Replaces invalid filename characters with underscores and trims the
    /// result; an empty result becomes "Untitled".
    /// </summary>
    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "Untitled" : result;
    }
}