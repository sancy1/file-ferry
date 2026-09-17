// ============================================================================
// FILE:     src\FileHarvester.Core/Implementation/BundleComposer.cs
// PURPOSE:  Implementation of IBundleComposer. Assembles FileContent records
//           into a ComposedDocument according to the user's output options.
//           Pure in-memory transformation: no I/O, no resilience.
//           Clean-output defaults: only the content, no decorations.
// RELATION: Implements IBundleComposer. Registered in AddFileHarvester.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Implementation;

public sealed class BundleComposer : IBundleComposer
{
    /// <inheritdoc/>
    public ComposedDocument Compose(
        IReadOnlyList<FileContent> files,
        ComposerOptions options)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        var delimiter = options.Mode == OutputMode.SingleMerged
            ? options.DelimiterTemplate
            : string.Empty;

        var twoBlankLines = options.Mode == OutputMode.SingleMerged
            && options.TwoBlankLines;

        return new ComposedDocument(
            Files: files,
            Mode: options.Mode,
            DelimiterTemplate: delimiter,
            TwoBlankLines: twoBlankLines,
            IncludeTableOfContents: options.IncludeTableOfContents,
            IncludeManifest: options.IncludeManifest,
            PreserveFolderStructure: options.PreserveFolderStructure,
            IncludeHeaders: options.IncludeHeaders,
            IncludeCodeFences: options.IncludeCodeFences);
    }

    /// <summary>
    /// Renders the merged text. Headers (delimiters) appear only when the
    /// document was built with IncludeHeaders = true. Otherwise this is a
    /// pure concatenation of the file contents.
    /// </summary>
    public static string RenderMergedText(ComposedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Mode != OutputMode.SingleMerged)
        {
            throw new InvalidOperationException(
                "RenderMergedText is only valid in SingleMerged mode.");
        }

        var sb = new System.Text.StringBuilder();

        for (var i = 0; i < document.Files.Count; i++)
        {
            var file = document.Files[i];

            if (document.IncludeHeaders)
            {
                var delimiter = DelimiterTemplate.Expand(
                    document.DelimiterTemplate, file, i);

                if (delimiter.Length > 0)
                {
                    sb.Append(delimiter);
                    sb.Append('\n');
                    sb.Append('\n');
                }
            }

            if (file.IsUnreadable)
            {
                sb.Append("[UNREADABLE: ");
                sb.Append(file.ReadError ?? "unknown reason");
                sb.Append(']');
            }
            else if (file.IsBinary)
            {
                sb.Append("[BINARY: ");
                sb.Append(file.RawBytes.Length);
                sb.Append(" bytes, SHA-256=");
                sb.Append(file.Sha256);
                sb.Append(']');
            }
            else
            {
                sb.Append(file.DecodedText);
            }

            if (i < document.Files.Count - 1)
            {
                sb.Append('\n');
                if (document.TwoBlankLines) sb.Append('\n');
            }
        }

        return sb.ToString();
    }
}
