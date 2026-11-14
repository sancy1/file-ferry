// ============================================================================
// FILE:     src/CodeViewer.Platform/Composition/CodeViewerComposition.cs
// PURPOSE:  Owns the registration of CodeViewer-owned Portfolio.Resilience
//           policies. Called from the App composition root through the
//           AddFileHarvester callback parameter, so FileHarvester.Core
//           does not need to reference CodeViewer.Platform.
// RELATION: Invoked from App.axaml.cs as
//           configureCodeViewerPolicies: CodeViewerComposition.RegisterResiliencePolicies
//           Mirrors the ImageTextify precedent (ImageTextifyComposition).
// ============================================================================

using Portfolio.Resilience.Configuration;

namespace CodeViewer.Platform.Composition;

/// <summary>
/// Composition helpers for CodeViewer-owned resilience policies.
/// </summary>
public static class CodeViewerComposition
{
    /// <summary>
    /// Registers every CodeViewer-owned resilience policy into the supplied
    /// <see cref="ResilienceBuilder"/>.
    /// </summary>
    /// <param name="builder">
    /// The resilience builder supplied by
    /// <c>FileHarvester.Core.Composition.ServiceCollectionExtensions.AddFileHarvester</c>
    /// through its <c>configureCodeViewerPolicies</c> parameter. Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Only policies that CodeViewer owns are registered here. The method
    /// does not touch any policy belonging to another feature.
    /// </para>
    /// <para>
    /// <see cref="CodeViewerPolicyNames.DirectoryScan"/> wraps individual
    /// directory-enumeration calls, never a whole-project scan. Retrying or
    /// aborting an entire scan because one directory entry failed would
    /// repeat completed work and starve cancellation, so:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Retry is limited to two retries after the initial attempt with a
    /// fixed ~250 ms delay, and only transient failures are retried.
    /// <c>System.IO.IOException</c> is already classified transient by
    /// FileHarvester.Core; <c>UnauthorizedAccessException</c>,
    /// <c>DirectoryNotFoundException</c>, and <c>PathTooLongException</c>
    /// are classified permanent and are never retried.
    /// </item>
    /// <item>
    /// The circuit breaker is disabled by setting the failure threshold to
    /// <see cref="int.MaxValue"/>, a count consecutive failures can never
    /// reach. Portfolio.Resilience 0.8.0 has no circuit opt-out flag, and
    /// a threshold of 0 opens the circuit immediately (verified against
    /// the 0.8.0 runtime), so <see cref="int.MaxValue"/> is the safe
    /// "never opens" configuration.
    /// </item>
    /// <item>
    /// The timeout ceiling is disabled with <c>TimeoutMs = 0</c>, the
    /// documented opt-out. Responsiveness comes from the caller's
    /// cancellation token, which the enumerator checks between entries.
    /// </item>
    /// </list>
    /// </remarks>
    public static void RegisterResiliencePolicies(ResilienceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPolicy(CodeViewerPolicyNames.DirectoryScan, p =>
        {
            // Two retries after the initial attempt, fixed ~250 ms delay.
            // Only transient IOException is retried; permanent filesystem
            // errors are handled at the entry level by the enumerator.
            p.Retry.MaxAttempts           = 2;
            p.Retry.BaseDelayMs           = 250;
            p.Retry.MaxDelayMs            = 250;
            p.Retry.JitterRatio           = 0.0;

            // Circuit breaker disabled: int.MaxValue consecutive failures
            // can never occur, so the circuit never opens. (Threshold 0
            // would open it immediately - verified against 0.8.0.)
            p.Circuit.FailureThreshold    = int.MaxValue;
            p.Circuit.OpenDurationSeconds = 1;
            p.Circuit.SuccessThreshold    = 1;
            p.Circuit.OnlyCountTransient  = true;

            // Timeout ceiling disabled (0 = documented opt-out).
            // Cancellation stays responsive via the caller's token.
            p.Timeout.TimeoutMs           = 0;
        });
    }
}
