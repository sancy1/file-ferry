// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/DiagnosticsViewModel.cs
// PURPOSE:  View model for the Diagnostics tab. Runs a 1-second refresh
//           timer, pulls snapshots from ILatencyTracker and
//           ICircuitBreakerMonitor, and rebuilds the metric and circuit
//           tables. Exposes export-diagnostics command with visible status.
// RELATION: Consumed by Views/DiagnosticsView.axaml. Child of
//           LogDrawerViewModel. Resolves services from App.Services.
// ============================================================================

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Correlation;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// View model for the Diagnostics tab.
/// </summary>
public sealed class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ILatencyTracker _latency;
    private readonly ICircuitBreakerMonitor _circuit;

    private DispatcherTimer? _timer;
    private bool _isActive;
    private bool _disposed;
    private string _lastRefreshText = "—";
    private string _correlationIdText = "—";
    private string _exportStatusText = string.Empty;

    public DiagnosticsViewModel(
        ILatencyTracker latency,
        ICircuitBreakerMonitor circuit)
    {
        _latency = latency ?? throw new ArgumentNullException(nameof(latency));
        _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));

        RefreshCommand = new RelayCommand(RefreshNow);
        ExportCommand = new RelayCommand(ExportSnapshot);
    }

    public ObservableCollection<PolicyMetricRowViewModel> Metrics { get; } = new();
    public ObservableCollection<CircuitRowViewModel> Circuits { get; } = new();

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetProperty(ref _lastRefreshText, value);
    }

    public string CorrelationIdText
    {
        get => _correlationIdText;
        private set => SetProperty(ref _correlationIdText, value);
    }

    public string ExportStatusText
    {
        get => _exportStatusText;
        private set => SetProperty(ref _exportStatusText, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }

    public void Start()
    {
        if (_isActive) return;
        _isActive = true;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => RefreshNow();
        _timer.Start();

        RefreshNow();
    }

    public void Stop()
    {
        _isActive = false;
        _timer?.Stop();
        _timer = null;
    }

    // ------------------------------------------------------------------
    // Refresh.
    // ------------------------------------------------------------------

    private void RefreshNow()
    {
        try
        {
            var latencySnapshots = _latency.Snapshot();

            Metrics.Clear();
            foreach (var s in latencySnapshots)
            {
                Metrics.Add(new PolicyMetricRowViewModel(
                    policyName: s.PolicyName,
                    totalCalls: s.TotalCalls,
                    errorRate:  s.ErrorRate,
                    p50Ms:      s.P50Ms,
                    p95Ms:      s.P95Ms,
                    p99Ms:      s.P99Ms,
                    inFlight:   s.InFlight));
            }

            var circuitSnapshots = _circuit.Snapshot();

            Circuits.Clear();
            foreach (var c in circuitSnapshots)
            {
                Circuits.Add(new CircuitRowViewModel(
                    policyName:          c.PolicyName,
                    state:               c.State,
                    consecutiveFailures: c.ConsecutiveFailures,
                    openedAtUtc:         c.OpenedAtUtc,
                    nextProbeAtUtc:      c.NextProbeAtUtc));
            }

            LastRefreshText = DateTime.UtcNow
                .ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

            CorrelationIdText = CorrelationContext.CurrentId ?? "—";
        }
        catch (Exception)
        {
            // Never crash the UI on a snapshot failure.
        }
    }

    // ------------------------------------------------------------------
    // Export.
    // ------------------------------------------------------------------

    private void ExportSnapshot()
    {
        try
        {
            var snapshot = new
            {
                exportedAtUtc = DateTime.UtcNow,
                correlationId = CorrelationContext.CurrentId,
                metrics = Metrics.Select(m => new
                {
                    policy     = m.PolicyName,
                    totalCalls = m.TotalCalls,
                    errorRate  = m.ErrorRate,
                    p50Ms      = m.P50Ms,
                    p95Ms      = m.P95Ms,
                    p99Ms      = m.P99Ms,
                    inFlight   = m.InFlight,
                }),
                circuits = Circuits.Select(c => new
                {
                    policy              = c.PolicyName,
                    state               = c.StateText,
                    consecutiveFailures = c.ConsecutiveFailures,
                    openedAtUtc         = c.OpenedAtUtc,
                    nextProbeAtUtc      = c.NextProbeAtUtc,
                }),
            };

            var json = JsonSerializer.Serialize(snapshot, ExportJsonOptions);

            var documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);

            var fileName = $"file-ferry-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var path = Path.Combine(documents, fileName);

            File.WriteAllText(path, json, Encoding.UTF8);

            ExportStatusText = $"Saved to {path}";
        }
        catch (Exception ex)
        {
            ExportStatusText = $"Export failed: {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
