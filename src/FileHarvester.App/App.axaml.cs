// ============================================================================
// FILE:     src\FileHarvester.App/App.axaml.cs
// PURPOSE:  Application bootstrap. Creates the ObservableLogSink and the
//           LogFileManager, processes any pending clear request, rotates
//           and prunes old log files, builds the DI container, registers
//           the built-in cloud providers, and creates the main window.
// RELATION: Loaded by Avalonia at startup. Depends on
//           FileHarvester.Core.Composition, FileHarvester.App.Services,
//           and FileHarvester.Platform.Cloud.Registry.
// ============================================================================

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FileHarvester.App.Platform;
using FileHarvester.App.Services;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.Dispatch;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Discovery;
using FileHarvester.Platform.Cloud.Registry;
using SchemaLence.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    public static OsKind OsKind => Os.Current;
    public static ObservableLogSink LogSink { get; private set; } = null!;
    public static LogFileManager LogManager { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Dark;

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileHarvester",
            "logs");

        LogManager = new LogFileManager(logDirectory);

        // Order matters:
        //   1. Process a pending clear request from the last session
        //      (before the library opens the log file for writing).
        //   2. Rotate the log if it is over 5 MB.
        //   3. Prune archives older than 7 days.
        LogManager.ProcessPendingClear();
        LogManager.RotateIfNeeded();
        LogManager.PruneOldArchives();

        LogSink = new ObservableLogSink();
        Services = BuildServices(LogSink, logDirectory);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices(ObservableLogSink logSink, string logDirectory)
    {
        var services = new ServiceCollection();

        services.AddSingleton(logSink);
        services.AddSingleton(App.LogManager);

        // The App-side overwrite confirmer. Registered before
        // AddFileHarvester so it overrides the fail-closed Core default,
        // which is registered with TryAddSingleton inside AddFileHarvester.
        services.AddSingleton<IOverwriteConfirmer, AvaloniaOverwriteConfirmer>();

        services.AddFileHarvester(
            new ResilienceConfig(
                LogDirectory: logDirectory,
                EnableConsoleSink: true,
                EnableFileSink: true),
            extraSink: logSink);

        services.AddFileHarvesterCloudCredentials();
        services.AddFileHarvesterCloudDispatch();
        services.AddFileHarvesterCloudOAuth();
        services.AddFileHarvesterCloudDiscovery();
        services.AddSchemaLence(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        var provider = services.BuildServiceProvider();

        // Register the built-in providers the app can resolve: S3, Azure
        // Blob, GCS, Google Drive, OneDrive, and Dropbox. A provider with
        // no stored credential is registered but resolves lazily on first
        // use, so the registration is safe at startup.
        BuiltInProviderRegistration.RegisterBuiltInProviders(provider);

        return provider;
    }
}
