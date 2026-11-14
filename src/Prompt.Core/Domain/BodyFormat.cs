// ============================================================================
// FILE:     src/Prompt.Core/Domain/BodyFormat.cs
// PURPOSE:  Enumerates the storage format of a Prompt body. The format
//           determines how the body is rendered by the viewer and how the
//           exporter adapts it for each format writer.
// RELATION: Referenced by Prompt (Prompt.BodyFormat). Consumed by the viewer
//           (P2.4) and the exporter adapter (P3.1). No logic.
// NOTES:    PlainText and Markdown are both text bodies that differ only in
//           rendering. Html is the rich-text path when P2.6 opens. The
//           stored format does not constrain the export format.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// The storage format of a <see cref="Prompt"/> body.
/// </summary>
public enum BodyFormat
{
    /// <summary>Plain text. No markup interpretation.</summary>
    PlainText = 0,

    /// <summary>Markdown source. Rendered as Markdown by the viewer (P2.4).</summary>
    Markdown = 1,

    /// <summary>HTML. Rendered as HTML by the viewer. Reserved for P2.6 (rich text).</summary>
    Html = 2,
}