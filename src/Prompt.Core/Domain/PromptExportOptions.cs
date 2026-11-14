// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptExportOptions.cs
// PURPOSE:  Input to IPromptExporter. Carries the destination, the format
//           extension, an optional file name base, the overwrite policy, and
//           whether to open the destination folder after a successful save.
// RELATION: Consumed by IPromptExporter.ExportSingleAsync / ExportManyAsync
//           (P3.2 / P3.3). The extension is resolved against
//           IFormatWriterRegistry.GetByExtension.
// NOTES:    Extension includes the leading dot (".md", ".docx"). When
//           FileName is null, the exporter uses the prompt's Title as the
//           base name; for multi-file export it uses the Category /
//           SubCategory / Title path. The record is an input shape, not a
//           result; it does not carry the outcome of the write.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Input to <c>IPromptExporter</c>. Specifies where and how to write.
/// </summary>
/// <param name="DestinationFolder">Absolute path to write into.</param>
/// <param name="Extension">
/// File extension including the leading dot (e.g. ".md"). Resolved against
/// <c>IFormatWriterRegistry.GetByExtension</c>.
/// </param>
/// <param name="FileName">
/// Optional base name without extension. Null or empty means "derive from the
/// prompt" (single export) or "use Category/SubCategory/Title" (multi export).
/// </param>
/// <param name="Overwrite">True to replace an existing file.</param>
/// <param name="OpenFolderAfterSave">
/// True to open <paramref name="DestinationFolder"/> after a successful write.
/// </param>
public sealed record PromptExportOptions(
    string DestinationFolder,
    string Extension,
    string? FileName,
    bool Overwrite,
    bool OpenFolderAfterSave);