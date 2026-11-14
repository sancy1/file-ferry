// ============================================================================
// FILE:     src/FileHarvester.App/Views/ManageExclusionsDialog.axaml.cs
// PURPOSE:  Code-behind for the Manage Exclusions dialog. Owns the folder
//           and file pickers (view models never touch Avalonia storage
//           types), and the ShowDialog<GlobalExclusions?> contract.
// RELATION: Opened by CodeViewerPage.OnManageExclusionsClicked via ShowDialog.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CodeViewer.Core.Domain;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class ManageExclusionsDialog : Window
{
    public ManageExclusionsDialog()
    {
        InitializeComponent();
        AddHandler(LoadedEvent, OnLoaded);
    }

    private ManageExclusionsViewModel? ViewModel => DataContext as ManageExclusionsViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var addFolders = this.FindControl<Button>("AddFoldersButton");
        if (addFolders is not null) { addFolders.Click += OnAddFoldersClicked; }

        var addFiles = this.FindControl<Button>("AddFilesButton");
        if (addFiles is not null) { addFiles.Click += OnAddFilesClicked; }

        var cancel = this.FindControl<Button>("CancelButton");
        if (cancel is not null)
        {
            cancel.Click += (_, _) => Close(null);
        }

        var save = this.FindControl<Button>("SaveButton");
        if (save is not null)
        {
            save.Click += (_, _) =>
            {
                var vm = ViewModel;
                if (vm is null) { Close(null); return; }
                Close(vm.ToGlobalExclusions());
            };
        }
    }

    private async void OnAddFoldersClicked(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) { return; }
        var topLevel = GetTopLevel(this);
        if (topLevel is null) { return; }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Add folder(s) to exclusions",
                AllowMultiple = true,
            });

        foreach (var folder in folders)
        {
            var path = folder.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) { continue; }
            var name = Path.GetFileName(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(name)) { vm.TryAddFromPicker(name, ExclusionKind.Folder); }
        }
    }

    private async void OnAddFilesClicked(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) { return; }
        var topLevel = GetTopLevel(this);
        if (topLevel is null) { return; }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Add file(s) to exclusions",
                AllowMultiple = true,
            });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) { continue; }
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(name)) { vm.TryAddFromPicker(name, ExclusionKind.File); }
        }
    }
}