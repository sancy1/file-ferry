using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FileHarvester.App.ViewModels;
using FileHarvester.Core.Composition;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace FileHarvester.App.Views;

public partial class LinkJoinerPage : UserControl
{
    private static readonly FilePickerFileType[] AllFilesFilter =
    {
        new FilePickerFileType("All files") { Patterns = new[] { "*" } },
    };

    private static readonly FilePickerFileType[] TextFileFilter =
    {
        new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } },
    };

    private LinkJoinerViewModel? _observedViewModel;
    private bool? _isStacked;

    public LinkJoinerPage()
    {
        InitializeComponent();
        Wire("CopySelectedButton", OnCopySelectedClicked);
        SizeChanged += OnSizeChanged;
    }

    private LinkJoinerViewModel? ViewModel =>
        (DataContext as MainWindowViewModel)?.LinkJoiner;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        SetResponsiveLayout(e.NewSize.Width < 860);

    private void SetResponsiveLayout(bool stacked)
    {
        if (_isStacked == stacked)
        {
            return;
        }
        _isStacked = stacked;

        var workspace = this.FindControl<Grid>("WorkspaceGrid");
        var input = this.FindControl<Border>("InputPane");
        var results = this.FindControl<Border>("ResultsPane");
        var splitter = this.FindControl<GridSplitter>("PaneSplitter");
        if (workspace is null || input is null || results is null || splitter is null)
        {
            return;
        }

        workspace.ColumnDefinitions.Clear();
        workspace.RowDefinitions.Clear();
        if (stacked)
        {
            workspace.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            workspace.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            workspace.RowDefinitions.Add(new RowDefinition(new GridLength(6, GridUnitType.Pixel)));
            workspace.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetColumn(input, 0);
            Grid.SetRow(input, 0);
            Grid.SetColumn(splitter, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetColumn(results, 0);
            Grid.SetRow(results, 2);
            splitter.ResizeDirection = GridResizeDirection.Rows;
            splitter.Width = double.NaN;
            splitter.Height = 6;
            splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }
        else
        {
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)));
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)));
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)));
            workspace.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(input, 0);
            Grid.SetRow(input, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetRow(splitter, 0);
            Grid.SetColumn(results, 2);
            Grid.SetRow(results, 0);
            splitter.ResizeDirection = GridResizeDirection.Columns;
            splitter.Width = 6;
            splitter.Height = double.NaN;
            splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_observedViewModel is not null)
        {
            _observedViewModel.BrowseBaseRequested -= OnBrowseBaseRequested;
            _observedViewModel.AddFilesRequested -= OnAddFilesRequested;
            _observedViewModel.CopyRequested -= OnCopyRequested;
            _observedViewModel.SaveRequested -= OnSaveRequested;
        }

        _observedViewModel = ViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.BrowseBaseRequested += OnBrowseBaseRequested;
            _observedViewModel.AddFilesRequested += OnAddFilesRequested;
            _observedViewModel.CopyRequested += OnCopyRequested;
            _observedViewModel.SaveRequested += OnSaveRequested;
        }
    }

    private void Wire(string name, EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
        {
            button.Click += handler;
        }
    }

    private async void OnBrowseBaseRequested(
        object? sender,
        LinkJoinerSectionViewModel section)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            ViewModel?.SetStatus("Folder picker is unavailable.");
            return;
        }

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = $"Choose base location for {section.Name}",
                    AllowMultiple = false,
                });
            if (folders.Count == 0)
            {
                return;
            }

            var path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ViewModel?.SetStatus("The selected folder could not be resolved to a local path.");
                return;
            }

            section.BaseLocation = path;
            ViewModel?.SetStatus($"Base location set for {section.Name}.");
        }
        catch (Exception ex)
        {
            ViewModel?.SetStatus("Folder selection failed (" + ex.GetType().Name + ").");
        }
    }

    private async void OnAddFilesRequested(
        object? sender,
        LinkJoinerSectionViewModel section)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            ViewModel?.SetStatus("File picker is unavailable.");
            return;
        }

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = $"Add absolute file paths to {section.Name}",
                    AllowMultiple = true,
                    FileTypeFilter = AllFilesFilter,
                });
            var paths = files.Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!);
            var selected = paths.ToArray();
            section.AppendAbsolutePaths(selected);
            ViewModel?.SetStatus(selected.Length == 0
                ? "No files selected."
                : $"Added {selected.Length} absolute path(s) to {section.Name}; these pass through without the base.");
        }
        catch (Exception ex)
        {
            ViewModel?.SetStatus("File selection failed (" + ex.GetType().Name + ").");
        }
    }

    private async void OnCopySelectedClicked(object? sender, RoutedEventArgs e)
    {
        var selectedText = this.FindControl<TextBox>("ResultsTextBox")?.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            ViewModel?.SetStatus("Select one or more result lines first.");
            return;
        }
        await CopyToClipboardAsync(selectedText);
    }

    private async void OnCopyRequested(object? sender, string text) =>
        await CopyToClipboardAsync(text);

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ViewModel?.SetStatus("Clipboard is unavailable.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            ViewModel?.SetStatus("Copied to clipboard.");
        }
        catch (Exception ex)
        {
            ViewModel?.SetStatus("Copy failed (" + ex.GetType().Name + ").");
        }
    }

    private async void OnSaveRequested(object? sender, string text) =>
        await SaveResultsAsync(text);

    private async Task SaveResultsAsync(string text)
    {
        var viewModel = ViewModel;
        var topLevel = TopLevel.GetTopLevel(this);
        if (viewModel is null || topLevel is null)
        {
            viewModel?.SetStatus("Save dialog is unavailable.");
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            viewModel.SetStatus("There are no results to save.");
            return;
        }

        try
        {
            var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                WellKnownFolder.Downloads);
            var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save joined paths",
                    SuggestedStartLocation = downloads,
                    SuggestedFileName = "joined-paths.txt",
                    DefaultExtension = "txt",
                    FileTypeChoices = TextFileFilter,
                    ShowOverwritePrompt = true,
                });
            if (destination is null)
            {
                viewModel.SetStatus("Save cancelled.");
                return;
            }

            var path = destination.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                viewModel.SetStatus("The selected destination is not a local file path.");
                return;
            }

            var fullPath = path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".txt";
            if (!string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(fullPath),
                    StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath))
            {
                viewModel.SetStatus(
                    "The .txt destination already exists. Choose a different filename to avoid replacing it without confirmation.");
                return;
            }
            var tempPath = fullPath + ".file-ferry-" + Guid.NewGuid().ToString("N") + ".part";
            var resilience = App.Services.GetRequiredService<IResilienceExecutor>();
            try
            {
                await resilience.ExecuteAsync(
                    policyName: PolicyNames.BundleWrite,
                    operation: async token =>
                    {
                        await File.WriteAllTextAsync(
                            tempPath, text, new UTF8Encoding(false), token);
                        File.Move(tempPath, fullPath, overwrite: true);
                    },
                    ct: CancellationToken.None);
                viewModel.SetStatus("Results saved to " + fullPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (ResilienceException)
        {
            viewModel.SetStatus("Saving results failed after retries.");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Saving results failed (" + ex.GetType().Name + ").");
        }
    }
}
