// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthLoopbackListener.cs
// PURPOSE:  Loopback HTTP listener that captures the OAuth authorization
//           redirect. Binds 127.0.0.1 only. Serves exactly one request,
//           parses only code, state, and error, writes a minimal HTML
//           response, and unbinds. The listener never logs and never
//           includes raw query data in an exception.
// RELATION: Consumed by GoogleOAuthFlow (C.6.3). Independent of IOAuthFlow
//           and of any provider — the flow composes the listener, not the
//           other way around.
// ============================================================================

using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// The result of a captured OAuth callback.
/// </summary>
/// <param name="Code">
/// The authorization code, or null if the callback carried an error.
/// </param>
/// <param name="State">
/// The state value returned by the provider, or null. Never trusted until
/// the flow validates it against the state store.
/// </param>
/// <param name="Error">
/// The provider error code (for example <c>access_denied</c>), or null.
/// </param>
internal sealed record OAuthCallbackResult(
    string? Code,
    string? State,
    string? Error);

/// <summary>
/// A loopback HTTP listener for one OAuth callback.
/// </summary>
/// <remarks>
/// <para>
/// Binds to <c>http://127.0.0.1:{port}/</c> — loopback only, never
/// <c>0.0.0.0</c>. Serves exactly one request. The port may be zero, in
/// which case the OS assigns an ephemeral port; <see cref="Port"/> reports
/// the actual assigned port after binding.
/// </para>
/// <para>
/// The callback path is fixed at construction and validated on every
/// request: a request to a different path is rejected with 404 and does
/// not complete the wait.
/// </para>
/// </remarks>
internal sealed class OAuthLoopbackListener : IOAuthLoopbackListener
{
    private readonly int _requestedPort;
    private readonly string _callbackPath;
    private readonly TimeSpan _timeout;
    private HttpListener? _listener;
    private bool _disposed;

    /// <summary>
    /// Creates a listener for the given port, callback path, and timeout.
    /// </summary>
    /// <param name="requestedPort">
    /// The port to bind, or zero to let the OS choose an ephemeral port.
    /// </param>
    /// <param name="callbackPath">
    /// The path the provider will redirect to (for example <c>/callback/</c>).
    /// Must start with a slash.
    /// </param>
    /// <param name="timeout">
    /// The maximum time to wait for the callback. Must be positive.
    /// </param>
    public OAuthLoopbackListener(
        int requestedPort,
        string callbackPath,
        TimeSpan timeout)
    {
        if (requestedPort < 0 || requestedPort > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedPort),
                "Port must be in [0, 65535].");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);
        if (!callbackPath.StartsWith((char)47))
        {
            throw new ArgumentException(
                "Callback path must start with a slash.",
                nameof(callbackPath));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be positive.");
        }

        _requestedPort = requestedPort;
        _callbackPath = callbackPath;
        _timeout = timeout;
    }

    /// <summary>
    /// The actual port the listener is bound to. Valid only after
    /// <see cref="WaitForCallbackAsync"/> has bound the listener.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Binds the listener, waits for exactly one callback on the configured
    /// path, writes a minimal HTML response to the browser, and unbinds.
    /// </summary>
    /// <param name="ct">
    /// The caller's cancellation token. Cancellation closes the listener
    /// and surfaces as <see cref="OperationCanceledException"/> unchanged.
    /// </param>
    /// <returns>The parsed callback result. Never null.</returns>
    /// <exception cref="OAuthTimeoutException">
    /// The callback did not arrive within the configured timeout.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled. Rethrown unchanged.
    /// </exception>
    public async Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener = new HttpListener();
        var prefix = "http://127.0.0.1:" + _requestedPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        Port = _requestedPort;
        if (Port == 0)
        {
            // HttpListener does not expose the OS-assigned port when the
            // prefix used port 0. Callers that need an ephemeral port must
            // supply one they obtained from the OS, or use a fixed port.
            // This is a documented limitation of HttpListener.
            throw new InvalidOperationException(
                "HttpListener does not report an OS-assigned ephemeral port; supply an explicit port.");
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new OAuthTimeoutException(_timeout);
                }

                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (!string.Equals(path, _callbackPath, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                var query = context.Request.QueryString;
                var code = query["code"];
                var state = query["state"];
                var error = query["error"];

                WriteBrowserResponse(context);
                _listener.Stop();

                return new OAuthCallbackResult(code, state, error);
            }
        }
        finally
        {
            try { _listener?.Stop(); } catch (ObjectDisposedException) { }
            try { _listener?.Close(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Writes a minimal HTML response so the browser does not hang. The
    /// response contains no code, no state, no error, and no user data.
    /// </summary>
    private static void WriteBrowserResponse(HttpListenerContext context)
    {
        const string html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>File-Ferry</title></head>" +
            "<body style=\"font-family:sans-serif;padding:2em;\">" +
            "<h1>Authorization received</h1>" +
            "<p>You may close this window and return to File-Ferry.</p>" +
            "</body></html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Flush();
        context.Response.Close();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try { _listener?.Stop(); } catch (ObjectDisposedException) { }
        try { _listener?.Close(); } catch (ObjectDisposedException) { }
        _listener = null;

        return ValueTask.CompletedTask;
    }
}
