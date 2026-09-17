// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/ComposedDocument.cs
// PURPOSE:  Assembled output ready for a format writer. Carries the ordered
//           list of file contents plus every user choice that affects
//           layout. Defaults are CLEAN: no headers, no fences, no TOC, no
//           manifest. Every decoration is opt-in.
// RELATION: Produced by IBundleComposer. Consumed by IFormatWriter.
// ============================================================================

namespace FileHarvester.Core.Domain;

public sealed record ComposedDocument(
    IReadOnlyList<FileContent> Files,
    OutputMode Mode,
    string DelimiterTemplate,
    bool TwoBlankLines,
    bool IncludeTableOfContents,
    bool IncludeManifest,
    bool PreserveFolderStructure = true,
    bool IncludeHeaders = false,
    bool IncludeCodeFences = false)
{
    public const string DefaultDelimiter = "----- {path} -----";

    public static ComposedDocument ForSingleMerged(
        IReadOnlyList<FileContent> files) =>
        new(
            Files: files,
            Mode: OutputMode.SingleMerged,
            DelimiterTemplate: DefaultDelimiter,
            TwoBlankLines: true,
            IncludeTableOfContents: false,
            IncludeManifest: false,
            PreserveFolderStructure: false,
            IncludeHeaders: true,
            IncludeCodeFences: false);

    public static ComposedDocument ForSeparateFiles(
        IReadOnlyList<FileContent> files,
        bool preserveFolderStructure = true) =>
        new(
            Files: files,
            Mode: OutputMode.SeparateFiles,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false,
            PreserveFolderStructure: preserveFolderStructure,
            IncludeHeaders: false,
            IncludeCodeFences: false);
}
