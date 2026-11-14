// ============================================================================
// FILE:     src/Prompt.Platform/Composition/PromptPolicyNames.cs
// PURPOSE:  Canonical string constants for the three named Portfolio.Resilience
//           policies owned by the Prompt App, mirroring the PolicyNames
//           pattern in FileHarvester.Core without modifying it.
// RELATION: Referenced by PromptResilienceConfig (registration) and by every
//           Prompt store/export call site that invokes
//           IResilienceExecutor.ExecuteAsync. Verified by
//           PromptPolicyRegistrationTests.
// NOTES:   Under a two-registration composition (verified by probes, 0.8.0),
//           an unknown policy name executes silently bare. The registration
//           test therefore executes each of these constants and asserts the
//           emitted events carry the exact policy name.
// ============================================================================

namespace Prompt.Platform.Composition;

/// <summary>
/// The three named resilience policies owned by the Prompt App.
/// </summary>
public static class PromptPolicyNames
{
    /// <summary>
    /// SQLite reads and writes for prompts, categories, versions and drafts.
    /// Local operations are fast: modest retry, 5-second per-attempt ceiling,
    /// circuit opens after 3 consecutive transient failures.
    /// </summary>
    public const string PromptStore = "prompt-store";

    /// <summary>
    /// Rendering conversions (for example Markdown to plain text).
    /// CPU-bound with no external dependency: light retry, 5-second ceiling.
    /// </summary>
    public const string PromptRender = "prompt-render";

    /// <summary>
    /// Format-writer disk writes on export. Mirrors the verified
    /// bundle-write tuning: 3 retries, 15-second ceiling, circuit after 5.
    /// </summary>
    public const string PromptExport = "prompt-export";
}
