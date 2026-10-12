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

public partial class ExtractPage : UserControl
{
    private static readonly FilePickerFileType[] TextFileFilter =
    {
        new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } },
    };

    private ExtractViewModel? _observedViewModel;
    private bool? _isStacked;

    public ExtractPage()
    {
        InitializeComponent();
        Wire("CopySelectedButton", OnCopySelectedClicked);
        SizeChanged += OnSizeChanged;
    }

    private ExtractViewModel? ViewModel =>
        (DataContext as MainWindowViewModel)?.Extract;

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
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)));
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
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
            _observedViewModel.PasteRequested -= OnPasteRequested;
            _observedViewModel.CopyRequested -= OnCopyRequested;
            _observedViewModel.SaveRequested -= OnSaveRequested;
        }

        _observedViewModel = ViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PasteRequested += OnPasteRequested;
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

    private async void OnPasteRequested(object? sender, EventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ViewModel?.SetStatus("Clipboard is unavailable.");
            return;
        }

        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (text is null)
            {
                ViewModel?.SetStatus("Clipboard does not contain text.");
                return;
            }
            ViewModel?.SetInputText(text);
        }
        catch (Exception ex)
        {
            ViewModel?.SetStatus("Paste failed (" + ex.GetType().Name + ").");
        }
    }

    private void OnCopySelectedClicked(object? sender, RoutedEventArgs e)
    {
        var selected = this.FindControl<ListBox>("ResultsListBox")?
            .SelectedItems?
            .Cast<ExtractResultItemViewModel>()
            .ToArray();
        ViewModel?.CopySelected(selected ?? Array.Empty<ExtractResultItemViewModel>());
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
            viewModel.SetStatus("There are no matches to save.");
            return;
        }

        try
        {
            var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                WellKnownFolder.Downloads);
            var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save extracted matches",
                    SuggestedStartLocation = downloads,
                    SuggestedFileName = "extracted-matches.txt",
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
                viewModel.SetStatus("Matches saved to " + fullPath);
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
            viewModel.SetStatus("Saving matches failed after retries.");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Saving matches failed (" + ex.GetType().Name + ").");
        }
    }
}
