// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptImportDialog.axaml.cs
// PURPOSE:  The import dialog's code-behind. Closes on the VM's Saved event
//           (returning the ImportResult) or Cancelled event (returning null).
//           Handles the Browse button by opening the native file picker and
//           writing the chosen path to the VM's SourceFilePath.
// RELATION: Paired with PromptImportDialog.axaml. Opened by
//           PromptPage.axaml.cs when the + Import button is clicked.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileHarvester.App.ViewModels;
using Prompt.Core.Domain;

namespace FileHarvester.App.Views;

/// <summary>
/// The import dialog.
/// </summary>
public partial class PromptImportDialog : Window
{
    // CA1861: hoisted from the OpenFilePickerAsync call; the arrays are not
    // mutated by the picker, so they are shared static readonly fields.
    private static readonly string[] SourceFilePatterns = new[] { "*.txt", "*.md", "*.docx", "*.pdf" };
    private static readonly FilePickerFileType[] SourceFileTypes = new[]
    {
        new FilePickerFileType("Prompt sources") { Patterns = SourceFilePatterns },
    };
    /// <summary>Creates the dialog.</summary>
    public PromptImportDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptImportDialogViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, ImportResult result) => Close(result);

    private void OnCancelled(object? sender, EventArgs e) => Close(null);

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PromptImportDialogViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a source file",
            AllowMultiple = false,
            FileTypeFilter = SourceFileTypes,
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.SourceFilePath = path;
        }
    }
}