// ============================================================================
// FILE:     src/FileHarvester.Tests/Infrastructure/AvaloniaTestFixture.cs
// PURPOSE:  Boots a headless Avalonia application once per test assembly
//           so view models that marshal through Dispatcher.UIThread can be
//           exercised from tests. Without a running Avalonia app,
//           Dispatcher.UIThread.InvokeAsync never completes.
// RELATION: Registered as a collection fixture. Tests that touch
//           Dispatcher.UIThread join the "Avalonia" collection.
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace FileHarvester.Tests.Infrastructure;

/// <summary>
/// Boots headless Avalonia for the test assembly.
/// </summary>
public sealed class AvaloniaTestFixture : IDisposable
{
    public AvaloniaTestFixture()
    {
        AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
    }

    public void Dispose()
    {
        // Nothing to tear down; the headless platform lives for the
        // duration of the test run.
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            // Load the real application styles so DynamicResource brushes
            // (ChromeBrush, SurfaceRaised, etc.) resolve as in production.
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            Styles.Add(new StyleInclude(
                new Uri("avares://FileHarvester.App/Styles/AppStyles.axaml"))
            {
                Source = new Uri("avares://FileHarvester.App/Styles/AppStyles.axaml"),
            });
            RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}

/// <summary>
/// Collection definition so all dispatcher-dependent tests share the one
/// headless Avalonia application.
/// </summary>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix",
    Justification = "xUnit requires the collection-definition suffix 'Collection' by convention.")]
public sealed class AvaloniaCollection : ICollectionFixture<AvaloniaTestFixture>
{
    public const string Name = "Avalonia";
}
