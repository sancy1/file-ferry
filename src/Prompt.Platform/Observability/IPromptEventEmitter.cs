// ============================================================================
// FILE:     src/Prompt.Platform/Observability/IPromptEventEmitter.cs
// PURPOSE:  Abstraction for emitting Prompt App structured operation events.
//           Implementations construct a ResilienceEvent carrying the
//           operation name in Metadata and hand it to the registered
//           ILogSink fan-out, so the existing Log Drawer surfaces it without
//           any Prompt-specific UI code.
// RELATION: Implemented by PromptEventEmitter. Consumed by every Prompt
//           store, exporter and importer call site that emits an operation
//           event. Depends only on Prompt.Core.Domain.PromptEventNames and
//           System types.
// NOTES:   - This is intentionally not a wrapper around IResilienceExecutor.
//             The executor's ExecuteAsync does not take a metadata parameter
//             (verified against Portfolio.Resilience 0.8.0), so operation
//             names cannot ride on the library-emitted events. The emitter
//             constructs its own event and emits it through the existing
//             sink fan-out.
//           - Implementations must be thread-safe: Emit is called from
//             whatever thread the operation completes on.
//           - Emit must never throw; a failing emitter must not fail the
//             operation it is recording.
// ============================================================================

namespace Prompt.Platform.Observability;

/// <summary>
/// Emits Prompt App structured operation events.
/// </summary>
/// <remarks>
/// <para>
/// The emitter constructs a <c>ResilienceEvent</c> with the operation name in
/// its <c>Metadata</c> dictionary and forwards it to every registered
/// <c>ILogSink</c>. The event is tagged with the Prompt policy that produced
/// it (default: <c>PromptPolicyNames.PromptStore</c>), so the existing Log
/// Drawer and Diagnostics views present it alongside the library's own
/// events.
/// </para>
/// <para>
/// Implementations must be thread-safe and must never throw.
/// </para>
/// </remarks>
public interface IPromptEventEmitter
{
    /// <summary>
    /// Emits a structured event for a Prompt operation.
    /// </summary>
    /// <param name="operation">
    /// One of the constants in <see cref="Prompt.Core.Domain.PromptEventNames"/>.
    /// Must not be null or empty.
    /// </param>
    /// <param name="promptId">
    /// The prompt the operation acted on, when known. <c>null</c> for
    /// operations that do not target a single prompt (for example a list
    /// search).
    /// </param>
    /// <param name="correlationId">
    /// Optional correlation id to attach to the event. When null the
    /// library's ambient correlation behaviour is not altered; the event is
    /// emitted with a null correlation id.
    /// </param>
    void Emit(string operation, Guid? promptId = null, string? correlationId = null);
}