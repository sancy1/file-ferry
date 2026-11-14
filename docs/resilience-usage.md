# File-Ferry - Resilience Usage

Status: Current as of v1.1. Supersedes the historical stub text below.

## Policies

- file-read    - FileResolver, ContentReader - existence, metadata, content reads
- path-search  - IPathSearchService - whole-PC filename search
- bundle-write - OutputSink - final output write

## Correlation

Every harvest job gets one correlation ID via CorrelationContext.NewId().
It flows through every log event, exception, and metric sample for the job.

---

## Policies (v1.1 - current)

This section supersedes the stub above. It documents the eight named
policies registered with Portfolio.Resilience 0.8.0 as of v1.1.

| Policy       | Call sites                                              | Retry | Base delay | Max delay | Jitter | Circuit threshold | Circuit open | Circuit success | Timeout | Rate limit | Rate window |
| ------------ | ------------------------------------------------------- | ----- | ---------- | --------- | ------ | ----------------- | ------------ | --------------- | ------- | ---------- | ----------- |
| file-read    | FileResolver, ContentReader, OutputSink existence check | 4     | 80ms       | 1000ms    | 0.20   | 10                | 15s          | 2               | 3s      | -          | -           |
| path-search  | IPathSearchService                                      | 2     | 200ms      | 500ms     | 0.00   | 3                 | 30s          | 1               | 180s    | -          | -           |
| bundle-write | OutputSink (local write)                                | 3     | 150ms      | 2000ms    | 0.10   | 5                 | 20s          | 1               | 15s     | -          | -           |
| cloud-read   | CloudFileResolver, CloudContentReader, CloudPathSearchService | 5 | 100ms | 2000ms | 0.20 | 5 | 30s | 2 | 30s | 300/min | 60s |
| cloud-write  | CloudOutputSink                                         | 3     | 200ms      | 3000ms    | 0.15   | 3                 | 60s          | 2               | 300s    | 60/min     | 60s         |
| cloud-search | CloudPathSearchService listings                         | 3     | 150ms      | 1500ms    | 0.15   | 3                 | 30s          | 2               | 60s     | 120/min    | 60s         |
| cloud-auth   | Token refresh, credential validation                    | 2     | 500ms      | 5000ms    | 0.10   | 3                 | 300s         | 1               | 15s     | 30/min     | 60s         |
| credential-store-write | ICredentialStore.WriteAsync (caller: CloudPage.axaml.cs) | 2 | 100ms | 500ms | 0.00 | 3 | 15s | 2 | 5s | - | - |
| code-viewer-scan | ProjectTreeEnumerator (one directory-enumeration call) | 2 | 250ms | 250ms | 0.00 | disabled | - | - | disabled | - | - |

### code-viewer-scan (CodeViewer)

`code-viewer-scan` wraps **one directory-enumeration call at a time**, never a
whole-project scan. It is registered by CodeViewer through the
`configureCodeViewerPolicies` callback on `AddFileHarvester` (the additive
overload; the original four-parameter signature is unchanged).

- **Retry:** two retries after the initial attempt, fixed ~250 ms delay, and
  only transient `IOException` (for example a mapped-drive blip) is retried.
  `UnauthorizedAccessException`, `DirectoryNotFoundException`, and
  `PathTooLongException` are classified permanent and are never retried; the
  enumerator skips the offending entry and continues.
- **Circuit breaker: disabled.** Portfolio.Resilience 0.8.0 has no circuit
  opt-out flag, and a `FailureThreshold` of 0 opens the circuit immediately
  (verified against the 0.8.0 runtime), so the threshold is set to
  `int.MaxValue` — a count consecutive failures can never reach. A local
  directory scan must never be circuit-broken.
- **Timeout: disabled** (`TimeoutMs = 0`, the documented opt-out). A slow
  directory is never aborted mid-call; responsiveness comes from the caller's
  cancellation token, which the enumerator checks between entries.

Rationale: retrying or aborting an entire scan because one directory entry
failed would repeat completed work and starve cancellation. Entry-level
tolerance plus a narrow transient-only retry is the correct failure profile
for local enumeration.

Rate limits use RateLimitStrategy.SlidingWindow. The window is 60
seconds; PermitLimit is the per-minute maximum.

## ExecuteAsync overloads

Portfolio.Resilience 0.8.0 exposes two overloads of
IResilienceExecutor.ExecuteAsync. Both take six parameters; call sites
use named arguments for every parameter.

    ExecuteAsync<T>(
        string policyName,
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task<T>>? fallback = null,
        string? idempotencyKey = null,
        int? timeBudgetMs = null,
        CancellationToken ct = default)

    ExecuteAsync(
        string policyName,
        Func<CancellationToken, Task> operation,
        Func<CancellationToken, Task>? fallback = null,
        string? idempotencyKey = null,
        int? timeBudgetMs = null,
        CancellationToken ct = default)

## Error classification

Classification is by fully-qualified type name string, not by System.Type.
The ResilienceException.Category property is the discriminator for the
caller, not the exception type.

Transient (retried):

- System.IO.IOException
- System.Net.Http.HttpRequestException
- System.Net.Sockets.SocketException
- System.TimeoutException
- Amazon.S3.AmazonS3Exception
- Azure.RequestFailedException
- Google.GoogleApiException

Permanent (not retried):

- System.IO.FileNotFoundException
- System.IO.DirectoryNotFoundException
- System.UnauthorizedAccessException
- System.Security.SecurityException
- System.IO.PathTooLongException

## Accepted exception

Directory.CreateDirectory(config.LogDirectory) in
ServiceCollectionExtensions.AddFileHarvester runs outside a policy.
This is a deliberate exception to the "every filesystem call goes
through the executor" rule. Reason: the call runs once at startup on
a directory the application owns, and its failure modes are not
transient failures that a retry could recover from. Recorded here per
the honest-findings discipline (finding F-009).
