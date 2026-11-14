// ============================================================================
// FILE:     src/Prompt.Platform/Observability/PromptEventEmitter.cs
// PURPOSE:  Default IPromptEventEmitter. Constructs a ResilienceEvent with
//           the operation name and prompt id in Metadata and forwards it to
//           every registered ILogSink. This is the only mechanism the
//           verified Portfolio.Resilience 0.8.0 surface supports for named
//           operation events.
// RELATION: Implements IPromptEventEmitter. Depends on ILogSink
//           (Portfolio.Resilience.Abstractions), ResilienceEvent and
//           ResilienceEventType (Portfolio.Resilience.Events), and
//           PromptPolicyNames (Prompt.Platform.Composition). Consumed by
//           Prompt store/export/import call sites. Verified by
//           PromptEventVocabularyTests.
// NOTES:   - ResilienceEvent's Metadata property is
//             IReadOnlyDictionary<string, object> and its setter is public
//             (verified by reflection against the installed 0.8.0 DLL). The
//             emitter assigns a fresh Dictionary<string, object> per event.
//           - EventType is CallSucceeded because the emitter is invoked
//             after the operation completed successfully. Failures are
//             recorded by the library's own CallFailed event; this emitter
//             does not duplicate that path.
//           - Emit never throws: each sink is wrapped so a faulty sink
//             cannot fail the operation it is recording. This is deliberate
//             and must not be "simplified" away.
//           - The sinks are resolved once in the constructor and treated as
//             immutable. Registration of new sinks happens through
//             ResilienceBuilder.AddLogSink at composition time, not at
//             runtime.
// ============================================================================

using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;
using Prompt.Platform.Composition;

namespace Prompt.Platform.Observability;

/// <summary>
/// Default <see cref="IPromptEventEmitter"/>.
/// </summary>
/// <remarks>
/// Constructs a <see cref="ResilienceEvent"/> per call with the operation
/// name in <c>Metadata["operation"]</c> and, when supplied, the prompt id in
/// <c>Metadata["promptId"]</c>. The event is emitted to every configured
/// <see cref="ILogSink"/>. Sinks that throw are isolated: the operation that
/// produced the event is not failed by a faulty sink.
/// </remarks>
public sealed class PromptEventEmitter : IPromptEventEmitter
{
    private readonly IReadOnlyList<ILogSink> _sinks;
    private readonly string _policyName;

    /// <summary>
    /// Creates an emitter that forwards events to the supplied sinks.
    /// </summary>
    /// <param name="sinks">
    /// The sinks to forward to. May be empty; the emitter then emits to
    /// nothing and does no work. Must not be null.
    /// </param>
    /// <param name="policyName">
    /// The Prompt policy name to tag the event with. Defaults to
    /// <see cref="PromptPolicyNames.PromptStore"/>. Must not be null or empty.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sinks"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="policyName"/> is null or empty.
    /// </exception>
    public PromptEventEmitter(
        IEnumerable<ILogSink> sinks,
        string policyName = PromptPolicyNames.PromptStore)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        if (string.IsNullOrWhiteSpace(policyName))
        {
            throw new ArgumentException("Policy name must not be empty.", nameof(policyName));
        }

        _sinks = sinks as IReadOnlyList<ILogSink> ?? new List<ILogSink>(sinks);
        _policyName = policyName;
    }

    /// <inheritdoc />
    public void Emit(string operation, Guid? promptId = null, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            // Guard: a blank operation would produce an untraceable event.
            return;
        }

        if (_sinks.Count == 0)
        {
            return;
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
        };
        if (promptId.HasValue)
        {
            metadata["promptId"] = promptId.Value;
        }

        var evt = new ResilienceEvent
        {
            EventType = ResilienceEventType.CallSucceeded,
            PolicyName = _policyName,
            CorrelationId = correlationId,
            TimestampUtc = DateTime.UtcNow,
            Metadata = metadata,
        };

        // WHAT: forward to every sink, isolating a faulty sink.
        // WHY:  the operation that produced this event must not fail because
        //       a sink implementation threw; sinks are ancillary.
        // NOT:  a single try/catch around the whole loop, because that would
        //       let one bad sink starve the rest.
        for (var i = 0; i < _sinks.Count; i++)
        {
            try
            {
                _sinks[i].Emit(evt);
            }
            catch
            {
                // Deliberately swallowed (see NOTES). The next sink still runs.
            }
        }
    }
}