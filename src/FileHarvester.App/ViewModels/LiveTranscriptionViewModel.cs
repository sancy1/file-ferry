// filepath: src/FileHarvester.App/ViewModels/LiveTranscriptionViewModel.cs
// layer:    App ViewModel
// module:   FileHarvester.App.ViewModels
// since:    Phase 10 Block 10.D.2
// purpose:  The live-transcription workspace ViewModel. Owns the session
//           state machine (Idle, Listening, Paused, Stopping, Failed), the
//           Start/Stop/Pause/Resume/Clear commands, the transcript collection,
//           and the Listening indicator. Talks to ILiveTranscriptionService
//           and ILiveTranscriptionSession. Never touches Avalonia types
//           directly; marshals to the UI thread through IUiDispatcher.
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : ILiveTranscriptionService, ILiveTranscriptionSession,
//                IUiDispatcher, LiveTranscriptItemViewModel
//   Used by    : FileHarvester.App.ViewModels.MainWindowViewModel
//   See also   : ImageTextify.Platform.Audio.LiveTranscriptionService,
//                FileHarvester.App.ViewModels.ImageTextifyViewModel
// NOTES
//   - Pause is a ViewModel-held state (ITX-10.D-003). The session has no
//     PauseAsync; the ViewModel sets _isUserPaused and discards incoming
//     events while paused. The session keeps capturing.
//   - Device enumeration is not implemented (ITX-10.D-004). The device
//     ComboBox has a single "Default device" entry; no enumeration service
//     exists yet.
//   - Interim text is deferred (ITX-10.C-013). The OnInterimTextReceived
//     handler exists and is a no-op; when interim lands, it appends an
//     interim row that the next final replaces.
//   - Quality warnings from the session arrive before the final text for
//     the same utterance. They are queued and joined into the final row's
//     WarningsText; the queue is cleared after use.
//   - All session events are marshalled to the UI thread via IUiDispatcher.
//     Tests use InlineUiDispatcher; production uses AvaloniaUiDispatcher.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The live-transcription workspace view model.
/// </summary>
public sealed class LiveTranscriptionViewModel : ViewModelBase, IDisposable
{
    private readonly ILiveTranscriptionService? _service;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILiveRecordingService? _recordingService;

    private ILiveTranscriptionSession? _session;
    private CancellationTokenSource? _sessionCts;
    private bool _isUserPaused;
    private LiveTranscriptionState _state = LiveTranscriptionState.Idle;
    private string _statusText = "Click Start to begin listening.";
    private bool _showPermissionDenied;
    private string _permissionDeniedText = string.Empty;
    private bool _isRecordingAudio;
    private bool _isRecordingVideo;
    private int _selectedModeIndex;
    private int _selectedLanguageIndex;
    private string? _selectedDeviceId;
    private string? _selectedLanguage;
    private readonly System.Collections.Generic.List<string> _pendingWarnings = new();

    /// <summary>
    /// Constructs the view model. A null <paramref name="service"/> means
    /// live transcription is not available on this platform (the
    /// registration is Windows-guarded); the ViewModel reports that state
    /// through <see cref="IsAvailable"/>.
    /// </summary>
    /// <param name="service">
    /// The live transcription service, or null when unavailable.
    /// </param>
    /// <param name="dispatcher">
    /// The UI-thread marshaller. Not null. Use
    /// <see cref="AvaloniaUiDispatcher"/> in production and
    /// <see cref="InlineUiDispatcher"/> in tests.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="dispatcher"/> is null.
    /// </exception>
    public LiveTranscriptionViewModel(ILiveTranscriptionService? service, IUiDispatcher dispatcher, ILiveRecordingService? recordingService = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _service = service;
        _recordingService = recordingService;

        StartCommand = new AsyncRelayCommand<object>(_ => StartAsync());
        StopCommand = new AsyncRelayCommand<object>(_ => StopAsync());
        PauseCommand = new RelayCommand(Pause);
        ResumeCommand = new RelayCommand(Resume);
        ClearCommand = new RelayCommand(() =>
        {
            Transcript.Clear();
            _pendingWarnings.Clear();
            if (IsIdle) { StatusText = "Click Start to begin listening."; }
        });
        ResetCommand = new AsyncRelayCommand<object>(_ => ResetAsync());
    }

    /// <summary>
    /// True when the underlying service is available on this platform.
    /// False when the ViewModel was constructed with a null service; the
    /// view shows a "not available on this platform" message.
    /// </summary>
    public bool IsAvailable => _service is not null;

    /// <summary>The transcript rows, appended per final utterance.</summary>
    public ObservableCollection<LiveTranscriptItemViewModel> Transcript { get; } = new();

    /// <summary>True when at least one transcript row exists.</summary>
    public bool HasTranscript => Transcript.Count > 0;

    /// <summary>The current session state.</summary>
    public LiveTranscriptionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsListening));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    /// <summary>True while the session is listening.</summary>
    public bool IsListening => State == LiveTranscriptionState.Listening;

    /// <summary>True while the session is paused by the user.</summary>
    public bool IsPaused => State == LiveTranscriptionState.Paused;

    /// <summary>True while the session is idle (no session open).</summary>
    public bool IsIdle => State == LiveTranscriptionState.Idle;

    /// <summary>True when the session failed.</summary>
    public bool IsFailed => State == LiveTranscriptionState.Failed;

    /// <summary>The current status line for the View.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>True when microphone permission was denied.</summary>
    public bool ShowPermissionDenied
    {
        get => _showPermissionDenied;
        private set => SetProperty(ref _showPermissionDenied, value);
    }

    /// <summary>The permission-denied explanation, or empty.</summary>
    public string PermissionDeniedText
    {
        get => _permissionDeniedText;
        private set => SetProperty(ref _permissionDeniedText, value);
    }

    /// <summary>Audio recording toggle. Off by default. Phase 10.F wires it.</summary>
    public bool IsRecordingAudio
    {
        get => _isRecordingAudio;
        set
        {
            if (SetProperty(ref _isRecordingAudio, value))
            {
                _ = ApplyRecordingToggleAsync(value);
            }
        }
    }

    /// <summary>Video recording toggle. Off by default. Phase 10.F wires it.</summary>
    public bool IsRecordingVideo
    {
        get => _isRecordingVideo;
        set => SetProperty(ref _isRecordingVideo, value);
    }

    /// <summary>
    /// The mode selector index. 0 = Microphone, 1 = System audio loopback.
    /// </summary>
    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set => SetProperty(ref _selectedModeIndex, value);
    }

    /// <summary>
    /// The selected device id, or null for the system default. The
    /// "loopback:" prefix selects loopback mode; that convention lives
    /// in NAudioAudioCaptureService.
    /// </summary>
    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set => SetProperty(ref _selectedDeviceId, value);
    }

    /// <summary>The language hint, or null for auto-detect.</summary>
    public string? SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    /// <summary>
    /// The language selector index. 0 = Auto-detect, then English, Spanish,
    /// French, German, Portuguese, Italian. Maps to the ISO-639-1 hint in
    /// <see cref="SelectedLanguage"/>.
    /// </summary>
    public int SelectedLanguageIndex
    {
        get => _selectedLanguageIndex;
        set
        {
            if (SetProperty(ref _selectedLanguageIndex, value))
            {
                SelectedLanguage = value >= 0 && value < LanguageCodes.Length
                    ? LanguageCodes[value]
                    : null;
            }
        }
    }

    private static readonly string?[] LanguageCodes = { null, "en", "es", "fr", "de", "pt", "it" };

    /// <summary>Starts a live session.</summary>
    public ICommand StartCommand { get; }

    /// <summary>Stops the live session.</summary>
    public ICommand StopCommand { get; }

    /// <summary>Pauses event delivery without stopping capture.</summary>
    public ICommand PauseCommand { get; }

    /// <summary>Resumes event delivery.</summary>
    public ICommand ResumeCommand { get; }

    /// <summary>Clears the transcript.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>
    /// Stops any active session, clears the transcript, and resets the
    /// input controls to their defaults.
    /// </summary>
    public ICommand ResetCommand { get; }

    /// <summary>
    /// Builds the full transcript as plain text, one row per line, for
    /// copying to the clipboard. The view's code-behind performs the
    /// clipboard call; this method stays Avalonia-free.
    /// </summary>
    /// <returns>The transcript text, or an empty string when there are no rows.</returns>
    public string BuildTranscriptText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in Transcript)
        {
            if (sb.Length > 0) { sb.Append('\n'); }
            sb.Append(row.Text);
        }
        return sb.ToString();
    }

    private async Task StartAsync()
    {
        if (_service is null)
        {
            StatusText = "Live transcription is not available on this platform.";
            return;
        }
        if (_session is not null)
        {
            return;
        }

        _sessionCts?.Dispose();
        _sessionCts = new CancellationTokenSource();
        var ct = _sessionCts.Token;

        try
        {
            var options = new LiveTranscriptionOptions(
                DeviceId: SelectedDeviceId,
                Language: SelectedLanguage,
                EnhancementOptions: null);

            var session = await _service.StartAsync(options, ct).ConfigureAwait(true);
            _session = session;
            _isUserPaused = false;
            session.FinalTextReceived += OnFinalTextReceived;
            session.InterimTextReceived += OnInterimTextReceived;
            session.SegmentReceived += OnSegmentReceived;
            session.QualityWarningReceived += OnQualityWarningReceived;
            session.TranscriptionActivityChanged += OnTranscriptionActivityChanged;
            State = session.State;
            StatusText = "Listening...";
            ShowPermissionDenied = false;
            PermissionDeniedText = string.Empty;
        }
        catch (AudioUnavailableException ex)
        {
            State = LiveTranscriptionState.Failed;
            StatusText = "Microphone unavailable.";
            ShowPermissionDenied = true;
            PermissionDeniedText = ex.Message;
        }
        catch (OperationCanceledException)
        {
            State = LiveTranscriptionState.Idle;
            StatusText = "Start cancelled.";
        }
        catch (Exception ex)
        {
            State = LiveTranscriptionState.Failed;
            StatusText = "Failed: " + ex.Message;
        }
    }

    private async Task StopAsync()
    {
        if (_session is null) { return; }
        State = LiveTranscriptionState.Stopping;
        StatusText = "Stopping...";
        try
        {
            try { _sessionCts?.Cancel(); } catch { }
            await _session.StopAsync(CancellationToken.None).ConfigureAwait(true);
            await _session.DisposeAsync().ConfigureAwait(true);
        }
        catch { }
        if (_recordingService is not null && _recordingService.IsRecording)
        {
            try { await _recordingService.StopAsync(CancellationToken.None).ConfigureAwait(true); } catch { }
        }
        _session = null;
        _sessionCts?.Dispose();
        _sessionCts = null;
        _isUserPaused = false;
        State = LiveTranscriptionState.Idle;
        StatusText = "Click Start to begin listening.";
    }

    private async Task ResetAsync()
    {
        if (_session is not null)
        {
            await StopAsync().ConfigureAwait(true);
        }
        Transcript.Clear();
        _pendingWarnings.Clear();
        SelectedModeIndex = 0;
        SelectedDeviceId = null;
        SelectedLanguageIndex = 0;
        ShowPermissionDenied = false;
        PermissionDeniedText = string.Empty;
        if (IsIdle) { StatusText = "Click Start to begin listening."; }
    }

    private void Pause()
    {
        if (_session is null || State != LiveTranscriptionState.Listening) { return; }
        _isUserPaused = true;
        State = LiveTranscriptionState.Paused;
        StatusText = "Paused.";
    }

    private void Resume()
    {
        if (_session is null || State != LiveTranscriptionState.Paused) { return; }
        _isUserPaused = false;
        State = LiveTranscriptionState.Listening;
        StatusText = "Listening...";
    }

    private void OnFinalTextReceived(object? sender, FinalTextReceivedEventArgs e)
    {
        if (_isUserPaused) { return; }
        var warningsText = string.Join("; ", _pendingWarnings);
        _pendingWarnings.Clear();
        var row = new LiveTranscriptItemViewModel(
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            e.Text,
            isInterim: false,
            warningsText: warningsText);
        _ = _dispatcher.RunAsync(() =>
        {
            Transcript.Add(row);
            OnPropertyChanged(nameof(HasTranscript));
        });
    }

    private void OnInterimTextReceived(object? sender, InterimTextReceivedEventArgs e)
    {
        // Interim is deferred (ITX-10.C-013). No-op.
        _ = e;
    }

    private void OnSegmentReceived(object? sender, SegmentReceivedEventArgs e)
    {
        // FinalTextReceived carries the same text; SegmentReceived is not
        // surfaced as its own row in Phase 10.D.
        _ = e;
    }

    private void OnQualityWarningReceived(object? sender, QualityWarningReceivedEventArgs e)
    {
        if (_isUserPaused) { return; }
        _pendingWarnings.Add(e.Warning);
    }

    private void OnTranscriptionActivityChanged(object? sender, TranscriptionActivityEventArgs e)
    {
        _ = _dispatcher.RunAsync(() =>
        {
            if (e.IsTranscribing)
            {
                StatusText = "Transcribing...";
            }
            else if (IsListening)
            {
                StatusText = "Listening...";
            }
            else if (IsPaused)
            {
                StatusText = "Paused.";
            }
            else
            {
                StatusText = "Click Start to begin listening.";
            }
        });
    }

    /// <summary>
    /// Disposes the live session and its cancellation source.
    /// </summary>
    private async Task ApplyRecordingToggleAsync(bool enabled)
    {
        if (_recordingService is null) { return; }
        try
        {
            if (enabled)
            {
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imagetextify-recordings");
                var name = "recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".wav";
                var options = new RecordingOptions(System.IO.Path.Combine(dir, name), RecordingFormat.Wav);
                await _recordingService.StartAsync(options, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                if (_recordingService.IsRecording)
                {
                    await _recordingService.StopAsync(CancellationToken.None).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "Recording error: " + ex.Message;
        }
    }

    public void Dispose()
    {
        try { _sessionCts?.Cancel(); } catch { }
        try { _sessionCts?.Dispose(); } catch { }
        _sessionCts = null;
        if (_session is not null)
        {
            try { _session.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            _session = null;
        }
        if (_recordingService is not null && _recordingService.IsRecording)
        {
            try { _recordingService.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        }
    }
}