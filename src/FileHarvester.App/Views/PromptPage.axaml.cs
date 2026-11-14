// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptPage.axaml.cs
// PURPOSE:  The Prompt App's page code-behind. InitializeComponent, plus the
//           CopyRequested (clipboard), NewPromptRequested and EditRequested
//           (editor dialog) handlers.
// RELATION: Paired with PromptPage.axaml. DataContext comes from the shell's
//           ContentControl; this code-behind sets nothing.
// NOTES:    - The CopyRequested handler uses Avalonia.Input.Platform's
//             SetTextAsync extension via TopLevel.Clipboard.
//           - The NewPromptRequested and EditRequested handlers open the
//             PromptEditorDialog. The stores are resolved from App.Services.
//           - On save, the list reloads; the viewer selects the saved prompt.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Input.Platform;
using FileHarvester.App.ViewModels;
using FileHarvester.Core.Writers;
using Microsoft.Extensions.DependencyInjection;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.Views;

/// <summary>
/// The Prompt App's page view. The two-pane layout; the copy bridge and the
/// editor dialog are the code-behind interactions.
/// </summary>
public partial class PromptPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public PromptPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        main.Prompt.Viewer.CopyRequested += OnCopyRequested;
        main.Prompt.NewPromptRequested += OnNewPromptRequested;
        main.Prompt.ImportRequested += OnImportRequested;
        main.Prompt.TrashRequested += OnTrashRequested;
        main.Prompt.Viewer.EditRequested += OnEditRequested;
        main.Prompt.Viewer.ExportRequested += OnExportRequested;
        main.Prompt.Viewer.HistoryRequested += OnHistoryRequested;
        main.Prompt.ManageCategoriesDialogRequested += OnManageCategoriesDialogRequested;
        main.Prompt.List.BulkExportRequested += OnBulkExportRequested;
        main.Prompt.List.BulkDeleteRequested += OnBulkDeleteRequested;
        main.Prompt.List.BulkMoveRequested += OnBulkMoveRequested;
        main.Prompt.List.BulkAddTagRequested += OnBulkAddTagRequested;
        main.Prompt.FocusSearchRequested += OnFocusSearchRequested;
        main.Prompt.Viewer.ShortcutsRequested += OnShortcutsRequested;

    }

    private async void OnCopyRequested(object? sender, string body)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null) return;
        await top.Clipboard.SetTextAsync(body);
    }

    private async void OnNewPromptRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        await OpenEditorAsync(main, existing: null);
    }

    private async void OnEditRequested(object? sender, PromptRecord existing)
    {
        if (DataContext is not MainWindowViewModel main) return;
        await OpenEditorAsync(main, existing);
    }

    private async Task OpenEditorAsync(MainWindowViewModel main, PromptRecord? existing)
    {
        var services = App.Services;
        var prompts = services.GetRequiredService<IPromptStore>();
        var categories = services.GetRequiredService<ICategoryStore>();
        var subCategories = services.GetRequiredService<ISubCategoryStore>();
        var vm = new PromptEditorViewModel(prompts, categories, subCategories, existing);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptEditorDialog { DataContext = vm };
        var result = await dialog.ShowDialog<PromptRecord?>(owner);
        if (result is null) return;
        await main.Prompt.List.LoadAsync();
        main.Prompt.Viewer.SetPrompt(result, main.Prompt.List);
    }
    private async void OnManageCategoriesDialogRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var categories = services.GetRequiredService<ICategoryStore>();
        var subCategories = services.GetRequiredService<ISubCategoryStore>();
        var vm = new CategoryManagerViewModel(categories, subCategories);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new CategoryManagerDialog { DataContext = vm };
        var changed = false;
        vm.Changed += (_, _) => changed = true;
        await dialog.ShowDialog(owner);
        if (changed)
        {
            await main.Prompt.List.LoadAsync();
        }
    }

    private async void OnExportRequested(object? sender, PromptRecord prompt)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var exporter = services.GetRequiredService<IPromptExporter>();
        var registry = services.GetRequiredService<IFormatWriterRegistry>();
        var vm = new PromptExportDialogViewModel(exporter, registry, prompt);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptExportDialog { DataContext = vm };
        var result = await dialog.ShowDialog<ExportResult?>(owner);
        if (result is { Success: true })
        {
            main.Prompt.List.StatusText = "Exported to " + result.OutputPath;
        }
    }

    private async void OnImportRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var importer = services.GetRequiredService<IPromptImporter>();
        var categories = services.GetRequiredService<ICategoryStore>();
        var subCategories = services.GetRequiredService<ISubCategoryStore>();
        var vm = new PromptImportDialogViewModel(importer, categories, subCategories);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptImportDialog { DataContext = vm };
        var result = await dialog.ShowDialog<ImportResult?>(owner);
        if (result is { Success: true })
        {
            await main.Prompt.List.LoadAsync();
            main.Prompt.List.StatusText = "Imported: " + (result.Prompt?.Title ?? "(unknown)");
        }
    }

    private async void OnTrashRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var store = services.GetRequiredService<IPromptStore>();
        var vm = new PromptTrashDialogViewModel(store);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptTrashDialog { DataContext = vm };
        var changed = await dialog.ShowDialog<bool>(owner);
        if (changed)
        {
            await main.Prompt.List.LoadAsync();
        }
    }

    private async void OnHistoryRequested(object? sender, PromptRecord prompt)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var versions = services.GetRequiredService<IPromptVersionStore>();
        var vm = new PromptHistoryDialogViewModel(versions, prompt);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptHistoryDialog { DataContext = vm };
        var changed = await dialog.ShowDialog<bool>(owner);
        if (changed)
        {
            await main.Prompt.List.LoadAsync();
            var refreshed = await services.GetRequiredService<IPromptStore>().GetAsync(prompt.Id);
            if (refreshed is not null)
            {
                main.Prompt.Viewer.SetPrompt(refreshed, main.Prompt.List);
            }
        }
    }

    private async void OnBulkExportRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var exporter = services.GetRequiredService<IPromptExporter>();
        var registry = services.GetRequiredService<IFormatWriterRegistry>();
        var prompts = main.Prompt.List.SelectedPrompts.Select(r => r.Prompt).ToList();
        if (prompts.Count == 0) return;
        var vm = new PromptExportDialogViewModel(exporter, registry, prompts);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptExportDialog { DataContext = vm };
        var result = await dialog.ShowDialog<ExportResult?>(owner);
        if (result is { Success: true })
        {
            await main.Prompt.List.LoadAsync();
            main.Prompt.List.StatusText = "Exported " + prompts.Count + " prompt(s) to " + result.OutputPath;
        }
    }

    private async void OnBulkDeleteRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var count = main.Prompt.List.SelectedPrompts.Count;
        if (count == 0) return;
        var confirm = new PromptConfirmDialog
        {
            DataContext = new PromptConfirmDialogViewModel(
                "Delete " + count + " prompt(s)?",
                "The selected prompts go to the trash. You can restore them from there."),
        };
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var yes = await confirm.ShowDialog<bool>(owner);
        if (yes)
        {
            await main.Prompt.List.BulkDeleteAsync();
        }
    }

    private async void OnBulkMoveRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var services = App.Services;
        var categories = services.GetRequiredService<ICategoryStore>();
        var subCategories = services.GetRequiredService<ISubCategoryStore>();
        var vm = new PromptCategoryPickerViewModel(categories, subCategories);
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptCategoryPickerDialog { DataContext = vm };
        var result = await dialog.ShowDialog<PromptCategoryPickerResult?>(owner);
        if (result is not null)
        {
            await main.Prompt.List.BulkMoveAsync(result.CategoryId, result.SubCategoryId);
        }
    }

    private async void OnBulkAddTagRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel main) return;
        var vm = new PromptTagPromptViewModel();
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptTagPromptDialog { DataContext = vm };
        var tag = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            await main.Prompt.List.BulkAddTagAsync(tag);
        }
    }

    private void OnFocusSearchRequested(object? sender, EventArgs e)
    {
        if (this.FindControl<TextBox>("SearchTextBox") is { } box)
        {
            box.Focus();
        }
    }

    private async void OnShortcutsRequested(object? sender, EventArgs e)
    {
        var vm = new PromptShortcutsDialogViewModel();
        var owner = TopLevel.GetTopLevel(this) as Window
            ?? throw new InvalidOperationException("No owner window.");
        var dialog = new PromptShortcutsDialog { DataContext = vm };
        await dialog.ShowDialog(owner);
    }
}
