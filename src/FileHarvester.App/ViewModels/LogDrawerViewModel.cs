// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/LogDrawerViewModel.cs
// PURPOSE:  View model for the Log drawer overlay. Holds the live event
//           collection AND the Diagnostics view model (as a second tab).
//           The Clear command truncates the file on next startup via the
//           marker file pattern.
// RELATION: Consumed by Views/LogDrawer.axaml. Depends on ObservableLogSink
//           and LogFileManager from App.Services.
// ============================================================================

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FileHarvester.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.App.ViewModels;

public sealed class LogDrawerViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableLogSink _sink;
    private readonly LogFileManager _logManager;

    private bool _isOpen;
    private int _eventCount;
    private int _selectedTabIndex;
    private bool _disposed;

    public LogDrawerViewModel(ObservableLogSink sink, LogFileManager logManager)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

        // Diagnostics tab: resolved from DI.
        var latency = App.Services.GetRequiredService<ILatencyTracker>();
        var circuit = App.Services.GetRequiredService<ICircuitBreakerMonitor>();
        Diagnostics = new DiagnosticsViewModel(latency, circuit);

        ClearCommand = new RelayCommand(Clear);
        ToggleCommand = new RelayCommand(ToggleOpen);
        OpenCommand = new RelayCommand(() => IsOpen = true);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        OpenLogFileCommand = new RelayCommand(OpenLogFile);

        _sink.EventAppended += OnEventAppended;
    }

    public event EventHandler? AutoScrollToBottomRequested;

    public ObservableCollection<LogEntryViewModel> Entries { get; } = new();

    public DiagnosticsViewModel Diagnostics { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                // Pause/resume diagnostics timer when switching tabs.
                if (value == 1)
                {
                    Diagnostics.Start();
                }
                else
                {
                    Diagnostics.Stop();
                }
            }
        }
    }

    public int EventCount
    {
        get => _eventCount;
        private set => SetProperty(ref _eventCount, value);
    }

    public ICommand ClearCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand OpenLogFileCommand { get; }

    private void OnEventAppended(object? sender, Portfolio.Resilience.Events.ResilienceEvent evt)
    {
        Entries.Add(new LogEntryViewModel(evt));

        while (Entries.Count > ObservableLogSink.MaxEvents)
        {
            Entries.RemoveAt(0);
        }

        EventCount = Entries.Count;
        AutoScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Clear()
    {
        // 1. Clear the in-memory view.
        _sink.Clear();
        Entries.Clear();
        EventCount = 0;

        // 2. Request a permanent clear. A marker file is written now; on
        //    next app startup, LogFileManager.ProcessPendingClear
        //    truncates the log file before the library opens it.
        _logManager.RequestClear();
    }

    private void ToggleOpen() => IsOpen = !IsOpen;

    private void OpenLogFile()
    {
        var path = _logManager.CurrentFilePath;

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Best-effort.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _sink.EventAppended -= OnEventAppended;
        Diagnostics.Dispose();
        _disposed = true;
    }
}
