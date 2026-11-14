// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/FakeImageOcrEngine.cs
// PURPOSE:  Test double for IImageOcrEngine. Returns a configurable OcrResult,
//           optionally throws a configurable exception, and counts invocations.
//           Never reads the stream; the real engine does that, and a fake that
//           consumed the stream would fail on an already-read or non-readable
//           source. This fake proves the caller opened the file and called the
//           engine, and nothing more.
// RELATION: Created by ImageTextifyExtractCommandTests and used by
//           CompositionConsumerTests. Sibling of FakeImageTextifySettingsStore
//           and FakeQwenVisionService.
// ============================================================================

using ImageTextify.Core.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// A configurable test double for <see cref="IImageOcrEngine"/>.
/// </summary>
public sealed class FakeImageOcrEngine : IImageOcrEngine
{
    /// <summary>The result returned by every successful call. Never null.</summary>
    public OcrResult ResultToReturn { get; set; } = new OcrResult(
        string.Empty, null, Array.Empty<string>(), OcrTextKind.Unknown, Array.Empty<OcrLineResult>());

    /// <summary>When set, the next call throws this exception instead of returning.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>
    /// Optional per-call exception selector. Receives the 1-based call
    /// number and returns an exception to throw on that call, or null to
    /// return <see cref="ResultToReturn"/>. Ignored when
    /// <see cref="ExceptionToThrow"/> is set. Used by tests that need a
    /// failure on one specific call rather than every call.
    /// </summary>
    public Func<int, Exception?>? ExceptionSelector { get; set; }

    /// <summary>The number of times ExtractAsync has been called.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc/>
    public Task<OcrResult> ExtractAsync(
        Stream imageStream,
        ImageOcrOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentNullException.ThrowIfNull(options);
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        if (ExceptionToThrow is not null) throw ExceptionToThrow;
        var selected = ExceptionSelector?.Invoke(CallCount);
        if (selected is not null) throw selected;
        return Task.FromResult(ResultToReturn);
    }
}
