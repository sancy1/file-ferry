// filepath: src/FileHarvester.Tests/ImageTextify/FakeQwenVisionService.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4d
// purpose:  Fake implementation of IQwenVisionService for testing.
//           Records every prompt it receives. Returns configurable output.
//           Does not load or run a real Qwen model.
//
// RELATIONSHIPS
//   Implements : IQwenVisionService
//   Depends on : IQwenVisionService
//   Used by    : QwenHandwritingRecognizerTests
//   See also   : IQwenVisionService.cs, QwenHandwritingRecognizer.cs
//
// NOTES
//   - PromptsReceived records every prompt string passed to GenerateAsync,
//     in order, so tests can verify the exact prompt.
//   - OutputToReturn is returned verbatim from every call. Set it before
//     invoking RecognizeAsync to simulate specific Qwen output.
//   - CallCount tracks the number of GenerateAsync invocations.
//   - Never runs a real model. Fast and deterministic.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Platform.Imaging;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Fake <see cref="IQwenVisionService"/> for unit tests.
/// </summary>
/// <remarks>
/// Records prompts, returns configurable output, tracks call count.
/// Does not run a real Qwen model.
/// </remarks>
public sealed class FakeQwenVisionService : IQwenVisionService
{
    /// <summary>
    /// The output this fake returns from every GenerateAsync call.
    /// </summary>
    public string OutputToReturn { get; set; } = string.Empty;

    /// <summary>
    /// The prompt strings received by GenerateAsync, in call order.
    /// </summary>
    public List<string> PromptsReceived { get; } = new();

    /// <summary>
    /// The number of GenerateAsync invocations.
    /// </summary>
    public int CallCount { get; private set; }

    /// <summary>
    /// When set, GenerateAsync throws this exception instead of returning.
    /// </summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <inheritdoc/>
    public Task<string> GenerateAsync(
        ReadOnlyMemory<byte> imageBytes,
        string prompt,
        int maxTokens = 256,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        PromptsReceived.Add(prompt);
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;
        return Task.FromResult(OutputToReturn);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

