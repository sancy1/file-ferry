// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/MainWindowViewModel.cs
// PURPOSE:  The main window's view model. Wires workspace navigation and panels.
//           Feeds resolved files into the Output Options panel via the
//           ResolvedFilesPanel.ResolutionCompleted event.
// RELATION: Bound to MainWindow.axaml.
// ============================================================================

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
using System.Windows.Input;

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
    }

    public InputPanelViewModel InputPanel { get; }
    public ResolvedFilesPanelViewModel ResolvedFilesPanel { get; }
    public OutputOptionsViewModel OutputOptionsPanel { get; }
    public LogDrawerViewModel LogDrawer { get; }
    public CloudPageViewModel Cloud { get; }
    public LinkJoinerViewModel LinkJoiner { get; }
    public ExtractViewModel Extract { get; }
    public SchemaLenceViewModel SchemaLence { get; }

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
            }
        }
    }


    public bool IsHarvestActive => CurrentPage == AppPage.Harvest;
    public bool IsCloudActive => CurrentPage == AppPage.Cloud;
    public bool IsLinkJoinerActive => CurrentPage == AppPage.LinkJoiner;
    public bool IsExtractActive => CurrentPage == AppPage.Extract;
    public bool IsSchemaLenceActive => CurrentPage == AppPage.SchemaLence;
    public ICommand OpenCloudCommand { get; }
    public ICommand OpenHarvestCommand { get; }
    public ICommand OpenLinkJoinerCommand { get; }
    public ICommand OpenExtractCommand { get; }
    public ICommand OpenSchemaLenceCommand { get; }

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
}
