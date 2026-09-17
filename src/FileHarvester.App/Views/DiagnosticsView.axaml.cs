// ============================================================================
// FILE:     src\FileHarvester.App/Views/DiagnosticsView.axaml.cs
// PURPOSE:  Code-behind for the Diagnostics tab. Starts the refresh timer
//           when the view is attached and stops it when detached, so the
//           timer only runs while the tab is visible.
// RELATION: Bound to Views/DiagnosticsView.axaml. Consumes DiagnosticsViewModel.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is DiagnosticsViewModel vm)
            {
                vm.Start();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is DiagnosticsViewModel vm)
            {
                vm.Stop();
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
