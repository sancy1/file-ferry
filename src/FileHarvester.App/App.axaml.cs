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
using Avalonia.Controls;
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
using ImageTextify.Platform.Composition;
using CodeViewer.Platform.Composition;
using Prompt.Platform.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    public static OsKind OsKind => Os.Current;
    public static ObservableLogSink LogSink { get; private set; } = null!;
    public static LogFileManager LogManager { get; private set; } = null!;

    /// <summary>
    /// The application top level, captured once after the main window is
    /// attached. Used by file pickers (camera save) so they resolve to a
    /// live visual tree root. Null before the first window is attached.
    /// </summary>
    public static TopLevel? MainTopLevel { get; private set; }

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
            App.MainTopLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            desktop.ShutdownRequested += OnShutdownRequested;
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

        services.AddPromptFeature(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Prompt",
                "prompt.db"));

        services.AddFileHarvester(
            new ResilienceConfig(
                LogDirectory: logDirectory,
                EnableConsoleSink: true,
                EnableFileSink: true),
            extraSink: logSink,
            configureImageTextifyPolicies: ImageTextifyComposition.RegisterResiliencePolicies,
            configureCodeViewerPolicies: CodeViewerComposition.RegisterResiliencePolicies);

        services.AddFileHarvesterCloudCredentials();
        services.AddFileHarvesterCloudDispatch();
        services.AddFileHarvesterCloudOAuth();
        services.AddFileHarvesterCloudDiscovery();
        services.AddSchemaLence(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        // FFmpeg bin directory: enabled when the pinned build is present
        // under the app's base directory as ffmpeg/ (the release bundling
        // step places ffmpeg.exe and ffprobe.exe there). When the binaries
        // are absent, the Audio and Video categories, live transcription,
        // and text-to-speech are unavailable; a startup log event makes the
        // cause visible instead of the vague "not enabled" message.
        var ffmpegBin = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (!File.Exists(Path.Combine(ffmpegBin, "ffmpeg.exe"))
            || !File.Exists(Path.Combine(ffmpegBin, "ffprobe.exe")))
        {
            var checkedPath = ffmpegBin;
            ffmpegBin = null!;
            try
            {
                logSink.Emit(new Portfolio.Resilience.Events.ResilienceEvent
                {
                    EventType = Portfolio.Resilience.Events.ResilienceEventType.CallFailed,
                    PolicyName = "App.Startup",
                    TimestampUtc = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["operation"] = "ffmpeg.probe",
                        ["message"] = "FFmpeg not found next to the app "
                            + "(" + checkedPath + "). Audio/video transcription, "
                            + "live transcription, and text-to-speech are disabled. "
                            + "Re-extract the full folder: ffmpeg.exe and ffprobe.exe "
                            + "must sit beside the app exe.",
                    },
                });
            }
            catch { }
        }
        services.AddImageTextify(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ffmpegBinDirectory: ffmpegBin);
        services.AddCodeViewer(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            logSink);

        services.AddPromptStores(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Prompt",
                "prompt.db"));

        var provider = services.BuildServiceProvider();

        // Register the built-in providers the app can resolve: S3, Azure
        // Blob, GCS, Google Drive, OneDrive, and Dropbox. A provider with
        // no stored credential is registered but resolves lazily on first
        // use, so the registration is safe at startup.
        BuiltInProviderRegistration.RegisterBuiltInProviders(provider);

        return provider;
    }

    // Phase 9 (camera capture): dispose the camera sub-ViewModel on shutdown so
    // no camera session survives the process. Scope Part 4 requires this.
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var vm = desktop.MainWindow?.DataContext as FileHarvester.App.ViewModels.MainWindowViewModel;
        if (vm?.Camera is null) return;
        try { vm.Camera.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { vm.TextToSpeech?.Dispose(); } catch { }
    }
}
