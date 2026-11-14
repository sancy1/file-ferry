// filepath: src/FileHarvester.Tests/ImageTextify/FakePdfTextExtractor.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.6-PDF-1
// purpose:  Test double for IPdfTextExtractor. Returns a configurable
//           PdfTextResult and counts invocations. Never reads the stream.
// RELATION: Used by ImageTextifyExtractCommandTests, ImageTextifyIntakeTests,
//           and CompositionConsumerTests as the fourth ImageTextifyViewModel
//           constructor argument.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// A configurable test double for <see cref="IPdfTextExtractor"/>.
/// </summary>
public sealed class FakePdfTextExtractor : IPdfTextExtractor
{
    /// <summary>The result returned by every call. Defaults to no pages.</summary>
    public PdfTextResult ResultToReturn { get; set; } = new PdfTextResult(Array.Empty<PdfPageText>());

    /// <summary>The number of times ExtractAsync has been called.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc/>
    public Task<PdfTextResult> ExtractAsync(
        Stream pdfStream,
        PdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        ArgumentNullException.ThrowIfNull(options);
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResultToReturn);
    }
}
