// ============================================================================
// FILE:     src\FileHarvester.App/MainWindow.axaml.cs
// PURPOSE:  Code-behind for the main window. Enables Windows 11 extended
//           client area and registers the interactive regions (theme
//           toggle and navigation buttons) with the OS so their clicks are
//           not swallowed by the drag handler. Hosts the body page
//           through a ContentControl and wires dialogs.
// RELATION: Bound to MainWindow.axaml. Consumes MainWindowViewModel.
// ============================================================================

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FileHarvester.App.ViewModels;
using FileHarvester.App.Views;
using FileHarvester.Core.Domain;

namespace FileHarvester.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Maximized;

        DataContext = new MainWindowViewModel();

        WireTitleBar();
        WirePageHost();
        WireAmbiguity();
        WireHarvest();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // ------------------------------------------------------------------
    // Title bar wiring — no drag handler needed. Windows 11 handles the
    // drag on the reserved non-interactive area between our left content
    // and the caption buttons.
    // ------------------------------------------------------------------

    private void WireTitleBar()
    {
        // Theme toggle.
        var themeToggle = this.FindControl<Button>("ThemeToggleButton");
        if (themeToggle is not null) themeToggle.Click += (_, _) => ToggleTheme();

        // The caption buttons (minimize/maximize/close) are drawn by
        // Windows itself because ExtendClientAreaToDecorationsHint is on.
        // We do not need to wire anything for them.
    }

    private static void ToggleTheme()
    {
        var current = Application.Current?.RequestedThemeVariant;
        var next = current == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = next;
        }
    }

    // ------------------------------------------------------------------
    // Page host. The ContentControl in the body hosts one page at a
    // time. The page is assigned on the CurrentPage change and once at
    // startup. The page inherits the window DataContext, so its bindings
    // resolve against MainWindowViewModel. A new page instance is created
    // on each change; the view models live on MainWindowViewModel and are
    // not recreated, so panel state survives.
    // ------------------------------------------------------------------

    private void WirePageHost()
    {
        if (ViewModel is null) return;

        var host = this.FindControl<ContentControl>("PageHost");
        if (host is null) return;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                AssignPage(host, ViewModel.CurrentPage);
            }
        };

        AssignPage(host, ViewModel.CurrentPage);
    }

    private void AssignPage(ContentControl host, AppPage page)
    {
        host.Content = page switch
        {
            AppPage.Cloud  => new CloudPage(),
            AppPage.LinkJoiner => new LinkJoinerPage(),
            AppPage.Extract => new ExtractPage(),
            AppPage.SchemaLence => new SchemaLencePage(),
            AppPage.ImageTextify => new ImageTextifyPage(),
            AppPage.Prompt => new PromptPage(),
            AppPage.LiveTranscription => new LiveTranscriptionPage(),
            AppPage.CameraCapture => new CameraCapturePage(),
            AppPage.Conversion => new ConversionPage(),
            AppPage.TextToSpeech => new TextToSpeechPage(),
            AppPage.CodeViewer => new CodeViewerPage
            {
                // The page binds directly to CodeViewerViewModel.
                DataContext = ViewModel?.CodeViewer,
            },
            _              => new HarvestPage(),
        };

        if (page == AppPage.Cloud && ViewModel is not null && !ViewModel.Cloud.IsLoaded)
        {
            _ = ViewModel.Cloud.LoadAsync();
        }
        if (page == AppPage.SchemaLence && ViewModel is not null)
        {
            _ = ViewModel.SchemaLence.InitializeAsync();
        }
    }

    // ------------------------------------------------------------------
    // Ambiguity dialog.
    // ------------------------------------------------------------------

    private void WireAmbiguity()
    {
        if (ViewModel is not null)
        {
            ViewModel.ResolvedFilesPanel.AmbiguityRequested += OnAmbiguityRequested;
        }
    }

    private async void OnAmbiguityRequested(object? sender, ResolvedFileRowViewModel row)
    {
        if (ViewModel is null) return;

        var allMatches = row.AllMatches.ToList();
        if (allMatches.Count == 0) return;

        var dialogVm = new AmbiguityDialogViewModel(row.Request.RawInput, allMatches);
        var dialog = new AmbiguityDialog { DataContext = dialogVm };

        var result = await dialog.ShowDialog<IReadOnlyList<ResolvedFile>?>(this);
        if (result is null || result.Count == 0) return;

        var panel = ViewModel.ResolvedFilesPanel;
        var index = panel.Rows.IndexOf(row);
        if (index < 0) return;

        foreach (var picked in result)
        {
            var newRow = new ResolvedFileRowViewModel(row.Request)
            {
                Status = FileStatus.Found,
                Note = $"Chosen from {allMatches.Count} matches",
                Resolved = picked,
            };

            panel.Rows.Insert(index + 1, newRow);
            index++;
        }

        panel.Rows.Remove(row);
        ViewModel.OutputOptionsPanel.SetResolvedFiles(panel.CurrentResolvedFiles);
    }

    // ------------------------------------------------------------------
    // Harvest result dialog.
    // ------------------------------------------------------------------

    private void WireHarvest()
    {
        if (ViewModel is not null)
        {
            ViewModel.OutputOptionsPanel.HarvestCompleted += OnHarvestCompleted;
        }
    }

    private async void OnHarvestCompleted(object? sender, SaveResult result)
    {
        var dialogVm = new HarvestResultDialogViewModel(result);
        var dialog = new HarvestResultDialog { DataContext = dialogVm };
        await dialog.ShowDialog(this);
    }
}
