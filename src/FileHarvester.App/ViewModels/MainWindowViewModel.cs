// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/MainWindowViewModel.cs
// PURPOSE:  The main window's view model. Wires workspace navigation and panels.
//           Feeds resolved files into the Output Options panel via the
//           ResolvedFilesPanel.ResolutionCompleted event.
// RELATION: Bound to MainWindow.axaml.
// ============================================================================

using System.Threading.Tasks;
using System.Threading;
using FileHarvester.App.Platform;
using FileHarvester.App.Services;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Core.Writers;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Providers;
using SchemaLence.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Prompt.Core.Domain;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Settings;
using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;
using Avalonia.Input.Platform;
using System.Windows.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageTextify.Platform.Imaging;

namespace FileHarvester.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private string _windowTitle = "File-Ferry";
    private OsKind _osKind = Os.Current;
    private bool _isDarkTheme = true;
    private AppPage _currentPage = AppPage.Harvest;

    public MainWindowViewModel()
    {
        InputPanel = new InputPanelViewModel();

        var resolver = App.Services.GetRequiredService<IFileResolver>();
        ResolvedFilesPanel = new ResolvedFilesPanelViewModel(resolver);

        var registry = App.Services.GetRequiredService<IFormatWriterRegistry>();
        var reader = App.Services.GetRequiredService<IContentReader>();
        var composer = App.Services.GetRequiredService<IBundleComposer>();
        var sink = App.Services.GetRequiredService<IOutputSink>();

        var runner = new HarvestJobRunner(reader, composer, sink);
        OutputOptionsPanel = new OutputOptionsViewModel(registry, runner);

        LogDrawer = new LogDrawerViewModel(App.LogSink, App.LogManager);

        var connections = App.Services.GetRequiredService<ICloudConnectionService>();
        var configStore = App.Services.GetRequiredService<IOAuthClientConfigStore>();
        var credentialStore = App.Services.GetRequiredService<ICredentialStore>();
        Cloud = new CloudPageViewModel(connections, configStore, credentialStore);
        LinkJoiner = new LinkJoinerViewModel();
        Extract = new ExtractViewModel();
        SchemaLence = new SchemaLenceViewModel(
            App.Services.GetRequiredService<ISchemaLenceLocalStore>(),
            App.Services.GetRequiredService<ISchemaLenceCredentialStore>(),
            App.Services.GetServices<IDatabaseProvider>(),
            App.Services.GetRequiredService<IPostgresBackupService>());
        ImageTextify = new ImageTextifyViewModel(
            App.Services.GetRequiredService<IImageTextifyIntakeService>(),
            App.Services.GetRequiredService<IImageTextifySettingsStore>(),
            App.Services.GetRequiredService<IImageOcrEngine>(),
            App.Services.GetRequiredService<ImageTextify.Core.Abstractions.IPdfTextExtractor>(),
            App.Services.GetService<ImageTextify.Core.Abstractions.ITranscriptionPipeline>());
        LiveTranscription = new LiveTranscriptionViewModel(
            App.Services.GetService<ImageTextify.Core.Abstractions.ILiveTranscriptionService>(),
            new AvaloniaUiDispatcher(),
            App.Services.GetService<ImageTextify.Core.Abstractions.ILiveRecordingService>());
        Prompt = new PromptPageViewModel(
            App.Services.GetRequiredService<IPromptStore>(),
            App.Services.GetRequiredService<ICategoryStore>(),
            App.Services.GetRequiredService<ISubCategoryStore>());
        var cameraService = App.Services.GetService(typeof(ImageTextify.Core.Abstractions.ICameraCaptureService))
            as ImageTextify.Core.Abstractions.ICameraCaptureService;
        Camera = new CameraCaptureViewModel(
            cameraService!,
            ImageTextify.ExtractFromFrameAsync,
            SaveCameraFrameAsync);

        Conversion = new ConversionViewModel(
            App.Services.GetRequiredService<ImageTextify.Core.Abstractions.IConversionService>(),
            PickConversionInputAsync,
            PickConversionOutputAsync);

        var ttsService = App.Services.GetService(typeof(ImageTextify.Core.Abstractions.ITextToSpeechService))
            as ImageTextify.Core.Abstractions.ITextToSpeechService;
        if (ttsService is not null)
        {
            TextToSpeech = new TextToSpeechViewModel(
                ttsService,
                PickTtsTextFileAsync,
                PickTtsSavePathAsync);

        }
        else
        {
            TextToSpeech = null!;
        }

        Screenshot = new ScreenshotViewModel(
            App.Services.GetRequiredService<ImageTextify.Core.Abstractions.IScreenCaptureService>());
        CodeViewer = new CodeViewerViewModel(
            App.Services.GetRequiredService<ProjectTreeEnumerator>(),
            App.Services.GetRequiredService<ProjectFileReader>(),
            App.Services.GetRequiredService<IWorkspaceStateStore>(),
            CopyToClipboardAsync,
            App.Services.GetRequiredService<IGlobalExclusionStore>(),
            App.Services.GetRequiredService<GlobalExclusionsHolder>());

        InputPanel.FilesChanged += OnInputFilesChanged;
        LinkJoiner.SendToHarvestRequested += (_, text) =>
        {
            InputPanel.PasteText = text;
            InputPanel.StatusText = "Link Joiner results were sent to Harvest input. Parse them when ready.";
            CurrentPage = AppPage.Harvest;
        };
        LinkJoiner.CloseRequested += (_, _) => CurrentPage = AppPage.Harvest;
        Extract.CloseRequested += (_, _) => CurrentPage = AppPage.Harvest;
        SchemaLence.CloseRequested += (_, _) => CurrentPage = AppPage.Harvest;

        // Reliable notification that survives in-place row mutations.
        ResolvedFilesPanel.ResolutionCompleted += OnResolutionCompleted;

        OpenCloudCommand = new RelayCommand(() => CurrentPage = AppPage.Cloud);
        OpenHarvestCommand = new RelayCommand(() => CurrentPage = AppPage.Harvest);
        OpenLinkJoinerCommand = new RelayCommand(() => CurrentPage = AppPage.LinkJoiner);
        OpenExtractCommand = new RelayCommand(() => CurrentPage = AppPage.Extract);
        OpenSchemaLenceCommand = new RelayCommand(() => CurrentPage = AppPage.SchemaLence);
        OpenImageTextifyCommand = new RelayCommand(() => CurrentPage = AppPage.ImageTextify);
        OpenPromptCommand = new RelayCommand(() => CurrentPage = AppPage.Prompt);
        OpenCodeViewerCommand = new RelayCommand(() => CurrentPage = AppPage.CodeViewer);
        OpenLiveTranscriptionCommand = new RelayCommand(() => CurrentPage = AppPage.LiveTranscription);
        OpenCameraCaptureCommand = new AsyncRelayCommand<object>(_ => OpenCameraCapture());
        OpenConversionCommand = new RelayCommand(OpenConversion);
        OpenTextToSpeechCommand = new RelayCommand(OpenTextToSpeech);
    }

    // Clipboard bridge for CodeViewer copy commands. The view models stay
    // free of Avalonia types; the actual clipboard call is resolved here
    // through the active window's TopLevel at invocation time.
    private static async System.Threading.Tasks.Task CopyToClipboardAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        }
    }

    public InputPanelViewModel InputPanel { get; }
    public ResolvedFilesPanelViewModel ResolvedFilesPanel { get; }
    public OutputOptionsViewModel OutputOptionsPanel { get; }
    public LogDrawerViewModel LogDrawer { get; }
    public CloudPageViewModel Cloud { get; }
    public LinkJoinerViewModel LinkJoiner { get; }
    public ExtractViewModel Extract { get; }
    public SchemaLenceViewModel SchemaLence { get; }

    /// <summary>
    /// The ImageTextify extraction workspace view model.
    /// </summary>
    public ImageTextifyViewModel ImageTextify { get; }

    /// <summary>
    /// The live transcription workspace view model. IsAvailable is false
    /// when the platform does not register ILiveTranscriptionService.
    /// </summary>
    public LiveTranscriptionViewModel LiveTranscription { get; }

    /// <summary>
    /// The camera capture workspace view model (Phase 9).
    /// </summary>
    public CameraCaptureViewModel Camera { get; }

    /// <summary>
    /// The conversion workspace view model (Phase 11).
    /// </summary>
    public ConversionViewModel Conversion { get; }

    /// <summary>
    /// The text-to-speech workspace view model (Phase 13). Null when the
    /// platform does not register ITextToSpeechService (non-Windows).
    /// </summary>
    public TextToSpeechViewModel TextToSpeech { get; }

    /// <summary>
    /// The screenshot capture sub-ViewModel (Phase 14).
    /// </summary>
    public ScreenshotViewModel Screenshot { get; }

    /// <summary>
    /// The Prompt App: a local prompt and document manager.
    /// </summary>
    public PromptPageViewModel Prompt { get; }

    /// <summary>
    /// The CodeViewer workspace: local project structure and code inspection.
    /// </summary>
    public CodeViewerViewModel CodeViewer { get; }

    public AppPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsHarvestActive));
                OnPropertyChanged(nameof(IsCloudActive));
                OnPropertyChanged(nameof(IsLinkJoinerActive));
                OnPropertyChanged(nameof(IsExtractActive));
                OnPropertyChanged(nameof(IsSchemaLenceActive));
                OnPropertyChanged(nameof(IsImageTextifyActive));
                OnPropertyChanged(nameof(IsPromptActive));
                OnPropertyChanged(nameof(IsCodeViewerActive));
                OnPropertyChanged(nameof(IsLiveTranscriptionActive));
                OnPropertyChanged(nameof(IsCameraCaptureActive));
                OnPropertyChanged(nameof(IsConversionActive));
                OnPropertyChanged(nameof(IsTextToSpeechActive));
            }
        }
    }


    public bool IsHarvestActive => CurrentPage == AppPage.Harvest;
    public bool IsCloudActive => CurrentPage == AppPage.Cloud;
    public bool IsLinkJoinerActive => CurrentPage == AppPage.LinkJoiner;
    public bool IsExtractActive => CurrentPage == AppPage.Extract;
    public bool IsSchemaLenceActive => CurrentPage == AppPage.SchemaLence;
    public bool IsImageTextifyActive => CurrentPage == AppPage.ImageTextify;
    public bool IsPromptActive => CurrentPage == AppPage.Prompt;
    public bool IsCodeViewerActive => CurrentPage == AppPage.CodeViewer;
    public bool IsLiveTranscriptionActive => CurrentPage == AppPage.LiveTranscription;
    public bool IsCameraCaptureActive => CurrentPage == AppPage.CameraCapture;
    public bool IsConversionActive => CurrentPage == AppPage.Conversion;
    public bool IsTextToSpeechActive => CurrentPage == AppPage.TextToSpeech;
    public ICommand OpenCloudCommand { get; }
    public ICommand OpenHarvestCommand { get; }
    public ICommand OpenLinkJoinerCommand { get; }
    public ICommand OpenExtractCommand { get; }
    public ICommand OpenSchemaLenceCommand { get; }

    /// <summary>
    /// Navigates to the ImageTextify workspace.
    /// </summary>
    public ICommand OpenImageTextifyCommand { get; }

    /// <summary>
    /// Navigates to the Prompt App.
    /// </summary>
    public ICommand OpenPromptCommand { get; }

    /// <summary>
    /// Navigates to the CodeViewer workspace.
    /// </summary>
    public ICommand OpenCodeViewerCommand { get; }

    /// <summary>Navigates to the live transcription workspace.</summary>
    public ICommand OpenLiveTranscriptionCommand { get; }

    /// <summary>Navigates to the camera capture workspace (Phase 9).</summary>
    public ICommand OpenCameraCaptureCommand { get; }

    /// <summary>Navigates to the conversion workspace (Phase 11).</summary>
    public ICommand OpenConversionCommand { get; }

    /// <summary>Opens the screenshot overlay (Phase 14).</summary>
    public ICommand TakeScreenshotCommand => Screenshot.TakeScreenshotCommand;

    /// <summary>Navigates to the text-to-speech workspace (Phase 13).</summary>
    public ICommand OpenTextToSpeechCommand { get; }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public OsKind OsKind
    {
        get => _osKind;
        set => SetProperty(ref _osKind, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => SetProperty(ref _isDarkTheme, value);
    }

#pragma warning disable CA1822

    public Uri? LogoUri => LogoService.CurrentUri;
    public bool HasLogo => LogoService.HasLogo;
    public string AppName => "File-Ferry";
    public string Version => "1.1.0";

#pragma warning restore CA1822

    private void OnInputFilesChanged(object? sender, EventArgs e)
    {
        var requests = InputPanel.Files.ToList();
        ResolvedFilesPanel.SetRequests(requests);
    }

    private void OnResolutionCompleted(object? sender, IReadOnlyList<ResolvedFile> resolvedFiles)
    {
        OutputOptionsPanel.SetResolvedFiles(resolvedFiles);
    }

    private async Task OpenCameraCapture()
    {
        CurrentPage = AppPage.CameraCapture;
        try { await Camera.RefreshDevicesAsync(CancellationToken.None).ConfigureAwait(true); }
        catch { }
        if (Camera.SelectedDevice is not null && !Camera.IsActive)
        {
            try { await Camera.StartAsync(CancellationToken.None).ConfigureAwait(true); }
            catch { }
        }
    }

    /// <summary>
    /// Save-frame delegate for the camera sub-ViewModel: opens a PNG save picker,
    /// writes the frozen frame stream to the chosen path atomically, and returns.
    /// Returns without writing when the user cancels.
    /// </summary>
    /// <param name="stream">The PNG frame bytes. Not null.</param>
    /// <param name="suggestedName">Suggested file name shown by the picker.</param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task SaveCameraFrameAsync(Stream stream, string suggestedName, CancellationToken ct)
    {
        if (stream.CanSeek) { stream.Position = 0; }
        var top = App.MainTopLevel;
        if (top?.StorageProvider is null)
        {
            throw new InvalidOperationException("Save picker is not available.");
        }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save camera frame",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = PngFilter,
        });
        if (file is null)
        {
            throw new InvalidOperationException("Save cancelled.");
        }
        var dest = file.Path.LocalPath;
        await FrameImageWriter.WriteAsync(stream, dest, ct);
    }

    /// <summary>PNG file-type filter used by the camera save picker. Hoisted for CA1861.</summary>
    private static readonly FilePickerFileType[] PngFilter =
        new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } };

    /// <summary>File-type choices for the conversion save picker. Hoisted for CA1861.</summary>
    private static readonly FilePickerFileType[] ConversionFileTypes =
    {
        new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.ico", "*.gif" } },
        new FilePickerFileType("Documents") { Patterns = new[] { "*.docx", "*.md", "*.pdf", "*.txt" } },
        new FilePickerFileType("Data") { Patterns = new[] { "*.xlsx", "*.json", "*.csv" } },
        new FilePickerFileType("All files") { Patterns = new[] { "*.*" } },
    };

    private void OpenConversion()
    {
        CurrentPage = AppPage.Conversion;
    }

    private static async Task<string?> PickConversionInputAsync(CancellationToken ct)
    {
        var top = App.MainTopLevel;
        if (top?.StorageProvider is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose a file to convert",
            AllowMultiple = false,
        });
        if (files is null || files.Count == 0) return null;
        return files[0].Path.LocalPath;
    }

    private static async Task<string?> PickConversionOutputAsync(string suggestedBaseName, CancellationToken ct)
    {
        var top = App.MainTopLevel;
        if (top?.StorageProvider is null) return null;
        var ext = System.IO.Path.GetExtension(suggestedBaseName).TrimStart('.');
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save converted file",
            SuggestedFileName = suggestedBaseName,
            DefaultExtension = string.IsNullOrEmpty(ext) ? null : ext,
            FileTypeChoices = ConversionFileTypes,
        });
        if (file is null) return null;
        return file.Path.LocalPath;
    }

    private void OpenTextToSpeech()
    {
        CurrentPage = AppPage.TextToSpeech;
    }

    private static async Task<string?> PickTtsTextFileAsync(CancellationToken ct)
    {
        var top = App.MainTopLevel;
        if (top?.StorageProvider is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose a .txt file to speak",
            AllowMultiple = false,
            FileTypeFilter = TtsTextFileFilter,
        });
        if (files is null || files.Count == 0) return null;
        return files[0].Path.LocalPath;
    }

    private static async Task<string?> PickTtsSavePathAsync(string suggestedBaseName, CancellationToken ct)
    {
        var top = App.MainTopLevel;
        if (top?.StorageProvider is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save synthesized WAV",
            SuggestedFileName = suggestedBaseName + ".wav",
            DefaultExtension = "wav",
            FileTypeChoices = TtsWavFileTypeChoices,
        });
        if (file is null) return null;
        return file.Path.LocalPath;
    }

    /// <summary>File-type filter for the TTS .txt picker. Hoisted for CA1861.</summary>
    private static readonly Avalonia.Platform.Storage.FilePickerFileType[] TtsTextFileFilter =
        new[] { new Avalonia.Platform.Storage.FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } } };

    /// <summary>File-type choices for the TTS WAV save picker. Hoisted for CA1861.</summary>
    private static readonly Avalonia.Platform.Storage.FilePickerFileType[] TtsWavFileTypeChoices =
        new[] { new Avalonia.Platform.Storage.FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } } };
}
