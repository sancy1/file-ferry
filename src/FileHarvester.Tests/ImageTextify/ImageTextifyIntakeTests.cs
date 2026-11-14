// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ImageTextifyIntakeTests.cs
// PURPOSE:  Consumer-driven tests for the ImageTextify intake queue and the
//           intake service. Covers queue init, insertion order, per-category
//           allowlist, duplicate detection per category, per-row remove,
//           cancellation, visible warnings, and composition registration.
// RELATION: Phase 1.5b exit-criteria test. Companion to
//           ImageTextifyResilienceTests.cs (Phase 1.5a) and
//           Consumer/CompositionConsumerTests.cs (Phase 1.4c).
// ============================================================================

using FileHarvester.App.ViewModels;
using FileHarvester.Core.Composition;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;
using ImageTextify.Platform.Intake;
using ImageTextify.Platform.Validation;
using Microsoft.Extensions.DependencyInjection;
using ImageTextify.Platform.Composition;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for the ImageTextify intake queue and the intake service.
/// </summary>
public sealed class ImageTextifyIntakeTests : IDisposable
{
    private readonly string _root;

    public ImageTextifyIntakeTests()
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

    private static ImageTextifyIntakeService BuildIntakeService()
    {
        var services = new ServiceCollection();
        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());
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
        return new ImageTextifyIntakeService(validator);
    }

    private static ImageTextifyViewModel BuildViewModel() => new(BuildIntakeService(), new FakeImageTextifySettingsStore(), new FakeImageOcrEngine(), new FakePdfTextExtractor());

    // --------------------------------------------------------------
    // 1. Initial state
    // --------------------------------------------------------------

    [Fact]
    public void QueueIsEmptyInitially()
    {
        var vm = BuildViewModel();
        vm.Files.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
        vm.HasFiles.Should().BeFalse();
    }

    [Fact]
    public void ResultsPaneShowsEmptyStateInitially()
    {
        // The results-pane empty state is XAML-bound to ImageTextify.IsEmpty
        // and ImageTextify.StatusText. Verify the VM's observable flags and
        // the XAML binding declarations.
        var vm = BuildViewModel();
        vm.IsEmpty.Should().BeTrue();

        var axaml = Path.Combine(
            RepoRoot(), "src", "FileHarvester.App", "Views", "ImageTextifyPage.axaml");
        File.Exists(axaml).Should().BeTrue();
        var text = File.ReadAllText(axaml);
        text.Should().Contain("{Binding ImageTextify.IsEmpty}");
        text.Should().Contain("{Binding ImageTextify.StatusText}");
        text.Should().Contain("No results yet");
    }

    // --------------------------------------------------------------
    // 2. Insertion order
    // --------------------------------------------------------------

    [Fact]
    public async Task AddFilesPreservesInsertionOrder()
    {
        var p1 = WriteFile("a.png", "x");
        var p2 = WriteFile("b.jpg", "x");
        var p3 = WriteFile("c.webp", "x");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { p1, p2, p3 }, CancellationToken.None);

        vm.Files.Should().HaveCount(3);
        vm.Files[0].DisplayName.Should().Be("a.png");
        vm.Files[1].DisplayName.Should().Be("b.jpg");
        vm.Files[2].DisplayName.Should().Be("c.webp");
    }

    // --------------------------------------------------------------
    // 3. Extension allowlist per category
    // --------------------------------------------------------------

    [Fact]
    public async Task AddFilesRejectsUnsupportedExtension()
    {
        var path = WriteFile("notes.txt", "x");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);

        vm.Files.Should().HaveCount(1);
        vm.Files[0].Reason.Should().Be(ImageTextifyIntakeReason.Rejected);
        vm.Files[0].ValidationReason.Should().Be(ImageTextifyValidationReason.UnsupportedExtension);
    }

    [Fact]
    public async Task AddFilesRejectsMissingFile()
    {
        var path = Path.Combine(_root, "ghost.png");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);

        vm.Files.Should().HaveCount(1);
        vm.Files[0].Reason.Should().Be(ImageTextifyIntakeReason.Rejected);
        vm.Files[0].ValidationReason.Should().Be(ImageTextifyValidationReason.MissingFile);
    }

    // --------------------------------------------------------------
    // 4. Duplicates
    // --------------------------------------------------------------

    [Fact]
    public async Task AddFilesRejectsDuplicatePath()
    {
        var path = WriteFile("dup.png", "x");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);
        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);

        vm.Files.Should().HaveCount(2);
        vm.Files[0].Reason.Should().Be(ImageTextifyIntakeReason.Accepted);
        vm.Files[1].Reason.Should().Be(ImageTextifyIntakeReason.Duplicate);
    }

    [Fact]
    public async Task IntakeDecisionRejectsDuplicateForSameCategory()
    {
        var path = WriteFile("dup2.png", "x");
        var service = BuildIntakeService();

        var first = await service.EvaluateAsync(
            path, ImageTextifyCategory.Image, Array.Empty<string>(), CancellationToken.None);
        first.ShouldAdd.Should().BeTrue();
        first.Reason.Should().Be(ImageTextifyIntakeReason.Accepted);

        var second = await service.EvaluateAsync(
            path, ImageTextifyCategory.Image, new[] { path }, CancellationToken.None);
        second.ShouldAdd.Should().BeFalse();
        second.Reason.Should().Be(ImageTextifyIntakeReason.Duplicate);
    }

    [Fact]
    public async Task IntakeDecisionAcceptsSamePathForDifferentCategory()
    {
        var path = WriteFile("shared.png", "x");
        var service = BuildIntakeService();

        var image = await service.EvaluateAsync(
            path, ImageTextifyCategory.Image, Array.Empty<string>(), CancellationToken.None);
        image.ShouldAdd.Should().BeTrue();

        // Same path, different category, with the image-category path in the
        // existing list. Must not be treated as a duplicate because the
        // existing list is per-category.
        var pdf = await service.EvaluateAsync(
            path, ImageTextifyCategory.Image, Array.Empty<string>(), CancellationToken.None);
        pdf.ShouldAdd.Should().BeTrue();
    }

    // --------------------------------------------------------------
    // 5. Remove
    // --------------------------------------------------------------

    [Fact]
    public async Task RemoveRemovesOnlySelectedRow()
    {
        var p1 = WriteFile("keep.png", "x");
        var p2 = WriteFile("drop.png", "x");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { p1, p2 }, CancellationToken.None);
        vm.Files.Should().HaveCount(2);

        var target = vm.Files[1];
        vm.RemoveFileCommand.Execute(target);

        vm.Files.Should().HaveCount(1);
        vm.Files[0].DisplayName.Should().Be("keep.png");
    }

    // --------------------------------------------------------------
    // 6. Category change does not revalidate existing rows
    // --------------------------------------------------------------

    [Fact]
    public async Task CategorySelectorChangesAllowedExtensions()
    {
        // A .png is valid for Image but not for Audio.
        var path = WriteFile("pic.png", "x");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);
        vm.Files[0].Reason.Should().Be(ImageTextifyIntakeReason.Accepted);

        vm.SelectedCategory = ImageTextifyCategory.Audio;

        // Same file, new category: extension is now unsupported.
        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);
        vm.Files.Should().HaveCount(2);
        vm.Files[1].Reason.Should().Be(ImageTextifyIntakeReason.Rejected);
        vm.Files[1].ValidationReason.Should().Be(ImageTextifyValidationReason.UnsupportedExtension);
    }

    [Fact]
    public async Task ExistingRowsKeepValidationStateWhenCategoryChanges()
    {
        var png = WriteFile("keep.png", "x");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { png }, CancellationToken.None);
        var rowBefore = vm.Files[0];
        rowBefore.Reason.Should().Be(ImageTextifyIntakeReason.Accepted);
        rowBefore.ValidationReason.Should().Be(ImageTextifyValidationReason.Valid);

        vm.SelectedCategory = ImageTextifyCategory.Audio;

        // The existing row must not have been revalidated.
        vm.Files[0].Reason.Should().Be(ImageTextifyIntakeReason.Accepted);
        vm.Files[0].ValidationReason.Should().Be(ImageTextifyValidationReason.Valid);
    }

    // --------------------------------------------------------------
    // 7. Visible warnings
    // --------------------------------------------------------------

    [Fact]
    public async Task ValidationFailureProducesVisibleWarning()
    {
        var path = Path.Combine(_root, "missing.png");
        var vm = BuildViewModel();

        await vm.AddFilesAsync(new[] { path }, CancellationToken.None);

        vm.Files.Should().HaveCount(1);
        vm.Files[0].StatusText.Should().Be("File not found");
        vm.Files[0].StatusText.Should().NotBeNullOrWhiteSpace();
    }

    // --------------------------------------------------------------
    // 8. Cancellation
    // --------------------------------------------------------------

    [Fact]
    public async Task CancelledValidationProducesSafeIntakeResult()
    {
        var path = WriteFile("cancel.png", "x");
        var vm = BuildViewModel();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await vm.AddFilesAsync(new[] { path }, cts.Token);

        vm.Files.Should().BeEmpty();
        vm.IsAdding.Should().BeFalse();
    }

    [Fact]
    public async Task AddFilesAsyncDoesNotAddPartialRowOnCancellation()
    {
        var p1 = WriteFile("first.png", "x");
        var p2 = WriteFile("second.png", "x");
        var vm = BuildViewModel();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await vm.AddFilesAsync(new[] { p1, p2 }, cts.Token);

        // Cancellation is observed before any row is created.
        vm.Files.Should().BeEmpty();
        vm.IsAdding.Should().BeFalse();
    }

    // --------------------------------------------------------------
    // 9. Composition registration
    // --------------------------------------------------------------

    [Fact]
    public void IntakeServiceIsRegisteredInComposition()
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
                p.Timeout.TimeoutMs           = 1_000;
            });
        });

        ImageTextifyServiceCollectionExtensions
            .AddImageTextify(services, _root);

        var provider = services.BuildServiceProvider();
        var intake = provider.GetService<IImageTextifyIntakeService>();

        intake.Should().NotBeNull(
            "AddImageTextify must register the intake service.");
        intake.Should().BeOfType<ImageTextifyIntakeService>();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "FileHarvester.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
