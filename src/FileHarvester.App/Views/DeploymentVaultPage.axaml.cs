// ============================================================================
// filepath: src/FileHarvester.App/Views/DeploymentVaultPage.axaml.cs
// layer:    App / Views
// module:   FileHarvester.App.Views
// since:    Phase A (Deployment Vault registry)
// purpose:  Code-behind for the Deployment Vault page. Loads the XAML and
//           hosts three small interaction handlers that cannot be expressed
//           as bindings: DataGrid double-click-to-open and the per-row
//           delete button. Everything else is bound to commands on
//           MainWindowViewModel.DeploymentVault.
//
// RELATIONSHIPS
//   Implements : n/a (Avalonia UserControl)
//   Depends on : Avalonia.Markup.Xaml.AvaloniaXamlLoader,
//                FileHarvester.App.ViewModels.MainWindowViewModel
//   Used by    : MainWindow.axaml.cs AssignPage via AppPage.DeploymentVault
//   See also   : ConversionPage.axaml.cs (the InitializeComponent-only shape)
//
// NOTES
//   - LAYOUT v8: the page is a three-view swap (applications | application
//     detail | record detail). Double-clicking a grid row opens the detail
//     view; the x button on a record row soft-deletes it. Both delegate to
//     commands on DeploymentVaultViewModel — no logic lives here.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>
/// The Deployment Vault page.
/// </summary>
public partial class DeploymentVaultPage : UserControl
{
    /// <summary>Initializes the page.</summary>
    public DeploymentVaultPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Double-click on an application row opens the detail view.</summary>
    private void ApplicationsGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // NOTE: named XAML fields are not wired with the manual
        // AvaloniaXamlLoader.Load pattern (same reason SchemaLencePage uses
        // FindControl). Resolve the grid from the name scope instead.
        var grid = this.FindControl<DataGrid>("ApplicationsGrid");
        if (DataContext is MainWindowViewModel vm
            && grid?.SelectedItem is ApplicationRowViewModel row)
        {
            vm.DeploymentVault.OpenApplicationCommand.Execute(row);
        }
    }

    /// <summary>Double-click on a record row opens the record detail view.</summary>
    private void RecordsGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("RecordsGrid");
        if (DataContext is MainWindowViewModel vm
            && grid?.SelectedItem is InfrastructureRecordRowViewModel row)
        {
            vm.DeploymentVault.OpenRecordCommand.Execute(row);
        }
    }

    /// <summary>The per-row x button soft-deletes that record.</summary>
    private void RecordRowDeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InfrastructureRecordRowViewModel row }
            && DataContext is MainWindowViewModel vm)
        {
            vm.DeploymentVault.DeleteRecordRowCommand.Execute(row);
        }
    }
}
