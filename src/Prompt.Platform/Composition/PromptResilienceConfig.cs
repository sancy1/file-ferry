// ============================================================================
// FILE:     src/Prompt.Platform/Composition/PromptResilienceConfig.cs
// PURPOSE:  Registers the three prompt-* resilience policies with
//           Portfolio.Resilience, using the policy configuration shape
//           verified in FileHarvester.Core ServiceCollectionExtensions.
// RELATION: Called only by PromptServiceCollectionExtensions.AddPromptFeature.
//           Policy names come from PromptPolicyNames. Verified by
//           PromptPolicyRegistrationTests.
// NOTES:   - prompt-export tuning copies bundle-write exactly (verified values).
//           - prompt-render leaves the circuit breaker at the library
//             default: UNVERIFIED (no source evidence of the default or
//             of a disable flag). CPU-bound work rarely trips it.
//           - No PCI-safe scrubbing is configured: no such API exists in
//             the verified registration surface (PROMPT-F-012).
//           - ErrorClassification is builder-level, not per-policy: the
//             per-policy placement caused 3 x CS1061 and is corrected here
//             (PROMPT-F-017 / PROMPT-F-030).
// ============================================================================

using Portfolio.Resilience.Configuration;

namespace Prompt.Platform.Composition;

/// <summary>
/// Registration of the three Prompt App resilience policies.
/// </summary>
public static class PromptResilienceConfig
{
    /// <summary>
    /// Adds prompt-store, prompt-render and prompt-export to the builder.
    /// </summary>
    /// <param name="builder">The resilience builder supplied by
    /// AddPortfolioResilience. Never null.</param>
    public static void RegisterPolicies(ResilienceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPolicy(PromptPolicyNames.PromptStore, p =>
        {
            p.Retry.MaxAttempts           = 2;
            p.Retry.BaseDelayMs           = 50;
            p.Retry.MaxDelayMs            = 500;
            p.Retry.JitterRatio           = 0.1;

            p.Circuit.FailureThreshold    = 3;
            p.Circuit.OpenDurationSeconds = 15;
            p.Circuit.SuccessThreshold    = 1;
            p.Circuit.OnlyCountTransient  = true;

            p.Timeout.TimeoutMs           = 5_000;
        });

        builder.AddPolicy(PromptPolicyNames.PromptRender, p =>
        {
            p.Retry.MaxAttempts           = 2;
            p.Retry.BaseDelayMs           = 25;
            p.Retry.MaxDelayMs            = 250;
            p.Retry.JitterRatio           = 0.0;

            p.Timeout.TimeoutMs           = 5_000;
        });

        builder.AddPolicy(PromptPolicyNames.PromptExport, p =>
        {
            p.Retry.MaxAttempts           = 3;
            p.Retry.BaseDelayMs           = 150;
            p.Retry.MaxDelayMs            = 2_000;
            p.Retry.JitterRatio           = 0.1;

            p.Circuit.FailureThreshold    = 5;
            p.Circuit.OpenDurationSeconds = 20;
            p.Circuit.SuccessThreshold    = 1;
            p.Circuit.OnlyCountTransient  = true;

            p.Timeout.TimeoutMs           = 15_000;
        });

        // Builder-level error classification. Per-policy placement of
        // ErrorClassification is not supported by Portfolio.Resilience 0.8.0:
        // PolicyDefinition has no ErrorClassification member (CS1061). The
        // builder owns transient/permanent classification. Place once, after
        // the AddPolicy blocks.
        builder.ErrorClassification.TransientExceptionTypeNames.Add(
            "Microsoft.Data.Sqlite.SqliteException");
        builder.ErrorClassification.TransientExceptionTypeNames.Add(
            "System.IO.IOException");
    }
}
