// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/ConversionViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Sub-ViewModel for the conversion workspace. Holds the selected
//           input path, the target format, the produced output path, and the
//           conversion status. Avalonia-free; file picking is a delegate
//           injected from MainWindowViewModel.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : ImageTextify.Core.Abstractions.IConversionService,
//                IConversionService, ConversionFormat, ConversionRequest,
//                ConversionResult, ConversionPair
//   Used by    : MainWindowViewModel.Conversion; ConversionPage.axaml
//   See also   : CameraCaptureViewModel.cs (the sibling sub-VM this mirrors);
//                ConversionService.cs (the dispatch it calls)
//
// NOTES
//   - The conversion writes to the path the user picks at Convert time.
//     There is no separate Save action: Convert is the save (finding F-035,
//     design correction to Phase 11 plan section 4).
//   - The file picker and the save picker are delegates (same pattern as
//     CameraCaptureViewModel's onSaveFrame) so this VM stays Avalonia-free
//     and unit-testable in future.
//   - State machine: Fresh -> FileSelected (idle) -> Converting -> Result.
//     Reset clears everything. The Back button binds OpenImageTextifyCommand
//     on the parent VM, so this VM has no Back command.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The conversion workspace view model.
/// </summary>
/// <remarks>
/// One instance per application. Convert picks the destination and writes to
/// it; there is no separate Save. The VM owns no file handles and no
/// Avalonia types; pickers are injected delegates.
/// </remarks>
public sealed class ConversionViewModel : ViewModelBase
{
    private readonly IConversionService _converter;
    private readonly Func<CancellationToken, Task<string?>>? _onPickInputFile;
    private readonly Func<string, CancellationToken, Task<string?>>? _onPickOutputFile;

    private string? _inputPath;
    private string? _outputPath;
    private string? _inputTextPreview;
    private string? _outputTextPreview;
    private ConversionFormat? _targetFormat;
    private bool _isConverting;
    private string _statusText = "Choose an input file and a target format.";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".ico", ".gif", ".bmp",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".log", ".xml",
        ".html", ".htm", ".yaml", ".yml", ".ini", ".cfg", ".srt", ".vtt",
    };

    private const int PreviewTextMaxChars = 200_000;

    /// <summary>
    /// Creates the sub-ViewModel.
    /// </summary>
    /// <param name="converter">The conversion service. Not null.</param>
    /// <param name="onPickInputFile">Optional input picker. Returns null on cancel.</param>
    /// <param name="onPickOutputFile">Optional output picker (suggested name, token). Returns null on cancel.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="converter"/> is null.</exception>
    public ConversionViewModel(
        IConversionService converter,
        Func<CancellationToken, Task<string?>>? onPickInputFile = null,
        Func<string, CancellationToken, Task<string?>>? onPickOutputFile = null)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _onPickInputFile = onPickInputFile;
        _onPickOutputFile = onPickOutputFile;

        SelectFileCommand = new AsyncRelayCommand<object>(_ => SelectFileAsync());
        ConvertCommand = new AsyncRelayCommand<object>(_ => ConvertAsync());
        ResetCommand = new RelayCommand(Reset);
    }

    /// <summary>The selected input file path, or null.</summary>
    public string? InputPath
    {
        get => _inputPath;
        private set
        {
            if (SetProperty(ref _inputPath, value))
            {
                OnPropertyChanged(nameof(HasInput));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(InputFileName));
                OnPropertyChanged(nameof(InputExtension));
                OnPropertyChanged(nameof(IsInputImage));
                OnPropertyChanged(nameof(IsInputDocument));
                OnPropertyChanged(nameof(IsInputText));
                OnPropertyChanged(nameof(IsInputBinaryDocument));
                _inputTextPreview = LoadPreviewText(value);
                OnPropertyChanged(nameof(InputTextPreview));
            }
        }
    }

    /// <summary>True when an input file is selected.</summary>
    public bool HasInput => !string.IsNullOrEmpty(InputPath);

    /// <summary>
    /// The target format, or null until the user picks one. The UI's dropdown
    /// is bound to this; the list of choices comes from SupportedPairs.
    /// </summary>
    public ConversionFormat? TargetFormat
    {
        get => _targetFormat;
        set
        {
            if (SetProperty(ref _targetFormat, value))
            {
                OnPropertyChanged(nameof(CanConvert));
            }
        }
    }

    /// <summary>The produced output path after a successful conversion, or null.</summary>
    public string? OutputPath
    {
        get => _outputPath;
        private set
        {
            if (SetProperty(ref _outputPath, value))
            {
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(OutputFileName));
                OnPropertyChanged(nameof(OutputExtension));
                OnPropertyChanged(nameof(IsOutputImage));
                OnPropertyChanged(nameof(IsOutputDocument));
                OnPropertyChanged(nameof(IsOutputText));
                OnPropertyChanged(nameof(IsOutputBinaryDocument));
                _outputTextPreview = LoadPreviewText(value);
                OnPropertyChanged(nameof(OutputTextPreview));
            }
        }
    }

    /// <summary>True after a successful conversion.</summary>
    public bool HasResult => !string.IsNullOrEmpty(OutputPath);

    /// <summary>File name of the selected input, or null.</summary>
    public string? InputFileName => InputPath is null ? null : System.IO.Path.GetFileName(InputPath);

    /// <summary>File name of the produced output, or null.</summary>
    public string? OutputFileName => OutputPath is null ? null : System.IO.Path.GetFileName(OutputPath);

    /// <summary>Lowercase extension (with dot) of the input, or null.</summary>
    public string? InputExtension => InputPath is null ? null : System.IO.Path.GetExtension(InputPath).ToLowerInvariant();

    /// <summary>Lowercase extension (with dot) of the output, or null.</summary>
    public string? OutputExtension => OutputPath is null ? null : System.IO.Path.GetExtension(OutputPath).ToLowerInvariant();

    /// <summary>True when the input is an image the preview can render.</summary>
    public bool IsInputImage => HasInput && ImageExtensions.Contains(InputExtension!);

    /// <summary>True when the output is an image the preview can render.</summary>
    public bool IsOutputImage => HasResult && ImageExtensions.Contains(OutputExtension!);

    /// <summary>True when the input is a non-image file (document preview card).</summary>
    public bool IsInputDocument => HasInput && !IsInputImage;

    /// <summary>True when the output is a non-image file (document preview card).</summary>
    public bool IsOutputDocument => HasResult && !IsOutputImage;

    /// <summary>True when the input is a plain-text document whose content can be previewed.</summary>
    public bool IsInputText => IsInputDocument && TextExtensions.Contains(InputExtension!);

    /// <summary>True when the output is a plain-text document whose content can be previewed.</summary>
    public bool IsOutputText => IsOutputDocument && TextExtensions.Contains(OutputExtension!);

    /// <summary>True when the input is a binary document (icon card, no text preview).</summary>
    public bool IsInputBinaryDocument => IsInputDocument && !IsInputText;

    /// <summary>True when the output is a binary document (icon card, no text preview).</summary>
    public bool IsOutputBinaryDocument => IsOutputDocument && !IsOutputText;

    /// <summary>Plain-text content of the input file (capped), or null.</summary>
    public string? InputTextPreview => _inputTextPreview;

    /// <summary>Plain-text content of the output file (capped), or null.</summary>
    public string? OutputTextPreview => _outputTextPreview;

    private static string? LoadPreviewText(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            var text = System.IO.File.ReadAllText(path);
            return text.Length > PreviewTextMaxChars ? text[..PreviewTextMaxChars] + "\n… (truncated)" : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True while a conversion is running.</summary>
    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            if (SetProperty(ref _isConverting, value))
            {
                OnPropertyChanged(nameof(CanConvert));
            }
        }
    }

    /// <summary>True when an input file and target format are present and no conversion is running.</summary>
    public bool CanConvert => HasInput && TargetFormat.HasValue && !IsConverting;

    /// <summary>The user-facing status line.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Opens the input picker and sets <see cref="InputPath"/>.</summary>
    public ICommand SelectFileCommand { get; }

    /// <summary>Runs the conversion. Writes to the path the user picks.</summary>
    public ICommand ConvertCommand { get; }

    /// <summary>
    /// Every target format the UI can offer. The list is static (the enum);
    /// whether a given pair is supported is decided by the service and
    /// surfaced by its named-reason rejection, not by this list.
    /// </summary>
    public IReadOnlyList<ConversionFormat> SupportedFormats { get; } = new[]
    {
        ConversionFormat.Png, ConversionFormat.Jpg, ConversionFormat.Jpeg,
        ConversionFormat.Webp, ConversionFormat.Ico, ConversionFormat.Gif,
        ConversionFormat.Docx, ConversionFormat.Md, ConversionFormat.Pdf,
        ConversionFormat.Txt, ConversionFormat.Xlsx, ConversionFormat.Json,
        ConversionFormat.Csv,
    };

    /// <summary>Clears the input, target, result, and status.</summary>
    public ICommand ResetCommand { get; }

    /// <summary>
    /// Opens the input file picker (delegate). No-op when no picker is wired.
    /// </summary>
    public async Task SelectFileAsync()
    {
        if (_onPickInputFile is null) { StatusText = "No file picker available."; return; }
        var path = await _onPickInputFile(CancellationToken.None).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        InputPath = path;
        OutputPath = null;
        StatusText = "Selected: " + System.IO.Path.GetFileName(path) + ". Choose a target format, then Convert.";
    }

    /// <summary>
    /// Runs the conversion. If no target or input, does nothing but set a
    /// specific status. Otherwise opens the output picker, then calls the
    /// service. Surfaces failure with the exact reason from the service.
    /// </summary>
    public async Task ConvertAsync()
    {
        if (!HasInput) { StatusText = "Choose an input file first."; return; }
        if (!TargetFormat.HasValue) { StatusText = "Choose a target format first."; return; }
        if (_onPickOutputFile is null) { StatusText = "No save picker available."; return; }

        var ext = ExtensionFor(TargetFormat.Value);
        var suggested = System.IO.Path.GetFileNameWithoutExtension(InputPath!) + "_converted" + ext;
        var dest = await _onPickOutputFile(suggested, CancellationToken.None).ConfigureAwait(true);
        if (string.IsNullOrEmpty(dest)) { StatusText = "Conversion cancelled."; return; }

        // The picker may return a path with no extension or a stale one
        // (user typed it, or accepted a default). Normalize to the target
        // extension so Windows can always open the produced file.
        dest = System.IO.Path.ChangeExtension(dest, ext);

        IsConverting = true;
        StatusText = "Converting...";
        try
        {
            var request = new ConversionRequest(InputPath!, dest, TargetFormat.Value);
            var result = await _converter.ConvertAsync(request, null, CancellationToken.None).ConfigureAwait(true);
            if (result.Success)
            {
                OutputPath = result.OutputPath;
                StatusText = "Converted to " + result.OutputPath;
            }
            else
            {
                OutputPath = null;
                StatusText = result.ErrorMessage ?? "Conversion failed.";
            }
        }
        catch (Exception ex)
        {
            OutputPath = null;
            StatusText = "Conversion failed: " + ex.Message;
        }
        finally
        {
            IsConverting = false;
        }
    }

    /// <summary>File extension (with dot) for each target format.</summary>
    private static string ExtensionFor(ConversionFormat format) => format switch
    {
        ConversionFormat.Png => ".png",
        ConversionFormat.Jpg => ".jpg",
        ConversionFormat.Jpeg => ".jpeg",
        ConversionFormat.Webp => ".webp",
        ConversionFormat.Ico => ".ico",
        ConversionFormat.Gif => ".gif",
        ConversionFormat.Docx => ".docx",
        ConversionFormat.Md => ".md",
        ConversionFormat.Pdf => ".pdf",
        ConversionFormat.Txt => ".txt",
        ConversionFormat.Xlsx => ".xlsx",
        ConversionFormat.Json => ".json",
        ConversionFormat.Csv => ".csv",
        _ => string.Empty,
    };

    /// <summary>Clears the input, target, result, and status.</summary>
    public void Reset()
    {
        InputPath = null;
        TargetFormat = null;
        OutputPath = null;
        StatusText = "Choose an input file and a target format.";
    }
}
