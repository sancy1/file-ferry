// ============================================================================
// FILE:     src/FileHarvester.App/Views/CodeViewerPage.axaml.cs
// PURPOSE:  Code-behind for the CodeViewer workspace page. Owns the
//           folder picker (the established convention: view models never
//           touch Avalonia storage types), the clipboard, the project
//           menu, and the display-mode pane visibility.
// RELATION: Bound to MainWindowViewModel; the CodeViewer view model is
//           reached through MainWindowViewModel.CodeViewer.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CodeViewer.Core.Domain;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class CodeViewerPage : UserControl
{
    public CodeViewerPage()
    {
        InitializeComponent();
        AddHandler(LoadedEvent, OnLoaded);
    }

    private CodeViewerViewModel? ViewModel => DataContext as CodeViewerViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var addButton = this.FindControl<Button>("AddProjectButton");
        if (addButton is not null)
        {
            addButton.Click += OnAddProjectClicked;
        }

        var menuButton = this.FindControl<Button>("ProjectMenuButton");
        if (menuButton is not null)
        {
            menuButton.Click += OnProjectMenuClicked;
        }

        var exclusionsButton = this.FindControl<Button>("ManageExclusionsButton");
        if (exclusionsButton is not null)
        {
            exclusionsButton.Click += OnManageExclusionsClicked;
        }

        var modeSelector = this.FindControl<ComboBox>("DisplayModeSelector");
        if (modeSelector is not null)
        {
            modeSelector.SelectionChanged += OnDisplayModeChanged;
        }
    }

    // ------------------------------------------------------------------
    // Folder picker (view-model stays free of Avalonia storage types)
    // ------------------------------------------------------------------

    private async void OnAddProjectClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || ViewModel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open a project folder",
                AllowMultiple = false,
            });

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            ViewModel.OpenProject(path);
        }
    }

    // ------------------------------------------------------------------
    // Project menu (Refresh, Show system files, Close Project)
    // ------------------------------------------------------------------

    private void OnProjectMenuClicked(object? sender, RoutedEventArgs e)
    {
        var tab = ViewModel?.ActiveTab;
        if (tab is null || sender is not Button button)
        {
            return;
        }

        var menu = new ContextMenu();

        var refresh = new MenuItem { Header = "Refresh Project" };
        refresh.Click += (_, _) => tab.RefreshCommand.Execute(null);
        menu.Items.Add(refresh);

        var showSystem = new MenuItem
        {
            Header = tab.ShowSystemFiles ? "Hide system files" : "Show system files",
        };
        showSystem.Click += (_, _) => ToggleShowSystemFiles(tab);
        menu.Items.Add(showSystem);

        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = "Close Project" };
        close.Click += (_, _) => ViewModel?.CloseTab(tab);
        menu.Items.Add(close);

        menu.Open(button);
    }

    private static void ToggleShowSystemFiles(ProjectTabViewModel tab)
    {
        tab.SetShowSystemFiles(!tab.ShowSystemFiles);
    }

    // ------------------------------------------------------------------
    // Display mode -> pane visibility
    // ------------------------------------------------------------------

    private async void OnManageExclusionsClicked(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) { return; }
        if (TopLevel.GetTopLevel(this) is not Window owner) { return; }

        var dialogVm = new ManageExclusionsViewModel(vm.CurrentExclusions);
        var dialog = new ManageExclusionsDialog { DataContext = dialogVm };

        // ShowDialog returns GlobalExclusions on Save & Apply, or null on Cancel.
        var result = await dialog.ShowDialog<GlobalExclusions?>(owner);
        if (result is not null)
        {
            await vm.ApplyExclusionsAsync(result);
        }
    }

    private void OnDisplayModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || ViewModel?.ActiveTab is null)
        {
            return;
        }

        var mode = combo.SelectedIndex switch
        {
            0 => DisplayMode.StructureOnly,
            1 => DisplayMode.CodeOnly,
            _ => DisplayMode.StructureAndCode,
        };

        ApplyDisplayMode(mode);
    }

    private void ApplyDisplayMode(DisplayMode mode)
    {
        var structurePane = this.FindControl<Border>("StructurePane");
        var codePane = this.FindControl<Border>("CodePane");
        var splitter = this.FindControl<GridSplitter>("StructureCodeSplitter");
        var workspace = this.FindControl<Grid>("RightWorkspace");
        if (structurePane is null || codePane is null || splitter is null || workspace is null)
        {
            return;
        }

        switch (mode)
        {
            case DisplayMode.StructureOnly:
                structurePane.IsVisible = true;
                codePane.IsVisible = false;
                splitter.IsVisible = false;
                workspace.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                workspace.RowDefinitions[1].Height = new GridLength(0);
                workspace.RowDefinitions[2].Height = new GridLength(0);
                break;

            case DisplayMode.CodeOnly:
                structurePane.IsVisible = false;
                codePane.IsVisible = true;
                splitter.IsVisible = false;
                workspace.RowDefinitions[0].Height = new GridLength(0);
                workspace.RowDefinitions[1].Height = new GridLength(0);
                workspace.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
                break;

            default: // StructureAndCode
                structurePane.IsVisible = true;
                codePane.IsVisible = true;
                splitter.IsVisible = true;
                workspace.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                workspace.RowDefinitions[1].Height = GridLength.Auto;
                workspace.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
                break;
        }
    }
}
