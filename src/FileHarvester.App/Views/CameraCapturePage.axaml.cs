// filepath: src/FileHarvester.App/Views/CameraCapturePage.axaml.cs
// layer:    App View code-behind
// module:   FileHarvester.App.Views
// since:    Phase 9 (Camera Capture)
// purpose:  Code-behind for CameraCapturePage.axaml. Initializes the compiled
//           XAML only. All behavior lives in the MainWindowViewModel camera
//           sub-ViewModel (added in step 9). No event handlers here.
// RELATIONSHIPS
//   Depends on : Avalonia UserControl
//   Used by    : MainWindowViewModel.CurrentPage navigation (AssignPage switch)
//   See also   : FileHarvester.App.Views.CameraCapturePage.axaml
// NOTES
//   - No event handlers here. Commands and bindings live in the ViewModel.
//   - DataContext is inherited from MainWindow; do not set it here.

using Avalonia.Controls;

namespace FileHarvester.App.Views;

/// <summary>
/// Code-behind for <see cref="CameraCapturePage"/>. Initializes the compiled
/// XAML only.
/// </summary>
public partial class CameraCapturePage : UserControl
{
    /// <summary>
    /// Creates the page. Wires no handlers; the compiled XAML wires bindings
    /// against <see cref="MainWindowViewModel"/> (inherited DataContext).
    /// </summary>
    public CameraCapturePage()
    {
        InitializeComponent();
    }
}
