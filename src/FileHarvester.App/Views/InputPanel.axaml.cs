// ============================================================================
// FILE:     src/FileHarvester.App/Views/InputPanel.axaml.cs
// PURPOSE:  Code-behind for the Input Intake panel. Wires native file and
//           folder pickers and the Parse Paste button. Drag-and-drop is
//           handled at the Window level in MainWindow.axaml.cs; see that
//           file for the drag event handlers.
// RELATION: Bound to Views/InputPanel.axaml. Consumes InputPanelViewModel.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class InputPanel : UserControl
{
    private static readonly string[] AllFilesPatterns = { "*" };

    private static readonly FilePickerFileType[] AllFilesFilter =
    {
        new FilePickerFileType("All files") { Patterns = AllFilesPatterns },
    };

    public InputPanel()
    {
        InitializeComponent();
        WireButtons();
    }

    private InputPanelViewModel? ViewModel => DataContext as InputPanelViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        var parseButton = this.FindControl<Button>("ParsePasteButton");
        if (parseButton is not null) parseButton.Click += OnParsePasteClicked;

        var addFilesButton = this.FindControl<Button>("AddFilesButton");
        if (addFilesButton is not null) addFilesButton.Click += OnAddFilesClicked;

        var addFolderButton = this.FindControl<Button>("AddFolderButton");
        if (addFolderButton is not null) addFolderButton.Click += OnAddFolderClicked;
    }

    private void OnParsePasteClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ParsePasteBox();
    }

    private async void OnAddFilesClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || ViewModel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to File-Ferry",
            AllowMultiple = true,
            FileTypeFilter = AllFilesFilter,
        });

        if (files.Count == 0) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!);

        var added = ViewModel.AddFilesFromPaths(paths);
        ViewModel.StatusText = added > 0
            ? $"Added {added} file(s) from picker."
            : "No new files added (all duplicates).";
    }

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || ViewModel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add all files from a folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var folder = folders[0];
        var folderPath = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            ViewModel.StatusText = "Selected folder could not be resolved.";
            return;
        }

        var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
        var added = ViewModel.AddFilesFromPaths(files);
        ViewModel.StatusText = added > 0
            ? $"Added {added} file(s) from folder."
            : "No new files added (all duplicates).";
    }
}
