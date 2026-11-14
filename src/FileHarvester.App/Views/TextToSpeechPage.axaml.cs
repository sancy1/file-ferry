// filepath: src/FileHarvester.App/Views/TextToSpeechPage.axaml.cs
// layer:    App View code-behind
// module:   FileHarvester.App.Views
// since:    Phase 13 (Text-to-Speech)
// purpose:  Code-behind for TextToSpeechPage.axaml. Initializes the compiled
//           XAML only. All behavior lives in the TextToSpeechViewModel
//           (reached via MainWindowViewModel.TextToSpeech).
// RELATIONSHIPS
//   Depends on : Avalonia UserControl
//   Used by    : MainWindowViewModel.CurrentPage navigation
//   See also   : FileHarvester.App.Views.TextToSpeechPage.axaml
// NOTES
//   - No event handlers here. Commands and bindings live in the ViewModel.
//   - DataContext is inherited from MainWindow; do not set it here.

using Avalonia.Controls;

namespace FileHarvester.App.Views;

/// <summary>
/// Code-behind for <see cref="TextToSpeechPage"/>. Initializes the compiled XAML only.
/// </summary>
public partial class TextToSpeechPage : UserControl
{
    /// <summary>Creates the page. Wires no handlers.</summary>
    public TextToSpeechPage()
    {
        InitializeComponent();
    }
}