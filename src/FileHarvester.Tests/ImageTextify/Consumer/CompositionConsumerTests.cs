// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/Consumer/CompositionConsumerTests.cs
// PURPOSE:  Consumer-driven tests for the ImageTextify App-facing contract.
//           They prove the navigation surface exists: the enum member, the
//           composition chain, the view-model property, the command, the
//           active-state property, the page type, and the page's binding to
//           the view model's status text.
//           They do NOT construct MainWindowViewModel, because its
//           constructor depends on the application-wide static service
//           provider App.Services. That coupling is deferred to a separate
//           design change (finding ImageTextify-022).
// RELATION: Phase 1.4c exit-criteria test. Companion to
//           Tests/ImageTextify/CompositionTests.cs (Phase 1.2) and
//           Tests/ImageTextify/CoreContractsTests.cs (Phase 1.3).
// ============================================================================

using System.Reflection;
using System.Windows.Input;
using FileHarvester.App.ViewModels;
using FileHarvester.App.Views;
using FluentAssertions;
using ImageTextify.Platform.Composition;
using FileHarvester.Core.Composition;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Intake;
using ImageTextify.Platform.Models;
using ImageTextify.Platform.Validation;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FileHarvester.Tests.ImageTextify;

namespace FileHarvester.Tests.ImageTextify.Consumer;

/// <summary>
/// Consumer-driven tests for the ImageTextify App-facing contract.
/// </summary>
public sealed class CompositionConsumerTests : IDisposable
{
    private readonly string _root;

    public CompositionConsumerTests()
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
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cleanup is best-effort.
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var slnx = Path.Combine(dir, "FileHarvester.slnx");
            if (File.Exists(slnx))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    // -----------------------------------------------------------------
    // 1. AppPage member
    // -----------------------------------------------------------------

    [Fact]
    public void AppPageContainsImageTextifyMember()
    {
        Enum.GetNames<AppPage>().Should().Contain("ImageTextify");
    }

    [Fact]
    public void AppPageImageTextifyIsDistinctFromOtherMembers()
    {
        var values = Enum.GetValues<AppPage>().Cast<int>().ToArray();
        values.Should().OnlyHaveUniqueItems(
            "each AppPage member must have a distinct underlying value.");

        var imageTextifyValue = (int)AppPage.ImageTextify;
        var otherValues = Enum.GetValues<AppPage>()
            .Where(v => v != AppPage.ImageTextify)
            .Select(v => (int)v)
            .ToArray();

        otherValues.Should().NotContain(imageTextifyValue);
    }

    // -----------------------------------------------------------------
    // 2. Composition chain (source-text assertion)
    // -----------------------------------------------------------------

    [Fact]
    public void AppCompositionChainsAddImageTextify()
    {
        var appCs = Path.Combine(
            FindRepoRoot(),
            "src", "FileHarvester.App", "App.axaml.cs");

        File.Exists(appCs).Should().BeTrue(
            "App.axaml.cs must exist at " + appCs);

        var text = File.ReadAllText(appCs);
        text.Should().Contain("services.AddImageTextify(",
            "the App composition root must chain the ImageTextify composition extension.");
    }

    // -----------------------------------------------------------------
    // 3. No premature service registration
    // -----------------------------------------------------------------

    [Fact]
    public void AddImageTextifyRegistersExactlyApprovedServices()
    {
        var services = new ServiceCollection();

        services.AddImageTextify(_root);


        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IImageTextifyFileValidator) &&
            d.ImplementationType == typeof(ImageTextifyFileValidator) &&
            d.Lifetime == ServiceLifetime.Singleton);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IImageTextifyIntakeService) &&
            d.ImplementationType == typeof(ImageTextifyIntakeService) &&
            d.Lifetime == ServiceLifetime.Singleton);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IFileHasher) &&
            d.ImplementationType == typeof(Sha256FileHasher) &&
            d.Lifetime == ServiceLifetime.Singleton);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IModelStore) &&
            d.ImplementationType == null &&
            d.ImplementationFactory != null &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    // -----------------------------------------------------------------
    // 4. MainWindowViewModel - shape only (no construction)
    // -----------------------------------------------------------------

    [Fact]
    public void MainWindowViewModelExposesImageTextifyViewModel()
    {
        var prop = typeof(MainWindowViewModel).GetProperty(
            "ImageTextify",
            BindingFlags.Public | BindingFlags.Instance);

        prop.Should().NotBeNull(
            "MainWindowViewModel must expose a public ImageTextify property.");

        prop!.PropertyType.Should().Be<ImageTextifyViewModel>(
            "the ImageTextify property must be typed as ImageTextifyViewModel.");
    }

    [Fact]
    public void OpenImageTextifyCommandExists()
    {
        var prop = typeof(MainWindowViewModel).GetProperty(
            "OpenImageTextifyCommand",
            BindingFlags.Public | BindingFlags.Instance);

        prop.Should().NotBeNull(
            "MainWindowViewModel must expose a public OpenImageTextifyCommand property.");

        typeof(ICommand).IsAssignableFrom(prop!.PropertyType).Should().BeTrue(
            "OpenImageTextifyCommand must be assignable to ICommand.");
    }

    [Fact]
    public void IsImageTextifyActiveIsBooleanProperty()
    {
        var prop = typeof(MainWindowViewModel).GetProperty(
            "IsImageTextifyActive",
            BindingFlags.Public | BindingFlags.Instance);

        prop.Should().NotBeNull(
            "MainWindowViewModel must expose a public IsImageTextifyActive property.");

        prop!.PropertyType.Should().Be<bool>();
    }

    // -----------------------------------------------------------------
    // 5. ImageTextifyViewModel - direct construction is safe
    // -----------------------------------------------------------------

    [Fact]
    public void ImageTextifyViewModelStatusTextIsNonEmpty()
    {
        var vm = new ImageTextifyViewModel(BuildIntakeService(), new FakeImageTextifySettingsStore(), new FakeImageOcrEngine(), new FakePdfTextExtractor());

        vm.StatusText.Should().NotBeNullOrWhiteSpace(
            "the view model must carry a truthful, non-empty foundation status.");
    }

    // -----------------------------------------------------------------
    // 6. ImageTextifyPage type and its binding
    // -----------------------------------------------------------------

    [Fact]
    public void ImageTextifyPageExists()
    {
        typeof(ImageTextifyPage).Should().NotBeNull();
        typeof(ImageTextifyPage).Namespace.Should().Be("FileHarvester.App.Views");
    }

    [Fact]
    public void ImageTextifyPageBindsStatusText()
    {
        var axaml = Path.Combine(
            FindRepoRoot(),
            "src", "FileHarvester.App", "Views", "ImageTextifyPage.axaml");

        File.Exists(axaml).Should().BeTrue(
            "ImageTextifyPage.axaml must exist at " + axaml);

        var text = File.ReadAllText(axaml);
        text.Should().Contain("{Binding ImageTextify.StatusText}",
            "the page must bind its status TextBlock through MainWindowViewModel.ImageTextify.StatusText.");
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

}
