// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/MainWindowViewModel.cs
// PURPOSE:  The main window's view model. Wires the four panels together.
//           Feeds resolved files into the Output Options panel via the
//           ResolvedFilesPanel.ResolutionCompleted event.
// RELATION: Bound to MainWindow.axaml.
// ============================================================================

using FileHarvester.App.Platform;
using FileHarvester.App.Services;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private string _windowTitle = "File-Ferry";
    private OsKind _osKind = Os.Current;
    private bool _isDarkTheme = true;

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

        InputPanel.FilesChanged += OnInputFilesChanged;

        // Reliable notification that survives in-place row mutations.
        ResolvedFilesPanel.ResolutionCompleted += OnResolutionCompleted;
    }

    public InputPanelViewModel InputPanel { get; }
    public ResolvedFilesPanelViewModel ResolvedFilesPanel { get; }
    public OutputOptionsViewModel OutputOptionsPanel { get; }
    public LogDrawerViewModel LogDrawer { get; }

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
    public string Version => "0.1.0";

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
}
