// ============================================================================
// FILE:     src/FileHarvester.Core/Composition/PolicyNames.cs
// PURPOSE:  Canonical string constants for the eight Portfolio.Resilience
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
/// The eight named policies registered with Portfolio.Resilience.
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

    /// <summary>
    /// Cloud object reads: OpenReadAsync, ListAsync, GetMetadataAsync,
    /// ExistsAsync. Retries transient network and provider-throttle
    /// failures with exponential backoff; trips the circuit after 5
    /// consecutive transient failures; bounds each attempt at 30
    /// seconds; limits calls to 50 per rolling minute via a sliding
    /// window rate limiter.
    /// </summary>
    public const string CloudRead = "cloud-read";

    /// <summary>
    /// Cloud object writes: OpenWriteAsync completion and DeleteAsync.
    /// Long payloads get a 5-minute ceiling per attempt. Rate-limited
    /// to 10 calls per rolling minute. Trips the circuit after 3
    /// consecutive transient failures.
    /// </summary>
    public const string CloudWrite = "cloud-write";

    /// <summary>
    /// Cloud folder listings performed by the picker. 60-second ceiling
    /// per attempt, 30 calls per rolling minute, circuit opens after 3
    /// consecutive transient failures.
    /// </summary>
    public const string CloudSearch = "cloud-search";

    /// <summary>
    /// Token refresh and credential validation. Fast per attempt (15
    /// seconds), rate-limited to 20 calls per rolling minute. The
    /// circuit stays open for 5 minutes after opening, giving transient
    /// environment failures time to clear before a probe is attempted.
    /// </summary>
    public const string CloudAuth = "cloud-auth";

    /// <summary>
    /// Credential store writes: ICredentialStore.WriteAsync. Short
    /// per-attempt ceiling; the Windows Credential Manager call is
    /// local and fast. Trips the circuit after 3 consecutive transient
    /// failures; retries twice.
    /// </summary>
    public const string CredentialStoreWrite = "credential-store-write";
}
