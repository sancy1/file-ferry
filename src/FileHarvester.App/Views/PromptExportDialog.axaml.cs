// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptExportDialog.axaml.cs
// PURPOSE:  The export dialog's code-behind. Closes on the VM's Saved event
//           (returning the ExportResult) or Cancelled event (returning null).
//           Handles the Browse button by opening the native folder picker and
//           writing the chosen path to the VM's DestinationPath.
// RELATION: Paired with PromptExportDialog.axaml. Opened by
//           PromptPage.axaml.cs when the viewer's Export button is clicked.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>
/// The export dialog.
/// </summary>
public partial class PromptExportDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptExportDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptExportDialogViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, Prompt.Core.Domain.ExportResult result) => Close(result);

    private void OnCancelled(object? sender, EventArgs e) => Close(null);

    private async void OnBrowseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PromptExportDialogViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose destination folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.DestinationPath = path;
        }
    }
}