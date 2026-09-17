// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/PolicyMetricRowViewModel.cs
// PURPOSE:  One row in the Diagnostics latency table. Wraps a LatencySnapshot
//           from Portfolio.Resilience's ILatencyTracker and exposes
//           display-ready formatted strings.
// RELATION: Created by DiagnosticsViewModel on every refresh tick. Bound by
//           Views/DiagnosticsView.axaml.
// ============================================================================

using System.Globalization;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the latency table.
/// </summary>
public sealed class PolicyMetricRowViewModel
{
    public PolicyMetricRowViewModel(
        string policyName,
        long totalCalls,
        double errorRate,
        double p50Ms,
        double p95Ms,
        double p99Ms,
        int inFlight)
    {
        PolicyName = policyName;
        TotalCalls = totalCalls;
        ErrorRate = errorRate;
        P50Ms = p50Ms;
        P95Ms = p95Ms;
        P99Ms = p99Ms;
        InFlight = inFlight;
    }

    public string PolicyName { get; }
    public long TotalCalls { get; }
    public double ErrorRate { get; }
    public double P50Ms { get; }
    public double P95Ms { get; }
    public double P99Ms { get; }
    public int InFlight { get; }

    public string CallsText => TotalCalls.ToString("N0", CultureInfo.InvariantCulture);

    public string ErrorRateText => TotalCalls == 0
        ? "—"
        : (ErrorRate * 100).ToString("F1", CultureInfo.InvariantCulture) + " %";

    public string P50Text => FormatMs(P50Ms);
    public string P95Text => FormatMs(P95Ms);
    public string P99Text => FormatMs(P99Ms);

    public string InFlightText => InFlight.ToString(CultureInfo.InvariantCulture);

    private static string FormatMs(double ms)
    {
        if (ms <= 0) return "—";
        if (ms < 1) return "<1 ms";
        return ms.ToString("F1", CultureInfo.InvariantCulture) + " ms";
    }
}
