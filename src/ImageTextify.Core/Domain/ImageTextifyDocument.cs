// ============================================================================
// FILE:     src/ImageTextify.Core/Domain/ImageTextifyDocument.cs
// PURPOSE:  The export payload. Carries the ordered results plus a
//           schema version that describes the shape of the serialized
//           document. A change to the shape is a version bump, not a
//           silent edit.
// RELATION: Consumed by the export adapter that hands the document to
//           the existing IFormatWriterRegistry in Phase 4.
// ============================================================================

namespace ImageTextify.Core.Domain;

/// <summary>
/// The export payload for an ImageTextify document.
/// </summary>
/// <param name="Items">
/// The per-file results in first-seen order. Never null; may be empty
/// when the queue produced nothing. Order is preserved through export.
/// </param>
/// <param name="SchemaVersion">
/// The version of the serialized shape, for example <c>1.0</c>. Never
/// null or empty. A change to the JSON shape is a version bump.
/// </param>
public sealed record ImageTextifyDocument(
    IReadOnlyList<ImageTextifyResult> Items,
    string SchemaVersion)
{
    /// <summary>
    /// The schema version emitted by the first release.
    /// </summary>
    public const string CurrentSchemaVersion = "1.0";
}