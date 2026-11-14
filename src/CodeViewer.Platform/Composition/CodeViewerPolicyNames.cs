// ============================================================================
// FILE:     src/CodeViewer.Platform/Composition/CodeViewerPolicyNames.cs
// PURPOSE:  Canonical string constants for the Portfolio.Resilience policy
//           names owned by CodeViewer. Using constants prevents typo-driven
//           policy misses (the documented trap in Portfolio.Resilience 0.8.0
//           troubleshooting; see FileHarvester PolicyNames.cs).
// RELATION: Registered by CodeViewerComposition.RegisterResiliencePolicies
//           and referenced by every CodeViewer call site that invokes
//           IResilienceExecutor.ExecuteAsync. Mirrors the ImageTextify
//           precedent (ImageTextifyPolicyNames).
// ============================================================================

namespace CodeViewer.Platform.Composition;

/// <summary>
/// The named resilience policies owned by CodeViewer. Policies belonging to
/// other features are never redeclared here; CodeViewer reuses
/// <c>file-read</c> for file content reads and <c>bundle-write</c> for
/// workspace state writes because those failure profiles already fit.
/// </summary>
public static class CodeViewerPolicyNames
{
    /// <summary>
    /// Individual local directory-enumeration calls performed while the
    /// Project Browser lazily loads folder contents and while Auto Display
    /// walks the tree. The policy wraps one directory call at a time, never
    /// a whole-project scan: expected per-entry filesystem errors
    /// (permission denied, path too long, entry deleted mid-scan) are
    /// handled at the entry level by the enumerator and never reach the
    /// policy. Only transient <see cref="System.IO.IOException"/> failures
    /// (for example a mapped-drive blip) are retried, twice, with a fixed
    /// ~250 ms delay. The circuit breaker is disabled (the threshold is
    /// <see cref="int.MaxValue"/>, which the breaker can never reach) and
    /// the timeout ceiling is disabled (<c>TimeoutMs = 0</c>) so that
    /// cancellation of a large scan stays responsive and a slow directory
    /// is never aborted mid-call.
    /// </summary>
    public const string DirectoryScan = "code-viewer-scan";
}
