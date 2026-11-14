// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CodeFileCardViewModel.cs
// PURPOSE:  View model for one source-code card in the Code Content
//           viewer: file name, full path, role, line count, and the
//           read-only code surface (or a no-preview notice for binary /
//           unreadable files). Content loads on demand when the card is
//           realized, never ahead of time.
// RELATION: Produced by ProjectTabViewModel.CodeCards. Bound by
//           CodeFileCardView.axaml. The text is rendered by a read-only
//           AvaloniaEdit surface in the view.
// ============================================================================

using System.Windows.Input;
using CodeViewer.Core.Domain;
using CodeViewer.Platform.FileSystem;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One file card in the Code Content viewer.
/// </summary>
public sealed class CodeFileCardViewModel : ViewModelBase
{
    private readonly ProjectFileReader _reader;
    private readonly Action<string> _copyToClipboard;
    private string _text = string.Empty;
    private int _lineCount;
    private bool _isLoaded;
    private bool _isLoading;
    private string? _noPreviewMessage;

    public CodeFileCardViewModel(
        ProjectNode file,
        ProjectFileReader reader,
        Action<string> copyToClipboard)
    {
        Node = file ?? throw new ArgumentNullException(nameof(file));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _copyToClipboard = copyToClipboard ?? throw new ArgumentNullException(nameof(copyToClipboard));

        // Heading is prepended so a pasted file shows name, path, and role
        // above the body. See the Heading property below.
        CopyCommand = new RelayCommand(
            () => _copyToClipboard(Heading + Environment.NewLine + Text),
            () => HasPreview);
    }

    /// <summary>The underlying file node.</summary>
    public ProjectNode Node { get; }

    /// <summary>The file name shown in the card header.</summary>
    public string FileName => Node.Name;

    /// <summary>The complete path shown under the file name.</summary>
    public string FullPath => Node.FullPath;

    /// <summary>The role label (for example "Python Source").</summary>
    public string Role => FileRoleClassifier.Classify(Node.Name);

    /// <summary>The decoded text. Empty until loaded or when no preview.</summary>
    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    /// <summary>The number of lines in <see cref="Text"/>.</summary>
    public int LineCount
    {
        get => _lineCount;
        private set
        {
            if (SetProperty(ref _lineCount, value))
            {
                OnPropertyChanged(nameof(LineCountLabel));
            }
        }
    }

    /// <summary>The "26 / 26 lines" label from the reference design.</summary>
    public string LineCountLabel => $"{LineCount} / {LineCount} lines";

    /// <summary>
    /// Three-line heading followed by a trailing newline:
    /// file name, full path, and role+line count. Prepended to the
    /// file's body on per-file Copy, and used by Copy All, so a
    /// pasted file shows its identity above its content.
    /// </summary>
    public string Heading =>
        FileName + Environment.NewLine +
        FullPath + Environment.NewLine +
        Role + " " + LineCountLabel + Environment.NewLine;

    /// <summary>True when the file has previewable text.</summary>
    public bool HasPreview => _noPreviewMessage is null;

    /// <summary>The no-preview notice for binary/unreadable files, or null.</summary>
    public string? NoPreviewMessage
    {
        get => _noPreviewMessage;
        private set
        {
            if (SetProperty(ref _noPreviewMessage, value))
            {
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    /// <summary>True while the file content is being read.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Copies this file's text to the clipboard.</summary>
    public ICommand CopyCommand { get; }

    /// <summary>
    /// Loads the file content if it has not been loaded yet. Called by the
    /// view when the card is realized (scrolled into view), so content is
    /// only ever read for cards the user can actually see.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded || _isLoading)
        {
            return;
        }

        _isLoading = true;
        IsLoading = true;
        try
        {
            var result = await _reader.ReadAsync(Node.FullPath, cancellationToken)
                .ConfigureAwait(false);

            if (result.HasPreview)
            {
                Text = result.Text;
                LineCount = result.LineCount;
                NoPreviewMessage = null;
            }
            else
            {
                Text = string.Empty;
                LineCount = 0;
                NoPreviewMessage = result.Reason == NoPreviewReason.Binary
                    ? "No content preview available (binary file)."
                    : $"No content preview available ({result.Error ?? "unreadable"}).";
            }

            _isLoaded = true;
        }
        finally
        {
            _isLoading = false;
            IsLoading = false;
        }
    }
}
