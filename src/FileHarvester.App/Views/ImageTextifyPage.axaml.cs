// ============================================================================
// FILE:     src/FileHarvester.App/Views/ImageTextifyPage.axaml.cs
// PURPOSE:  Code-behind for the ImageTextify page. Wires the file picker
//           and the drop-zone drag/drop events. Both handlers forward plain
//           path strings to the workspace view model; the view model never
//           sees Avalonia types.
// RELATION: Hosted by MainWindow's page host when CurrentPage is
//           AppPage.ImageTextify.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using FileHarvester.App.ViewModels;
using ImageTextify.Core.Domain;

namespace FileHarvester.App.Views;

/// <summary>
/// The ImageTextify workspace page.
/// </summary>
public partial class ImageTextifyPage : UserControl
{
    private static readonly string[] AllFilesPatterns = { "*" };

    private static readonly FilePickerFileType[] AllFilesFilter =
    {
        new FilePickerFileType("All files") { Patterns = AllFilesPatterns },
    };

    /// <summary>
    /// Creates the page. Loads the compiled XAML; does not set a
    /// DataContext, so the window's DataContext flows down.
    /// </summary>
    public ImageTextifyPage()
    {
        InitializeComponent();
        WireAddFilesButton();
        WireDropZone();
        WireCopyButton();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Builds the file-picker filter for the given category. The extension
    /// lists mirror ImageTextifyFileValidator's per-category allowlists;
    /// update both together (finding ITX-PHASE26-06).
    /// </summary>
    private static FilePickerFileType[] BuildFilterForCategory(ImageTextifyCategory category) => category switch
    {
        ImageTextifyCategory.Image => new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp", "*.webp" } } },
        ImageTextifyCategory.Pdf => new[] { new FilePickerFileType("PDFs") { Patterns = new[] { "*.pdf" } } },
        ImageTextifyCategory.Audio => new[] { new FilePickerFileType("Audio") { Patterns = new[] { "*.wav", "*.mp3", "*.m4a", "*.flac", "*.ogg" } } },
        ImageTextifyCategory.Video => new[] { new FilePickerFileType("Video") { Patterns = new[] { "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm" } } },
        _ => AllFilesFilter,
    };

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireAddFilesButton()
    {
        var addButton = this.FindControl<Button>("AddFilesButton");
        if (addButton is not null) addButton.Click += OnAddFilesClicked;
    }

    private void WireDropZone()
    {
        var dropZone = this.FindControl<Border>("DropZone");
        if (dropZone is null) return;

        dropZone.AddHandler(DragDrop.DragOverEvent, OnDropZoneDragOver);
        dropZone.AddHandler(DragDrop.DropEvent, OnDropZoneDrop);
    }

    /// <summary>
    /// Wires the Copy button in the action rail to the clipboard.
    /// </summary>
    private void WireCopyButton()
    {
        var copyButton = this.FindControl<Button>("CopyButton");
        if (copyButton is not null) copyButton.Click += OnCopyClicked;
    }

    /// <summary>
    /// Copies the joined extraction results to the system clipboard.
    /// The view model builds the text; the code-behind performs the
    /// clipboard call, keeping Avalonia types out of the view model.
    /// </summary>
    private async void OnCopyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var viewModel = ViewModel;
        if (topLevel is null || viewModel is null) return;
        var clipboard = topLevel.Clipboard;
        if (clipboard is null) return;
        var text = viewModel.ImageTextify.BuildResultsText();
        if (string.IsNullOrEmpty(text)) return;
        await clipboard.SetTextAsync(text);
    }

    private async void OnAddFilesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || ViewModel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to ImageTextify",
            AllowMultiple = true,
            FileTypeFilter = BuildFilterForCategory(ViewModel.ImageTextify.SelectedCategory),
        });

        if (files.Count == 0) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count == 0) return;

        await ViewModel.ImageTextify.AddFilesAsync(paths, CancellationToken.None).ConfigureAwait(true);
    }

    private void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // TryGetFiles() returns a nullable array. Capture once and null-guard.
        var files = e.DataTransfer.TryGetFiles();
        var hasFiles = ImageTextifyDropHelpers.ExtractLocalPaths(files).Count > 0;

        e.DragEffects = hasFiles
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var viewModel = ViewModel;
        if (viewModel is null) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0) return;

        var paths = ImageTextifyDropHelpers.ExtractLocalPaths(files);

        if (paths.Count == 0) return;

        e.Handled = true;

        await viewModel.ImageTextify.AddFilesAsync(paths, CancellationToken.None).ConfigureAwait(true);
    }
}
