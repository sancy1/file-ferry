// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/CircuitRowViewModel.cs
// PURPOSE:  One row in the Diagnostics circuit-state table. Wraps a
//           CircuitSnapshot from Portfolio.Resilience's
//           ICircuitBreakerMonitor.
// RELATION: Created by DiagnosticsViewModel on every refresh tick. Bound by
//           Views/DiagnosticsView.axaml.
// ============================================================================

using System.Globalization;
using Avalonia.Media;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the circuit-state table.
/// </summary>
public sealed class CircuitRowViewModel
{
    public CircuitRowViewModel(
        string policyName,
        CircuitState state,
        int consecutiveFailures,
        DateTime? openedAtUtc,
        DateTime? nextProbeAtUtc)
    {
        PolicyName = policyName;
        State = state;
        ConsecutiveFailures = consecutiveFailures;
        OpenedAtUtc = openedAtUtc;
        NextProbeAtUtc = nextProbeAtUtc;
    }

    public string PolicyName { get; }
    public CircuitState State { get; }
    public int ConsecutiveFailures { get; }
    public DateTime? OpenedAtUtc { get; }
    public DateTime? NextProbeAtUtc { get; }

    public string StateText => State switch
    {
        CircuitState.Closed   => "Closed",
        CircuitState.Open     => "Open",
        CircuitState.HalfOpen => "Half-Open",
        _                     => State.ToString(),
    };

    public SolidColorBrush StateBrush => State switch
    {
        CircuitState.Closed   => new SolidColorBrush(Color.Parse("#22C55E")),
        CircuitState.Open     => new SolidColorBrush(Color.Parse("#EF4444")),
        CircuitState.HalfOpen => new SolidColorBrush(Color.Parse("#F59E0B")),
        _                     => new SolidColorBrush(Color.Parse("#9CA3AF")),
    };

    public string FailuresText =>
        ConsecutiveFailures.ToString(CultureInfo.InvariantCulture);

    public string OpenedAtText => OpenedAtUtc is DateTime dt
        ? dt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        : "—";

    public string NextProbeText => NextProbeAtUtc is DateTime dt
        ? dt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        : "—";
}
