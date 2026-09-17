// ============================================================================
// FILE:     src\FileHarvester.App/Views/AmbiguityDialog.axaml.cs
// PURPOSE:  Code-behind for the ambiguity picker. Wires the preset buttons
//           and closes the window with a result.
// RELATION: Bound to Views/AmbiguityDialog.axaml.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class AmbiguityDialog : Window
{
    public AmbiguityDialog()
    {
        InitializeComponent();
        WireButtons();
    }

    private AmbiguityDialogViewModel? ViewModel =>
        DataContext as AmbiguityDialogViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        Wire("SelectAllButton", (_, _) => ViewModel?.SelectAllCommand.Execute(null));
        Wire("SelectNoneButton", (_, _) => ViewModel?.SelectNoneCommand.Execute(null));
        Wire("SelectNewestButton", (_, _) => ViewModel?.SelectNewestCommand.Execute(null));
        Wire("SelectLargestButton", (_, _) => ViewModel?.SelectLargestCommand.Execute(null));
        Wire("CancelButton", (_, _) => Close(null));
        Wire("ConfirmButton", (_, _) => Close(ViewModel?.SelectedMatches));
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
