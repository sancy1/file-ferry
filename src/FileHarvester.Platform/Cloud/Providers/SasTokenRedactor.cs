// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/SasTokenRedactor.cs
// PURPOSE:  Strips SAS query tokens (and URI fragments) from any string
//           before it is logged, included in an exception message, or
//           attached to telemetry. SAS tokens grant time-limited access to
//           Azure Storage resources; if they leak into logs or crash dumps,
//           an attacker can use them for the duration of the token.
// RELATION: Called by AzureBlobProvider and its adapter on every code path
//           that produces a URI string destined for a log, an exception, or
//           the resilience event stream. It is a static helper rather than
//           an extension method so that the call site is always visible in
//           source review.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Removes SAS query tokens from URI strings.
/// </summary>
public static class SasTokenRedactor
{
    /// <summary>
    /// The placeholder that replaces the removed query string.
    /// </summary>
    public const string RedactedPlaceholder = "[REDACTED]";

    /// <summary>
    /// Returns <paramref name="value"/> with any query string replaced by
    /// <see cref="RedactedPlaceholder"/>.
    /// </summary>
    /// <param name="value">
    /// The string to redact. Null or empty input returns the input
    /// unchanged — callers may pass a possibly-null URI without a guard.
    /// </param>
    /// <returns>
    /// The redacted string. If <paramref name="value"/> contains no
    /// <c>?</c> character, it is returned unchanged. If it does, everything
/// from the first <c>?</c> onward is replaced with <c>?[REDACTED]</c>,
/// including any fragment that followed the query string. If no
/// <c>?</c> is present, the value is returned unchanged (a fragment-only
/// URI is not redacted; SAS tokens always appear in the query string).
    /// </returns>
    /// <remarks>
    /// This method never throws. It never logs. It performs no I/O. It is
    /// safe to call from any thread and from any context, including inside
    /// catch blocks and inside exception constructors.
    /// </remarks>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var questionIndex = value.IndexOf('?');
        if (questionIndex < 0)
        {
            return value;
        }

        return string.Concat(
            value.AsSpan(0, questionIndex),
            "?",
            RedactedPlaceholder);
    }
}
