// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ImageTextifyViewModel.cs
// PURPOSE:  The ImageTextify workspace view model. Owns the intake queue,
//           the category selector, the empty-results status, and the
//           commands that operate on the queue. It talks only to
//           IImageTextifyIntakeService; it never touches Avalonia types,
//           TopLevel, IStorageProvider, or DragDrop. The picker and drop
//           handlers live in the page code-behind.
// RELATION: Instantiated by MainWindowViewModel with an
//           IImageTextifyIntakeService resolved from App.Services.
//           Reached through the ImageTextify page, which binds through
//           MainWindowViewModel.ImageTextify. The type name is permanent;
//           later phases extend it, they do not replace it.
// ============================================================================

using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;
using ImageTextify.Core.Settings;
using ImageTextify.Platform.Extraction;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The ImageTextify workspace view model.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.5b scope.</b> The view model owns an intake queue of
/// <see cref="ImageTextifyFileRowViewModel"/> instances, a category
/// selector, an add method that validates each candidate through
/// <see cref="IImageTextifyIntakeService"/>, and a per-row remove
/// command. It does not extract, read, hash, or decode file contents.
/// </para>
/// <para>
/// <b>UI-thread safety.</b> <see cref="AddFilesAsync"/> is intended to
/// be invoked from the UI thread. Its awaits resume on the captured
/// synchronization context, so mutations to <see cref="Files"/> occur
/// on the UI thread without an explicit dispatcher call. A caller
/// without a UI context (a test) simply runs synchronously, which is
/// also safe because the collection is only mutated by the caller.
/// </para>
/// <para>
/// <b>Category change.</b> Changing <see cref="SelectedCategory"/> does
/// not revalidate existing rows. New files are validated against the
/// currently selected category. This is deliberate: revalidating old
/// rows would silently change their displayed state.
/// </para>
/// <para>
/// <b>Status text.</b> <see cref="StatusText"/> is preserved from the
/// Phase 1.4 foundation page. It is the right-pane empty-state message
/// until results exist in a later phase.
/// </para>
/// </remarks>
public sealed class ImageTextifyViewModel : ViewModelBase, IDisposable
{
    private readonly IImageTextifyIntakeService _intake;
    private readonly IImageOcrEngine _engine;
    private readonly IPdfTextExtractor _pdfExtractor;

    private ImageTextifyCategory _selectedCategory = ImageTextifyCategory.Image;
    private bool _isAdding;
    private bool _isExtracting;
    private string _extractStatusText = "No extraction has run yet.";
    private readonly IImageTextifySettingsStore _settings;
    private readonly ITranscriptionPipeline? _transcriptionPipeline;
    private CancellationTokenSource? _extractCts;

    /// <summary>
    /// Creates the view model with its intake dependency.
    /// </summary>
    /// <param name="intake">
    /// The intake evaluator. Not null. Resolved from the application
    /// service provider by <c>MainWindowViewModel</c>.
    /// </param>
    /// <param name="pdfTextExtractor">
    /// The PDF text extractor for the Pdf category. Not null. Resolved
    /// from the application service provider by <c>MainWindowViewModel</c>.
    /// Used only when a queued row's category is Pdf.
    /// </param>
    /// <param name="engine">
    /// The OCR engine. Not null. Resolved from the application service
    /// provider by <c>MainWindowViewModel</c>. It is stored and used only
    /// by <see cref="ExtractTextCommand"/>; no other member calls it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="intake"/> is null.
    /// </exception>
    public ImageTextifyViewModel(
        IImageTextifyIntakeService intake,
        IImageTextifySettingsStore settings,
        IImageOcrEngine engine,
        IPdfTextExtractor pdfTextExtractor,
        ITranscriptionPipeline? transcriptionPipeline = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _pdfExtractor = pdfTextExtractor ?? throw new ArgumentNullException(nameof(pdfTextExtractor));
        _transcriptionPipeline = transcriptionPipeline;

        RemoveFileCommand = new RelayCommand<ImageTextifyFileRowViewModel>(RemoveFile);
        ExtractTextCommand = new AsyncRelayCommand<object>(_ => ExtractAllAsync());
        ResetCommand = new RelayCommand(Reset);
        CancelExtractionCommand = new RelayCommand(CancelExtraction);

        // Keep HasResults and HasNoResults in sync with the results collection.
        // The extraction command clears and repopulates Results; without this
        // subscription the page's empty-state toggles would not update.
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
        };
    }

    /// <summary>
    /// The intake queue, in first-seen order. Rows are never removed
    /// without an explicit user action or a category-clear.
    /// </summary>
    public ObservableCollection<ImageTextifyFileRowViewModel> Files { get; } = new();

    /// <summary>
    /// True when the queue holds at least one row. Bound by the page to
    /// switch between the empty-state visual and the file list.
    /// </summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>
    /// True when there are no rows. Bound by the page to show the drop
    /// zone prompt.
    /// </summary>
    public bool IsEmpty => Files.Count == 0;

    /// <summary>
    /// The category new files are validated against. Changing it does
    /// not revalidate existing rows.
    /// </summary>
    public ImageTextifyCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                OnPropertyChanged(nameof(SelectedCategoryIndex));
            }
        }
    }

    /// <summary>
    /// The selected category as an integer, for two-way binding to a
    /// ComboBox's <c>SelectedIndex</c>.
    /// </summary>
    public int SelectedCategoryIndex
    {
        get => (int)_selectedCategory;
        set
        {
            var clamped = value < 0 ? 0 : (value > 3 ? 3 : value);
            SelectedCategory = (ImageTextifyCategory)clamped;
        }
    }

    /// <summary>
    /// True while an add operation is running. Exposed so the page can
    /// disable the Add Files button and show progress.
    /// </summary>
    public bool IsAdding
    {
        get => _isAdding;
        private set => SetProperty(ref _isAdding, value);
    }

    /// <summary>
    /// The truthful foundation status, preserved from Phase 1.4. Shown
    /// in the right pane while there are no results.
    /// </summary>
    /// <summary>
    /// True when the Qwen printed-text fallback is enabled for
    /// low-confidence printed lines. PP-OCR remains the default; the
    /// fallback is opt-in and slower (Qwen inference per line).
    /// Persisted only for the process lifetime (finding ITX-B9-003).
    /// </summary>
    public bool EnableQwenPrintedFallback
    {
        get => _settings.Current.EnableQwenPrintedFallback;
        set
        {
            if (_settings.Current.EnableQwenPrintedFallback == value) return;
            var current = _settings.Current;
            _settings.Replace(new ImageTextifySettings(value, current.LowConfidenceThreshold));
            OnPropertyChanged(nameof(EnableQwenPrintedFallback));
        }
    }

    public string StatusText { get; } =
        "Add files to the queue, then click Extract Text.";

    /// <summary>The per-row remove command, bound by each row.</summary>
    public ICommand RemoveFileCommand { get; }

    /// <summary>
    /// The extraction results, one row per processed queue file. Cleared
    /// at the start of each extraction. Populated only by
    /// <see cref="ExtractTextCommand"/>.
    /// </summary>
    public ObservableCollection<ImageTextifyResultItemViewModel> Results { get; } = new();

    /// <summary>
    /// True when at least one extraction result exists. Bound by the page
    /// to show the results list and hide the empty state.
    /// </summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>
    /// True when there are no extraction results. Bound by the page to show
    /// the empty state. Mirrors the <see cref="HasFiles"/> / <see cref="IsEmpty"/>
    /// pair used for the intake queue.
    /// </summary>
    public bool HasNoResults => Results.Count == 0;

    /// <summary>
    /// True while an extraction is running. Guards against re-entrant
    /// execution and drives the progress UI in a later increment.
    /// </summary>
    public bool IsExtracting
    {
        get => _isExtracting;
        private set => SetProperty(ref _isExtracting, value);
    }

    /// <summary>
    /// The extraction status line. Reflects the last extraction's outcome:
    /// how many of how many accepted files were extracted.
    /// </summary>
    public string ExtractStatusText
    {
        get => _extractStatusText;
        private set => SetProperty(ref _extractStatusText, value);
    }

    /// <summary>
    /// The extraction command. Runs <see cref="ExtractAllAsync"/>; bound
    /// by the page in a later increment. No parameter; iterates every
    /// accepted queue row in order.
    /// </summary>
    public ICommand ExtractTextCommand { get; }

    /// <summary>
    /// Adds each of the supplied paths to the intake queue, validating
    /// each through <see cref="IImageTextifyIntakeService"/>. Every
    /// candidate produces a row, including rejected and duplicate rows.
    /// Nothing is silently discarded.
    /// </summary>
    /// <param name="fullPaths">
    /// The absolute paths to add. Null or empty is a no-op.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the intake operation. On cancellation no partial row is
    /// added.
    /// </param>
    /// <returns>A task that completes when every candidate has a row.</returns>
    public async Task AddFilesAsync(
        IReadOnlyCollection<string>? fullPaths,
        CancellationToken cancellationToken = default)
    {
        if (fullPaths is null || fullPaths.Count == 0) return;

        IsAdding = true;
        try
        {
            foreach (var path in fullPaths)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var existingRaw = Files
                    .Where(r => r.Category == SelectedCategory)
                    .Select(r => r.FullPathForComparison)
                    .ToList();
                var decision = await _intake
                    .EvaluateAsync(path, SelectedCategory, existingRaw, cancellationToken)
                    .ConfigureAwait(true);

                if (cancellationToken.IsCancellationRequested) return;

                Files.Add(new ImageTextifyFileRowViewModel(
                    decision.Validation,
                    decision.Reason,
                    SelectedCategory,
                    RemoveFileCommand));
            }

            NotifyQueueChanged();
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    /// Extracts text from one in-memory frame (camera capture) and adds the result
    /// to <see cref="Results"/>. Same OCR call path as the imported-image branch:
    /// <see cref="IImageOcrEngine.ExtractAsync"/> with <c>Enhance:false</c>, then one
    /// <see cref="ImageTextifyResultItemViewModel"/> row. The caller owns the stream.
    /// </summary>
    /// <param name="frame">The frame stream. Not null; the caller disposes it.</param>
    /// <param name="displayName">Human label for the result row (e.g. device name).</param>
    /// <param name="cancellationToken">Cancels the OCR call.</param>
    public async Task ExtractFromFrameAsync(Stream frame, string displayName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(displayName);
        var result = await _engine
            .ExtractAsync(frame, new ImageOcrOptions(Enhance: false), cancellationToken)
            .ConfigureAwait(true);
        Results.Add(new ImageTextifyResultItemViewModel(
            displayName,
            result.Text,
            string.Join("; ", result.Warnings),
            "Extracted"));
        ExtractStatusText = "Camera frame extracted: " + displayName;
    }

    private async Task ExtractAllAsync()
    {
        if (IsExtracting) return;
        IsExtracting = true;
        try { _extractCts?.Dispose(); } catch { }
        _extractCts = new CancellationTokenSource();
        var extractionToken = _extractCts.Token;
        try
        {
            Results.Clear();
            var accepted = Files.Where(r => r.IsAccepted).ToList();
            if (accepted.Count == 0)
            {
                ExtractStatusText = "No accepted files to extract.";
                return;
            }

            var succeeded = 0;
            foreach (var row in accepted)
            {
                try
                {
                    if (row.Category == ImageTextifyCategory.Audio || row.Category == ImageTextifyCategory.Video)
                    {
                        var avOutcome = await ExtractAudioOrVideoAsync(row, extractionToken).ConfigureAwait(true);
                        if (avOutcome == "Completed") { succeeded++; }
                        if (avOutcome == "Cancelled") { break; }
                        continue;
                    }
                    if (row.Category == ImageTextifyCategory.Pdf)
                    {
                        await using var pdfStream = File.OpenRead(row.FullPathForExtraction);
                        var pdf = await _pdfExtractor
                            .ExtractAsync(pdfStream, new PdfExtractionOptions(), CancellationToken.None)
                            .ConfigureAwait(true);
                        var joined = string.Join(Environment.NewLine, pdf.Pages.Select(p => p.Text));
                        if (string.IsNullOrWhiteSpace(joined))
                        {
                            // Scanned PDF: no text layer. Rasterize each page and OCR it
                            // through the image engine, which handles printed and
                            // handwritten text. A fresh stream per page because the
                            // rasterizer consumes the stream.
                            var pageTexts = new List<string>();
                            for (var pageNumber = 1; pageNumber <= pdf.Pages.Count; pageNumber++)
                            {
                                await using var pageStream = File.OpenRead(row.FullPathForExtraction);
                                var pagePng = PdfPageRasterizer.RenderPageToPng(pageStream, pageNumber, 2.0f);
                                using var pngStream = new MemoryStream(pagePng);
                                var pageResult = await _engine
                                    .ExtractAsync(pngStream, new ImageOcrOptions(Enhance: false), CancellationToken.None)
                                    .ConfigureAwait(true);
                                pageTexts.Add(pageResult.Text);
                            }
                            var scannedText = string.Join(Environment.NewLine, pageTexts.Where(t => !string.IsNullOrWhiteSpace(t)));
                            if (string.IsNullOrWhiteSpace(scannedText))
                            {
                                Results.Add(new ImageTextifyResultItemViewModel(
                                    row.DisplayName, string.Empty, string.Empty,
                                    "Failed: scanned PDF OCR produced no text."));
                                continue;
                            }
                            Results.Add(new ImageTextifyResultItemViewModel(
                                row.DisplayName, scannedText, string.Empty, "Extracted"));
                            succeeded++;
                            continue;
                        }
                        Results.Add(new ImageTextifyResultItemViewModel(
                            row.DisplayName, joined, string.Empty, "Extracted"));
                        succeeded++;
                        continue;
                    }

                    await using var stream = File.OpenRead(row.FullPathForExtraction);
                    var result = await _engine
                        .ExtractAsync(stream, new ImageOcrOptions(Enhance: false), CancellationToken.None)
                        .ConfigureAwait(true);
                    Results.Add(new ImageTextifyResultItemViewModel(
                        row.DisplayName,
                        result.Text,
                        string.Join("; ", result.Warnings),
                        "Extracted"));
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    Results.Add(new ImageTextifyResultItemViewModel(
                        row.DisplayName, string.Empty, string.Empty, "Cancelled"));
                    break;
                }
                catch (Exception ex)
                {
                    Results.Add(new ImageTextifyResultItemViewModel(
                        row.DisplayName, string.Empty, string.Empty, "Failed: " + ex.Message));
                }
            }
            ExtractStatusText = succeeded + " of " + accepted.Count + " extracted.";
        }
        finally
        {
            IsExtracting = false;
        }
    }

    /// <summary>
    /// The reset command. Clears the queue, the results, and the extraction
    /// status. Does not change the selected category or the Qwen setting.
    /// </summary>
    public ICommand ResetCommand { get; }

    /// <summary>
    /// The cancel-extraction command. Cancels a running audio or video
    /// transcription. No-op when no extraction is running or when the
    /// current extraction is image/PDF (those are synchronous and short).
    /// </summary>
    public ICommand CancelExtractionCommand { get; }

    /// <summary>
    /// Clears the queue, results, and extraction status. The selected
    /// category and the Qwen setting are deliberately preserved.
    /// </summary>
    /// <summary>
    /// Builds a plain-text representation of all extraction results for the
    /// clipboard. Each result is its display name, a line break, its extracted
    /// text, and a blank line before the next. Empty results produce an empty
    /// string. No file names are invented and no text is rewritten.
    /// </summary>
    /// <returns>
    /// The clipboard text. Empty when there are no results.
    /// </returns>
    public string BuildResultsText()
    {
        if (Results.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < Results.Count; i++)
        {
            var r = Results[i];
            sb.AppendLine(r.DisplayName);
            sb.AppendLine(r.ExtractedText);
            if (i < Results.Count - 1) sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private void Reset()
    {
        try { _extractCts?.Cancel(); } catch { }
        try { _extractCts?.Dispose(); } catch { }
        _extractCts = null;
        Files.Clear();
        Results.Clear();
        ExtractStatusText = "No extraction has run yet.";
        NotifyQueueChanged();
    }

    private async Task<string> ExtractAudioOrVideoAsync(ImageTextifyFileRowViewModel row, CancellationToken cancellationToken)
    {
        if (_transcriptionPipeline is null)
        {
            Results.Add(new ImageTextifyResultItemViewModel(
                row.DisplayName, string.Empty, string.Empty,
                "Failed: audio and video transcription is not enabled on this build."));
            return "NotAvailable";
        }

        try
        {
            var progress = new Progress<TranscriptionProgress>(p =>
            {
                if (p.TotalSeconds is double total && total > 0)
                {
                    ExtractStatusText = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0:F0} s / {1:F0} s processed", p.ProcessedSeconds, total);
                }
                else
                {
                    ExtractStatusText = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0:F0} s processed", p.ProcessedSeconds);
                }
            });

            var request = new TranscriptionRequest(row.FullPathForExtraction);
            var result = await _transcriptionPipeline
                .RunAsync(request, progress, cancellationToken)
                .ConfigureAwait(true);

            if (result.Outcome == TranscriptionOutcome.Cancelled)
            {
                Results.Add(new ImageTextifyResultItemViewModel(
                    row.DisplayName, string.Empty, string.Empty, "Cancelled"));
                return "Cancelled";
            }
            if (result.Outcome == TranscriptionOutcome.Completed && result.Document is not null)
            {
                var fullText = string.Join(Environment.NewLine,
                    result.Document.Segments.Select(s => s.Text));
                var warnText = string.Join("; ", result.Document.Warnings.Select(w => w.Message));
                Results.Add(new ImageTextifyResultItemViewModel(
                    row.DisplayName, fullText, warnText, "Extracted"));
                return "Completed";
            }
            var failureReason = result.UserMessage ?? result.Outcome.ToString();
            Results.Add(new ImageTextifyResultItemViewModel(
                row.DisplayName, string.Empty, string.Empty, "Failed: " + failureReason));
            return "Failed";
        }
        catch (OperationCanceledException)
        {
            Results.Add(new ImageTextifyResultItemViewModel(
                row.DisplayName, string.Empty, string.Empty, "Cancelled"));
            return "Cancelled";
        }
        catch (Exception ex)
        {
            Results.Add(new ImageTextifyResultItemViewModel(
                row.DisplayName, string.Empty, string.Empty, "Failed: " + ex.Message));
            return "Failed";
        }
    }

    /// <summary>
    /// Disposes the extraction cancellation source. Called when the
    /// view model is torn down. Currently the App lifetime owns the
    /// view model and does not dispose it explicitly; the CTS release
    /// happens at process exit. Recorded as F-AV-072.
    /// </summary>
    public void Dispose()
    {
        try { _extractCts?.Cancel(); } catch { }
        try { _extractCts?.Dispose(); } catch { }
        _extractCts = null;
    }

    private void CancelExtraction()
    {
        try { _extractCts?.Cancel(); } catch { }
    }

    private void RemoveFile(ImageTextifyFileRowViewModel? row)
    {
        if (row is null) return;
        Files.Remove(row);
        NotifyQueueChanged();
    }

    private void NotifyQueueChanged()
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
