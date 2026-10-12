// ============================================================================
// FILE:     src\FileHarvester.App/Views/OverwriteConfirmDialog.axaml.cs
// PURPOSE:  Code-behind for the overwrite confirmation. Wires the buttons
//           by name and closes the window with the chosen result. The
//           window owns completion; the view model only carries state.
// RELATION: Bound to Views/OverwriteConfirmDialog.axaml.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class OverwriteConfirmDialog : Window
{
    public OverwriteConfirmDialog()
    {
        InitializeComponent();
        WireButtons();
    }

    private OverwriteConfirmDialogViewModel? ViewModel =>
        DataContext as OverwriteConfirmDialogViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        Wire("CancelButton", (_, _) => Close(OverwriteConfirmDialogViewModel.CancelResult()));
        Wire("ReplaceButton", (_, _) => Close(ViewModel?.ReplaceResult()));
    }

    private void Wire(string name, EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
        {
            button.Click += handler;
        }
    }
}
