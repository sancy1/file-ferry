// ============================================================================
// FILE:     src\FileHarvester.App/Views/LogDrawer.axaml.cs
// PURPOSE:  Code-behind for the Log drawer. Wires auto-scroll to the bottom
//           when the view model raises AutoScrollToBottomRequested. All
//           other behavior lives in LogDrawerViewModel.
// RELATION: Bound to Views/LogDrawer.axaml. Consumes LogDrawerViewModel.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class LogDrawer : UserControl
{
    private LogDrawerViewModel? _subscribedViewModel;

    public LogDrawer()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from the old view model.
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.AutoScrollToBottomRequested -= OnAutoScrollRequested;
            _subscribedViewModel = null;
        }

        // Subscribe to the new one.
        if (DataContext is LogDrawerViewModel vm)
        {
            vm.AutoScrollToBottomRequested += OnAutoScrollRequested;
            _subscribedViewModel = vm;
        }
    }

    private void OnAutoScrollRequested(object? sender, EventArgs e)
    {
        // Scroll to bottom on the UI thread, after the layout has been
        // updated to include the new row. DispatcherPriority.Background
        // ensures we run after the layout pass.
        Dispatcher.UIThread.Post(
            () =>
            {
                var scroll = this.FindControl<ScrollViewer>("LogScrollViewer");
                scroll?.ScrollToEnd();
            },
            DispatcherPriority.Background);
    }
}
