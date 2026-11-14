// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/TextToSpeechViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase 13 (Text-to-Speech)
// purpose:  Sub-ViewModel for the text-to-speech workspace. Holds the text to
//           speak, the synthesized file path, and the speak/pause/resume/stop/
//           synthesize state machine. Avalonia-free; file pickers are
//           injected delegates (same pattern as ConversionViewModel).
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : ImageTextify.Core.Abstractions.ITextToSpeechService
//   Used by    : MainWindowViewModel.TextToSpeech; TextToSpeechPage.axaml
//   See also   : ConversionViewModel.cs, CameraCaptureViewModel.cs (siblings);
//                SystemSpeechTextToSpeechService.cs (the implementation)
//
// NOTES
//   - Synthesize and Save are the same action: Synthesize opens the save
//     picker, then writes to the picked path. There is no second Save
//     (finding F-045; same design correction as the conversion phase F-035).
//   - The VM does not poll IsSpeaking. IsSpeaking flips true when SpeakAsync
//     starts and false when it completes. The service handles the audio-side
//     Pause/Resume state internally; buttons are enabled based on the VM's
//     own flags. Background polling is out of scope (plan section 6).
//   - Text input is the primary source; LoadFileCommand fills it from a .txt
//     picked by the injected delegate. Both paths produce the same TextInput
//     state, which is what the plan section 1 calls out ("paste or upload").
//   - Empty/whitespace text disables Speak and Synthesize; the status line
//     says "Enter some text first." when a command is invoked anyway.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The text-to-speech workspace view model.
/// </summary>
public sealed class TextToSpeechViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Word separators used to derive a suggested filename from the text.
    /// Hoisted for CA1861 (constant array argument on a repeated call).
    /// </summary>
    private static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r' };
    private readonly ITextToSpeechService _tts;
    private readonly Func<CancellationToken, Task<string?>>? _onPickTextFile;
    private readonly Func<string, CancellationToken, Task<string?>>? _onPickSavePath;

    private string _textInput = string.Empty;
    private string? _lastSavedPath;
    private bool _isSpeaking;
    private bool _isSynthesizing;
    private string _statusText = "Paste text or load a .txt file, then choose an action.";

    /// <summary>Creates the sub-ViewModel.</summary>
    /// <param name="tts">The text-to-speech service. Not null.</param>
    /// <param name="onPickTextFile">Optional .txt picker. Returns null on cancel.</param>
    /// <param name="onPickSavePath">Optional save picker. Receives suggested base name; returns null on cancel.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="tts"/> is null.</exception>
    public TextToSpeechViewModel(
        ITextToSpeechService tts,
        Func<CancellationToken, Task<string?>>? onPickTextFile = null,
        Func<string, CancellationToken, Task<string?>>? onPickSavePath = null)
    {
        _tts = tts ?? throw new ArgumentNullException(nameof(tts));
        _onPickTextFile = onPickTextFile;
        _onPickSavePath = onPickSavePath;

        LoadFileCommand = new AsyncRelayCommand<object>(_ => LoadFileAsync());
        SpeakCommand = new AsyncRelayCommand<object>(_ => SpeakAsync());
        PauseCommand = new RelayCommand(Pause);
        ResumeCommand = new RelayCommand(Resume);
        StopCommand = new RelayCommand(Stop);
        SynthesizeCommand = new AsyncRelayCommand<object>(_ => SynthesizeAsync());
        ResetCommand = new RelayCommand(Reset);
    }

    /// <summary>The text to speak. Two-way bound to the page's TextBox.</summary>
    public string TextInput
    {
        get => _textInput;
        set
        {
            if (SetProperty(ref _textInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasText));
                OnPropertyChanged(nameof(CanSpeak));
                OnPropertyChanged(nameof(CanSynthesize));
            }
        }
    }

    /// <summary>True when there is non-whitespace text.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(TextInput);

    /// <summary>True while live speech is active in this session.</summary>
    public bool IsSpeaking
    {
        get => _isSpeaking;
        private set
        {
            if (SetProperty(ref _isSpeaking, value))
            {
                OnPropertyChanged(nameof(CanSpeak));
                OnPropertyChanged(nameof(CanPauseResumeStop));
            }
        }
    }

    /// <summary>True while synthesizing to a file.</summary>
    public bool IsSynthesizing
    {
        get => _isSynthesizing;
        private set
        {
            if (SetProperty(ref _isSynthesizing, value))
            {
                OnPropertyChanged(nameof(CanSpeak));
                OnPropertyChanged(nameof(CanSynthesize));
            }
        }
    }

    /// <summary>True when the Speak button should be enabled.</summary>
    public bool CanSpeak => HasText && !IsSpeaking && !IsSynthesizing;

    /// <summary>True when the Pause/Resume/Stop buttons should be enabled.</summary>
    public bool CanPauseResumeStop => IsSpeaking;

    /// <summary>True when the Synthesize button should be enabled.</summary>
    public bool CanSynthesize => HasText && !IsSpeaking && !IsSynthesizing;

    /// <summary>The path of the last successfully synthesized file, or null.</summary>
    public string? LastSavedPath
    {
        get => _lastSavedPath;
        private set
        {
            if (SetProperty(ref _lastSavedPath, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    /// <summary>True after a successful synthesis.</summary>
    public bool HasResult => !string.IsNullOrEmpty(LastSavedPath);

    /// <summary>The user-facing status line.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Opens a .txt picker and loads the file into <see cref="TextInput"/>.</summary>
    public ICommand LoadFileCommand { get; }

    /// <summary>Speaks <see cref="TextInput"/> aloud.</summary>
    public ICommand SpeakCommand { get; }

    /// <summary>Pauses live playback.</summary>
    public ICommand PauseCommand { get; }

    /// <summary>Resumes live playback.</summary>
    public ICommand ResumeCommand { get; }

    /// <summary>Stops live playback.</summary>
    public ICommand StopCommand { get; }

    /// <summary>Opens a save picker and writes <see cref="TextInput"/> as WAV to the picked path.</summary>
    public ICommand SynthesizeCommand { get; }

    /// <summary>Clears text, result, and status.</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Loads a .txt file via the injected picker.</summary>
    public async Task LoadFileAsync()
    {
        if (_onPickTextFile is null) { StatusText = "No file picker available."; return; }
        try
        {
            var path = await _onPickTextFile(CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrEmpty(path)) return;
            var text = await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(true);
            TextInput = text;
            LastSavedPath = null;
            StatusText = "Loaded " + System.IO.Path.GetFileName(path) + " (" + text.Length + " chars).";
        }
        catch (Exception ex)
        {
            StatusText = "Load failed: " + ex.Message;
        }
    }

    /// <summary>Speaks the text aloud via the service.</summary>
    public async Task SpeakAsync()
    {
        if (!HasText) { StatusText = "Enter some text first."; return; }
        if (IsSpeaking) return;
        IsSpeaking = true;
        StatusText = "Speaking...";
        try
        {
            await _tts.SpeakAsync(TextInput, CancellationToken.None).ConfigureAwait(true);
            StatusText = "Finished speaking.";
        }
        catch (Exception ex)
        {
            StatusText = "Speak failed: " + ex.Message;
        }
        finally
        {
            IsSpeaking = false;
        }
    }

    private void Pause()
    {
        _tts.Pause();
        StatusText = "Paused.";
    }

    private void Resume()
    {
        _tts.ResumeSpeaking();
        StatusText = "Resumed.";
    }

    private void Stop()
    {
        _tts.StopSpeaking();
        IsSpeaking = false;
        StatusText = "Stopped.";
    }

    /// <summary>Synthesizes the text to a WAV file at a user-picked path.</summary>
    public async Task SynthesizeAsync()
    {
        if (!HasText) { StatusText = "Enter some text first."; return; }
        if (_onPickSavePath is null) { StatusText = "No save picker available."; return; }

        var firstWord = TextInput.Trim().Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var baseName = firstWord.Length > 0 ? firstWord[0] : "speech";
        if (baseName.Length > 32) baseName = baseName.Substring(0, 32);

        var dest = await _onPickSavePath(baseName, CancellationToken.None).ConfigureAwait(true);
        if (string.IsNullOrEmpty(dest)) { StatusText = "Synthesis cancelled."; return; }

        IsSynthesizing = true;
        StatusText = "Synthesizing...";
        try
        {
            var produced = await _tts.SynthesizeToFileAsync(TextInput, dest, CancellationToken.None).ConfigureAwait(true);
            LastSavedPath = produced;
            StatusText = "Synthesized to " + produced;
        }
        catch (Exception ex)
        {
            LastSavedPath = null;
            StatusText = "Synthesis failed: " + ex.Message;
        }
        finally
        {
            IsSynthesizing = false;
        }
    }

    /// <summary>Clears text, result, and status.</summary>
    public void Reset()
    {
        TextInput = string.Empty;
        LastSavedPath = null;
        StatusText = "Paste text or load a .txt file, then choose an action.";
    }

    /// <summary>
    /// Disposes the underlying TTS service if it implements IDisposable.
    /// The interface does not require IDisposable; the concrete SystemSpeech
    /// service does, so this releases the SpeechSynthesizer / audio device.
    /// </summary>
    public void Dispose()
    {
        if (_tts is IDisposable d) { try { d.Dispose(); } catch { } }
    }
}
