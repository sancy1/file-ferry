// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/LogEntryViewModel.cs
// PURPOSE:  View model for one row in the Log drawer. Wraps a
//           ResilienceEvent and exposes display-friendly properties:
//           formatted timestamp, human-readable event type, policy,
//           correlation ID, message, and a color brush for the row accent.
// RELATION: Created by LogDrawerViewModel from ObservableLogSink events.
//           Bound by Views/LogDrawer.axaml.
// ============================================================================

using System.Globalization;
using Avalonia.Media;
using Portfolio.Resilience.Events;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the log drawer.
/// </summary>
public sealed class LogEntryViewModel
{
    public LogEntryViewModel(ResilienceEvent evt)
    {
        Event = evt ?? throw new ArgumentNullException(nameof(evt));

        Timestamp = evt.TimestampUtc
            .ToLocalTime()
            .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        EventTypeText = HumanizeEventType(evt.EventType);
        PolicyName = string.IsNullOrEmpty(evt.PolicyName) ? "—" : evt.PolicyName;
        CorrelationId = ShortenCorrelation(evt.CorrelationId);
        Message = BuildMessage(evt);
        AccentBrush = BrushForEventType(evt.EventType);
    }

    public ResilienceEvent Event { get; }
    public string Timestamp { get; }
    public string EventTypeText { get; }
    public string PolicyName { get; }
    public string CorrelationId { get; }
    public string Message { get; }
    public SolidColorBrush AccentBrush { get; }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static string HumanizeEventType(ResilienceEventType eventType) => eventType switch
    {
        ResilienceEventType.CallStarted        => "Call Started",
        ResilienceEventType.RetryAttempted     => "Retry Attempted",
        ResilienceEventType.CallSucceeded      => "Call Succeeded",
        ResilienceEventType.CallFailed         => "Call Failed",
        ResilienceEventType.CircuitOpened      => "Circuit Opened",
        ResilienceEventType.CircuitClosed      => "Circuit Closed",
        ResilienceEventType.CircuitHalfOpened  => "Circuit Half-Opened",
        ResilienceEventType.FallbackUsed       => "Fallback Used",
        ResilienceEventType.TimeoutBreached    => "Timeout Breached",
        ResilienceEventType.RateLimited        => "Rate Limited",
        ResilienceEventType.BulkheadRejected   => "Bulkhead Rejected",
        ResilienceEventType.HedgeWon           => "Hedge Won",
        ResilienceEventType.HedgeLost          => "Hedge Lost",
        ResilienceEventType.HedgeCancelled     => "Hedge Cancelled",
        _                                      => eventType.ToString(),
    };

    private static string ShortenCorrelation(string? correlationId)
    {
        if (string.IsNullOrEmpty(correlationId)) return "—";
        return correlationId.Length > 8 ? correlationId[..8] : correlationId;
    }

    private static string BuildMessage(ResilienceEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.ErrorMessage))
        {
            return evt.ErrorMessage!;
        }

        var bits = new List<string>();

        if (evt.Attempt is int attempt && attempt > 0)
        {
            bits.Add($"attempt={attempt}");
        }

        if (evt.DurationMs is double ms && ms > 0)
        {
            bits.Add($"duration={ms.ToString("F1", CultureInfo.InvariantCulture)}ms");
        }

        if (evt.ErrorCategory is not null)
        {
            bits.Add($"category={evt.ErrorCategory}");
        }

        if (evt.Metadata is { Count: > 0 })
        {
            foreach (var kv in evt.Metadata)
            {
                bits.Add($"{kv.Key}={kv.Value}");
            }
        }

        return bits.Count > 0 ? string.Join(" · ", bits) : "—";
    }

    private static SolidColorBrush BrushForEventType(ResilienceEventType eventType) => eventType switch
    {
        ResilienceEventType.CallSucceeded
            or ResilienceEventType.CircuitClosed        => new SolidColorBrush(Color.Parse("#22C55E")),

        ResilienceEventType.RetryAttempted
            or ResilienceEventType.FallbackUsed
            or ResilienceEventType.TimeoutBreached
            or ResilienceEventType.RateLimited
            or ResilienceEventType.BulkheadRejected
            or ResilienceEventType.HedgeWon
            or ResilienceEventType.HedgeLost
            or ResilienceEventType.HedgeCancelled       => new SolidColorBrush(Color.Parse("#F59E0B")),

        ResilienceEventType.CallFailed
            or ResilienceEventType.CircuitOpened
            or ResilienceEventType.CircuitHalfOpened    => new SolidColorBrush(Color.Parse("#EF4444")),

        _                                               => new SolidColorBrush(Color.Parse("#9CA3AF")),
    };
}
