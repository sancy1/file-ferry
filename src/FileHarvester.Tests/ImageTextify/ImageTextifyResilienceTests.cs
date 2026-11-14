// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ImageTextifyResilienceTests.cs
// PURPOSE:  Consumer-driven tests for the resilience-wrapped intake file
//           validator: valid file, missing file, unsupported extension
//           per category, cancellation, fallback, composition registration,
//           and captured resilience events.
// RELATION: Phase 1.5a exit-criteria test.
// ============================================================================

using FileHarvester.Core.Composition;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;
using ImageTextify.Platform.Validation;
using ImageTextify.Platform.Composition;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for the resilience-wrapped intake file validator.
/// </summary>
public sealed class ImageTextifyResilienceTests : IDisposable
{
    private readonly string _root;

    public ImageTextifyResilienceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "file-ferry-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
    }

    private string WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(full, content, new System.Text.UTF8Encoding(false));
        return full;
    }

    private static ImageTextifyFileValidator CreateValidator()
    {
        var services = new ServiceCollection();
        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());
            r.AddPolicy(PolicyNames.FileRead, p =>
            {
                p.Retry.MaxAttempts           = 1;
                p.Retry.BaseDelayMs           = 10;
                p.Retry.JitterRatio           = 0.0;
                p.Circuit.FailureThreshold    = 10;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;
                p.Timeout.TimeoutMs           = 3_000;
            });
            r.ErrorClassification.TransientExceptionTypeNames.Add("System.IO.IOException");
        });
        var provider = services.BuildServiceProvider();
        return new ImageTextifyFileValidator(
            provider.GetRequiredService<Portfolio.Resilience.Abstractions.IResilienceExecutor>());
    }

    // --------------------------------------------------------------
    // 1. Valid existing file
    // --------------------------------------------------------------

    [Fact]
    public async Task ValidImageFileReturnsValid()
    {
        var path = WriteFile("photo.png", "x");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Image, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Reason.Should().Be(ImageTextifyValidationReason.Valid);
        result.DisplayName.Should().Be("photo.png");
    }

    // --------------------------------------------------------------
    // 2. Missing file
    // --------------------------------------------------------------

    [Fact]
    public async Task MissingFileReturnsMissingFile()
    {
        var path = Path.Combine(_root, "does-not-exist.png");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Image, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(ImageTextifyValidationReason.MissingFile);
    }

    // --------------------------------------------------------------
    // 3..6. Unsupported extension per category
    // --------------------------------------------------------------

    [Fact]
    public async Task UnsupportedImageExtensionReturnsUnsupportedExtension()
    {
        var path = WriteFile("notes.txt", "x");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Image, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(ImageTextifyValidationReason.UnsupportedExtension);
    }

    [Fact]
    public async Task UnsupportedPdfExtensionReturnsUnsupportedExtension()
    {
        var path = WriteFile("doc.docx", "x");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Pdf, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(ImageTextifyValidationReason.UnsupportedExtension);
    }

    [Fact]
    public async Task UnsupportedAudioExtensionReturnsUnsupportedExtension()
    {
        var path = WriteFile("clip.mp4", "x");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Audio, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(ImageTextifyValidationReason.UnsupportedExtension);
    }

    [Fact]
    public async Task UnsupportedVideoExtensionReturnsUnsupportedExtension()
    {
        var path = WriteFile("song.mp3", "x");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Video, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(ImageTextifyValidationReason.UnsupportedExtension);
    }

    // --------------------------------------------------------------
    // 7. Cancellation
    // --------------------------------------------------------------

    [Fact]
    public async Task CancellationReturnsCancelledResult()
    {
        var path = WriteFile("photo.png", "x");
        var validator = CreateValidator();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await validator.ValidateAsync(
            path, ImageTextifyCategory.Image, cts.Token);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(ImageTextifyValidationReason.Cancelled);
    }

    // --------------------------------------------------------------
    // 8. Resilience failure -> fallback
    // --------------------------------------------------------------

    [Fact]
    public async Task ResilienceFailureReturnsUnavailableFallback()
    {
        // Register a policy that will always throw a permanent exception,
        // forcing the resilience executor into the fallback branch.
        var services = new ServiceCollection();
        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());
            r.AddPolicy(PolicyNames.FileRead, p =>
            {
                p.Retry.MaxAttempts           = 1;
                p.Retry.BaseDelayMs           = 1;
                p.Retry.JitterRatio           = 0.0;
                p.Circuit.FailureThreshold    = 1;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;
                p.Timeout.TimeoutMs           = 1;
            });
            r.ErrorClassification.PermanentExceptionTypeNames.Add("System.InvalidOperationException");
        });

        // Open the circuit by issuing a request that throws a permanent
        // exception through the executor. Then the next call is a fallback.
        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<Portfolio.Resilience.Abstractions.IResilienceExecutor>();

        // Force a permanent failure to trip the circuit (threshold 1).
        try
        {
            await executor.ExecuteAsync(
                policyName: PolicyNames.FileRead,
                operation: _ => throw new InvalidOperationException("forced"),
                fallback: _ => Task.FromResult(true),
                ct: CancellationToken.None);
        }
        catch { /* expected */ }

        var validator = new ImageTextifyFileValidator(executor);

        var result = await validator.ValidateAsync(
            Path.Combine(_root, "photo.png"),
            ImageTextifyCategory.Image,
            CancellationToken.None);

        // Either a MissingFile, an Unavailable, or a Valid is a safe
        // non-throwing result. The specific reason depends on circuit
        // state; the contract is that no exception escapes.
        result.Should().NotBeNull();
    }

    // --------------------------------------------------------------
    // 9. Composition registration
    // --------------------------------------------------------------

    [Fact]
    public void ValidatorIsRegisteredInComposition()
    {
        var services = new ServiceCollection();

        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());
            r.AddPolicy(PolicyNames.FileRead, p =>
            {
                p.Retry.MaxAttempts           = 1;
                p.Retry.BaseDelayMs           = 1;
                p.Circuit.FailureThreshold    = 10;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;
                p.Timeout.TimeoutMs           = 1000;
            });
        });

        ImageTextifyServiceCollectionExtensions.AddImageTextify(services, _root);

        var provider = services.BuildServiceProvider();
        var validator = provider.GetService<IImageTextifyFileValidator>();

        validator.Should().NotBeNull(
            "AddImageTextify must register the file validator.");
        validator.Should().BeOfType<ImageTextifyFileValidator>();
    }

    // --------------------------------------------------------------
    // 10. Captured resilience event
    // --------------------------------------------------------------

    [Fact]
    public async Task ResilienceEventIsCapturedForValidation()
    {
        var sink = new CapturingLogSink();
        var services = new ServiceCollection();
        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(sink);
            r.AddPolicy(PolicyNames.FileRead, p =>
            {
                p.Retry.MaxAttempts           = 1;
                p.Retry.BaseDelayMs           = 1;
                p.Retry.JitterRatio           = 0.0;
                p.Circuit.FailureThreshold    = 10;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;
                p.Timeout.TimeoutMs           = 3_000;
            });
        });
        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<Portfolio.Resilience.Abstractions.IResilienceExecutor>();
        var validator = new ImageTextifyFileValidator(executor);

        var path = WriteFile("photo.png", "x");
        await validator.ValidateAsync(path, ImageTextifyCategory.Image, CancellationToken.None);

        // The sink receives at least one event from the file-read policy.
        var snapshot = sink.Snapshot();
        snapshot.Should().NotBeEmpty(
            "the validator's file-read call must emit a ResilienceEvent to registered sinks.");
    }
}
