// ============================================================================
// FILE:     src/FileHarvester.Core/Composition/PolicyNames.cs
// PURPOSE:  Canonical string constants for the three Portfolio.Resilience
//           policy names used throughout File-Ferry.
// RELATION: Referenced by ServiceCollectionExtensions (registration) and by
//           every call site that invokes IResilienceExecutor.ExecuteAsync:
//           FileResolver, ContentReader, IPathSearchService implementations,
//           and OutputSink. Using constants prevents typo-driven policy
//           misses (a documented trap in Portfolio.Resilience 0.8.0
//           troubleshooting section 24.12).
// ============================================================================

namespace FileHarvester.Core.Composition;

/// <summary>
/// The three named policies registered with Portfolio.Resilience.
/// Each policy owns one category of I/O work and is tuned for that work's
/// failure profile. See docs/resilience-usage.md for the rationale behind
/// each policy's retry / circuit / timeout settings.
/// </summary>
public static class PolicyNames
{
    /// <summary>
    /// File existence checks, metadata reads, and content reads.
    /// Retries transient I/O failures (network share blips) with modest
    /// backoff; trips the circuit after 10 consecutive transient failures;
    /// bounds each attempt at 3 seconds.
    /// </summary>
    public const string FileRead = "file-read";

    /// <summary>
    /// Whole-PC filename search. Slower and more expensive than file reads,
    /// so retries are more conservative and timeouts are longer. Trips the
    /// circuit after only 3 failures to fail fast when a drive goes offline.
    /// </summary>
    public const string PathSearch = "path-search";

    /// <summary>
    /// Final output writes (bundles or per-file documents). Large payloads
    /// get a 15-second ceiling per attempt. Trips the circuit after 5
    /// consecutive failures.
    /// </summary>
    public const string BundleWrite = "bundle-write";
}
