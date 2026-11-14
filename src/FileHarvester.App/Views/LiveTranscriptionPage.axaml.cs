// filepath: src/FileHarvester.App/Views/LiveTranscriptionPage.axaml.cs
// layer:    App View code-behind
// module:   FileHarvester.App.Views
// since:    Phase 10 Block 10.D.3
// purpose:  Code-behind for LiveTranscriptionPage.axaml. Initializes the
//           compiled XAML only. All behavior is in LiveTranscriptionViewModel.
// RELATIONSHIPS
//   Depends on : Avalonia UserControl
//   Used by    : MainWindowViewModel.CurrentPage navigation
//   See also   : FileHarvester.App.Views.LiveTranscriptionPage.axaml
// NOTES
//   - No event handlers here. Commands and bindings live in the ViewModel.
//   - DataContext is inherited from MainWindow; do not set it here.

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class LiveTranscriptionPage : UserControl
{
    public LiveTranscriptionPage()
    {
        InitializeComponent();
        WireCopyButton();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Wires the Copy text button in the transcript pane to the clipboard.
    /// </summary>
    private void WireCopyButton()
    {
        var copyButton = this.FindControl<Button>("CopyTranscriptButton");
        if (copyButton is not null) copyButton.Click += OnCopyClicked;
    }

    /// <summary>
    /// Copies the full transcript to the system clipboard. The view model
    /// builds the text; the code-behind performs the clipboard call,
    /// keeping Avalonia types out of the view model.
    /// </summary>
    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var viewModel = ViewModel;
        if (topLevel is null || viewModel is null) return;
        var clipboard = topLevel.Clipboard;
        if (clipboard is null) return;
        var text = viewModel.LiveTranscription.BuildTranscriptText();
        if (string.IsNullOrEmpty(text)) return;
        await clipboard.SetTextAsync(text);
    }
}