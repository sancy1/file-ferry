// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/IBundleComposer.cs
// PURPOSE:  Contract for the service that assembles FileContent records
//           into a ComposedDocument according to the user's output options.
//           Defaults are clean output.
// RELATION: Implemented by BundleComposer. Consumed by HarvestJobRunner.
// ============================================================================

namespace FileHarvester.Core.Domain;

public interface IBundleComposer
{
    ComposedDocument Compose(
        IReadOnlyList<FileContent> files,
        ComposerOptions options);
}

public sealed record ComposerOptions(
    OutputMode Mode,
    string DelimiterTemplate = ComposedDocument.DefaultDelimiter,
    bool TwoBlankLines = true,
    bool IncludeTableOfContents = false,
    bool IncludeManifest = false,
    bool PreserveFolderStructure = true,
    bool IncludeHeaders = false,
    bool IncludeCodeFences = false)
{
    public static ComposerOptions SingleMerged() =>
        new(OutputMode.SingleMerged);

    public static ComposerOptions SeparateFiles(bool preserveFolderStructure = true) =>
        new(
            Mode: OutputMode.SeparateFiles,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false,
            PreserveFolderStructure: preserveFolderStructure,
            IncludeHeaders: false,
            IncludeCodeFences: false);
}
