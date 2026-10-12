using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
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

public partial class SchemaLencePage : UserControl
{
    private static readonly FilePickerFileType[] TextFileFilter =
    {
        new FilePickerFileType("Text and Markdown files")
        {
            Patterns = new[] { "*.txt", "*.md" },
        },
    };
    private static readonly FilePickerFileType[] ExcelFileFilter =
    {
        new FilePickerFileType("Excel workbook")
        {
            Patterns = new[] { "*.xlsx" },
        },
    };
    private static readonly FilePickerFileType[] BackupFileFilter =
    {
        new FilePickerFileType("PostgreSQL custom archive")
        {
            Patterns = new[] { "*.dump" },
        },
    };

    private SchemaLenceViewModel? _observedViewModel;
    private bool? _isStacked;
    private CancellationTokenSource? _backupCancellation;
    private CancellationTokenSource? _exportCancellation;

    public SchemaLencePage()
    {
        InitializeComponent();
        Wire("CopyReportButton", OnCopyReportClicked);
        var objectsList = this.FindControl<ListBox>("DatabaseObjectsList");
        if (objectsList is not null)
        {
            objectsList.SelectionChanged += OnDatabaseObjectSelectionChanged;
        }
        SizeChanged += OnSizeChanged;
    }

    private SchemaLenceViewModel? ViewModel =>
        (DataContext as MainWindowViewModel)?.SchemaLence;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        SetResponsiveLayout(e.NewSize.Width < 1000);

    private void SetResponsiveLayout(bool stacked)
    {
        if (_isStacked == stacked)
        {
            return;
        }
        _isStacked = stacked;

        var workspace = this.FindControl<Grid>("WorkspaceGrid");
        var browser = this.FindControl<Border>("BrowserPane");
        var details = this.FindControl<Border>("DetailsPane");
        var splitter = this.FindControl<GridSplitter>("PaneSplitter");
        if (workspace is null || browser is null || details is null || splitter is null)
        {
            return;
        }

        workspace.ColumnDefinitions.Clear();
        workspace.RowDefinitions.Clear();
        if (stacked)
        {
            workspace.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            workspace.RowDefinitions.Add(new RowDefinition(new GridLength(0.9, GridUnitType.Star)));
            workspace.RowDefinitions.Add(new RowDefinition(new GridLength(6, GridUnitType.Pixel)));
            workspace.RowDefinitions.Add(new RowDefinition(new GridLength(1.1, GridUnitType.Star)));
            Grid.SetColumn(browser, 0);
            Grid.SetRow(browser, 0);
            Grid.SetColumn(splitter, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetColumn(details, 0);
            Grid.SetRow(details, 2);
            splitter.ResizeDirection = GridResizeDirection.Rows;
            splitter.Width = double.NaN;
            splitter.Height = 6;
            splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }
        else
        {
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(410, GridUnitType.Pixel)));
            workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)));
            workspace.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            workspace.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(browser, 0);
            Grid.SetRow(browser, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetRow(splitter, 0);
            Grid.SetColumn(details, 2);
            Grid.SetRow(details, 0);
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
            _observedViewModel.CopyReportRequested -= OnCopyRequested;
            _observedViewModel.SaveReportRequested -= OnSaveRequested;
            _observedViewModel.ExportExcelRequested -= OnExportExcelRequested;
            _observedViewModel.BackupDatabaseRequested -= OnBackupDatabaseRequested;
            _observedViewModel.CancelBackupRequested -= OnCancelBackupRequested;
            _observedViewModel.CancelExcelExportRequested -= OnCancelExcelExportRequested;
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedViewModel = ViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.CopyReportRequested += OnCopyRequested;
            _observedViewModel.SaveReportRequested += OnSaveRequested;
            _observedViewModel.ExportExcelRequested += OnExportExcelRequested;
            _observedViewModel.BackupDatabaseRequested += OnBackupDatabaseRequested;
            _observedViewModel.CancelBackupRequested += OnCancelBackupRequested;
            _observedViewModel.CancelExcelExportRequested += OnCancelExcelExportRequested;
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            RebuildDataGridColumns();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemaLenceViewModel.Page))
        {
            RebuildDataGridColumns();
        }
    }

    private void RebuildDataGridColumns()
    {
        var dataGrid = this.FindControl<DataGrid>("DataGrid");
        if (dataGrid is null)
        {
            return;
        }
        dataGrid.Columns.Clear();
        var columns = _observedViewModel?.Page?.Columns;
        if (columns is null)
        {
            return;
        }

        for (var index = 0; index < columns.Count; index++)
        {
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[index],
                Binding = new Binding($"[{index}]"),
                IsReadOnly = true,
                MinWidth = 110,
                Width = new DataGridLength(180),
            });
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

    private void OnCopyReportClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Structure is null)
        {
            ViewModel?.SetStatus("Select an object before copying a structure report.");
            return;
        }
        ViewModel.CopyReportCommand.Execute(null);
    }

    private void OnDatabaseObjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SchemaLenceObjectRowViewModel selected })
        {
            selected.SelectCommand.Execute(null);
        }
    }

    private async void OnCopyRequested(object? sender, string text)
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
            ViewModel?.SetStatus("Database content copied to clipboard.");
        }
        catch (Exception ex)
        {
            ViewModel?.SetStatus("Copy failed (" + ex.GetType().Name + ").");
        }
    }

    private async void OnSaveRequested(object? sender, string text) =>
        await SaveTextAsync(text, "schema-report.md");

    private void OnCancelBackupRequested(object? sender, EventArgs e) =>
        _backupCancellation?.Cancel();

    private void OnCancelExcelExportRequested(object? sender, EventArgs e) =>
        _exportCancellation?.Cancel();

    private async void OnExportExcelRequested(object? sender, EventArgs e) =>
        await ExportExcelAsync();

    private async void OnBackupDatabaseRequested(object? sender, EventArgs e) =>
        await BackupDatabaseAsync();

    private async Task ExportExcelAsync()
    {
        var viewModel = ViewModel;
        var topLevel = TopLevel.GetTopLevel(this);
        if (viewModel?.Structure is null || topLevel is null)
        {
            viewModel?.SetStatus("Select a table or view before exporting to Excel.");
            return;
        }
        string? temporaryPath = null;
        var progressUpdatesFinished = false;
        try
        {
            var exportRequest = viewModel.CaptureExcelExportRequest();
            var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                WellKnownFolder.Downloads);
            var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export all rows to Excel",
                    SuggestedStartLocation = downloads,
                    SuggestedFileName = SafeFileName(exportRequest.ObjectName) + ".xlsx",
                    DefaultExtension = "xlsx",
                    FileTypeChoices = ExcelFileFilter,
                    ShowOverwritePrompt = true,
                });
            var path = destination?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                viewModel.SetStatus(destination is null
                    ? "Excel export cancelled."
                    : "The selected destination is not a local file path.");
                return;
            }

            var fullPath = EnsureExtension(path, ".xlsx");
            if (!string.Equals(fullPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath))
            {
                viewModel.SetStatus("An Excel workbook already exists at the extension-adjusted destination.");
                return;
            }
            temporaryPath = fullPath + ".file-ferry-" + Guid.NewGuid().ToString("N") + ".part";
            using var cancellation = new CancellationTokenSource();
            _exportCancellation = cancellation;
            viewModel.SetExcelExportRunning(true);
            var resilience = App.Services.GetRequiredService<IResilienceExecutor>();
            var progress = new Progress<long>(count =>
            {
                if (!progressUpdatesFinished)
                {
                    viewModel.SetStatus($"Exporting all rows to Excel… {count:N0} rows written.");
                }
            });
            var exportedRows = await resilience.ExecuteAsync(
                policyName: PolicyNames.BundleWrite,
                operation: async token =>
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                    return await viewModel.ExportSelectedObjectToExcelAsync(
                        exportRequest, temporaryPath, progress, token);
                },
                ct: cancellation.Token);
            File.Move(temporaryPath, fullPath, overwrite: true);
            temporaryPath = null;
            progressUpdatesFinished = true;
            viewModel.SetStatus(exportedRows == 0
                ? $"Excel workbook contains headers but no rows visible to this role. Check table contents and row-level security. Saved to {fullPath}"
                : $"Excel export complete: {exportedRows:N0} rows saved to {fullPath}");
        }
        catch (OperationCanceledException)
        {
            viewModel.SetStatus("Excel export cancelled.");
        }
        catch (ResilienceException exception)
        {
            viewModel.SetStatus("Excel export failed after retries: "
                + SafeFailureMessage(exception));
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Excel export failed: " + SafeFailureMessage(ex));
        }
        finally
        {
            progressUpdatesFinished = true;
            _exportCancellation?.Dispose();
            _exportCancellation = null;
            viewModel?.SetExcelExportRunning(false);
            DeleteTemporaryFile(temporaryPath, viewModel, "Excel export");
        }
    }

    private async Task BackupDatabaseAsync()
    {
        var viewModel = ViewModel;
        var topLevel = TopLevel.GetTopLevel(this);
        if (viewModel?.ActiveConnection is null || topLevel is null)
        {
            viewModel?.SetStatus("Connect to a database before creating a backup.");
            return;
        }
        string? temporaryPath = null;
        try
        {
            var backupRequest = viewModel.CaptureBackupRequest();
            var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                WellKnownFolder.Downloads);
            var suggestedName = SafeFileName(backupRequest.Profile.Name) + ".dump";
            var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save PostgreSQL database backup",
                    SuggestedStartLocation = downloads,
                    SuggestedFileName = suggestedName,
                    DefaultExtension = "dump",
                    FileTypeChoices = BackupFileFilter,
                    ShowOverwritePrompt = true,
                });
            var path = destination?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                viewModel.SetStatus(destination is null
                    ? "Database backup cancelled."
                    : "The selected destination is not a local file path.");
                return;
            }

            var fullPath = EnsureExtension(path, ".dump");
            if (!string.Equals(fullPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath))
            {
                viewModel.SetStatus("A backup already exists at the extension-adjusted destination.");
                return;
            }
            temporaryPath = fullPath + ".file-ferry-" + Guid.NewGuid().ToString("N") + ".part";
            using var cancellation = new CancellationTokenSource();
            _backupCancellation = cancellation;
            viewModel.SetBackupRunning(true);
            viewModel.SetStatus("Creating a restorable PostgreSQL archive. It includes schema and data available to this role, not cluster roles or global settings.");
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            await viewModel.CreateDatabaseBackupAsync(
                backupRequest, temporaryPath, cancellation.Token);
            File.Move(temporaryPath, fullPath, overwrite: true);
            temporaryPath = null;
            try
            {
                await viewModel.RecordBackupAsync(backupRequest, fullPath);
                viewModel.SetStatus("Database backup saved to " + fullPath);
            }
            catch (Exception ex)
            {
                viewModel.SetStatus($"Backup saved to {fullPath}, but local history could not be updated ({ex.GetType().Name}).");
            }
        }
        catch (OperationCanceledException)
        {
            viewModel.SetStatus("Database backup cancelled.");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Database backup failed: " + SafeFailureMessage(ex));
        }
        finally
        {
            _backupCancellation?.Dispose();
            _backupCancellation = null;
            viewModel?.SetBackupRunning(false);
            DeleteTemporaryFile(temporaryPath, viewModel, "Database backup");
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "database-export" : safe;
    }

    private static string EnsureExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path + extension);

    private static string SafeFailureMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return string.IsNullOrWhiteSpace(message)
            ? exception.GetBaseException().GetType().Name
            : message;
    }

    private static void DeleteTemporaryFile(
        string? path,
        SchemaLenceViewModel? viewModel,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            viewModel?.SetStatus($"{operation} temporary file cleanup failed ({ex.GetType().Name}).");
        }
    }

    private async Task SaveTextAsync(string text, string suggestedName)
    {
        var viewModel = ViewModel;
        var topLevel = TopLevel.GetTopLevel(this);
        if (viewModel is null || topLevel is null)
        {
            viewModel?.SetStatus("Save dialog is unavailable.");
            return;
        }

        try
        {
            var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                WellKnownFolder.Downloads);
            var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save database report",
                    SuggestedStartLocation = downloads,
                    SuggestedFileName = suggestedName,
                    DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
                    FileTypeChoices = TextFileFilter,
                    ShowOverwritePrompt = true,
                });
            var path = destination?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                viewModel.SetStatus(destination is null
                    ? "Save cancelled."
                    : "The selected destination is not a local file path.");
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var temporaryPath = fullPath + ".file-ferry-" + Guid.NewGuid().ToString("N") + ".part";
            var resilience = App.Services.GetRequiredService<IResilienceExecutor>();
            try
            {
                await resilience.ExecuteAsync(
                    policyName: PolicyNames.BundleWrite,
                    operation: async token =>
                    {
                        await File.WriteAllTextAsync(
                            temporaryPath, text, new UTF8Encoding(false), token);
                        File.Move(temporaryPath, fullPath, overwrite: true);
                    },
                    ct: CancellationToken.None);
                viewModel.SetStatus("Database report saved to " + fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (ResilienceException)
        {
            viewModel.SetStatus("Saving the database report failed after retries.");
        }
        catch (Exception ex)
        {
            viewModel.SetStatus("Saving the database report failed (" + ex.GetType().Name + ").");
        }
    }
}
