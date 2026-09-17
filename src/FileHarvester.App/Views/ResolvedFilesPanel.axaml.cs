// ============================================================================
// FILE:     src\FileHarvester.App/Views/ResolvedFilesPanel.axaml.cs
// PURPOSE:  Code-behind for the Resolved Files panel. Wires the double-tap
//           on the ListBox to the view model's RequestAmbiguityResolution,
//           which raises AmbiguityRequested upward to MainWindow.
// RELATION: Bound to Views/ResolvedFilesPanel.axaml. Consumes
//           ResolvedFilesPanelViewModel.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class ResolvedFilesPanel : UserControl
{
    public ResolvedFilesPanel()
    {
        InitializeComponent();

        // Wire the double-tap handler here, in the control that owns the
        // ListBox. FindControl works from this control's own XAML scope.
        var list = this.FindControl<ListBox>("ResolvedFilesList");
        if (list is not null)
        {
            list.DoubleTapped += OnListDoubleTapped;
        }
    }

    private ResolvedFilesPanelViewModel? ViewModel =>
        DataContext as ResolvedFilesPanelViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (list.SelectedItem is not ResolvedFileRowViewModel row) return;

        ViewModel?.RequestAmbiguityResolution(row);
    }
}
