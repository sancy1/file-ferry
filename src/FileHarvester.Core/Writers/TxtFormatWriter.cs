// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/TxtFormatWriter.cs
// PURPOSE:  Plain text output. Clean by default: only the file contents.
//           Delimiters appear only when IncludeHeaders is true.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;

namespace FileHarvester.Core.Writers;

public sealed class TxtFormatWriter : IFormatWriter
{
    public string Extension => ".txt";
    public string DisplayName => "Plain Text";
    public string MimeType => "text/plain";
    public bool SupportsStreaming => true;

    public async Task WriteAsync(
        ComposedDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        if (document.Mode != OutputMode.SingleMerged)
        {
            throw new NotSupportedException(
                "TxtFormatWriter only renders SingleMerged documents.");
        }

        // RenderMergedText already respects IncludeHeaders.
        var text = BundleComposer.RenderMergedText(document);

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(text);

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
