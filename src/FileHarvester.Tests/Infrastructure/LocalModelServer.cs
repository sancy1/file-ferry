// ============================================================================
// FILE:     src/FileHarvester.Tests/Infrastructure/LocalModelServer.cs
// PURPOSE:  A local HTTP listener used by download tests. Binds to
//           127.0.0.1 on a free port, serves deterministic bytes, counts
//           requests, and can simulate a truncated body. Never contacts
//           an external network.
// RELATION: Test-only infrastructure. Used by ModelStoreTests in Phase 2.2
//           and by the OCR engine tests in Phase 2.4.
// ============================================================================

using System.Net;
using System.Text;

namespace FileHarvester.Tests.Infrastructure;

/// <summary>
/// A local HTTP server bound to 127.0.0.1 that serves a fixed payload
/// for the duration of a test.
/// </summary>
/// <remarks>
/// <para>
/// Binds to a free port on 127.0.0.1. Exposes <see cref="BaseUrl"/> and
/// <see cref="RequestCount"/>. Every request is served with the configured
/// payload unless <see cref="TruncateTo"/> was called, in which case the
/// response body is shorter than the declared size.
/// </para>
/// <para>
/// Implements <see cref="IDisposable"/>. Disposing stops the listener and
/// releases the port.
/// </para>
/// </remarks>
internal sealed class LocalModelServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;
    private readonly object _gate = new();

    private byte[] _payload = Array.Empty<byte>();
    private int? _truncateTo;
    private bool _disposed;
    private int _requestCount;

    /// <summary>
    /// Creates and starts the server on a free 127.0.0.1 port.
    /// </summary>
    public LocalModelServer()
    {
        // Pick a free port by binding a listener to port 0 and reading back
        // the assigned port.
        var port = GetFreePort();
        BaseUrl = "http://127.0.0.1:" + port + "/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>The base URL, ending in a slash.</summary>
    public string BaseUrl { get; }

    /// <summary>The number of requests received so far.</summary>
    public int RequestCount
    {
        get { lock (_gate) { return _requestCount; } }
    }

    /// <summary>
    /// Sets the payload the server returns on the next request.
    /// </summary>
    /// <param name="payload">The bytes to serve. Not null.</param>
    public void SetPayload(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        lock (_gate) { _payload = payload; _truncateTo = null; }
    }

    /// <summary>
    /// Serves only the first <paramref name="byteCount"/> bytes of the
    /// payload. The Content-Length header is omitted so the client sees
    /// the connection close as the end-of-body signal. This simulates a
    /// truncated download.
    /// </summary>
    /// <param name="byteCount">The number of bytes to send.</param>
    public void TruncateTo(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        lock (_gate) { _truncateTo = byteCount; }
    }

    /// <summary>
    /// Returns a URL that resolves to this server. Does not use the network.
    /// </summary>
    /// <returns>The base URL.</returns>
    public override string ToString() => BaseUrl;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }

    // ------------------------------------------------------------------
    // Private.
    // ------------------------------------------------------------------

    private static int GetFreePort()
    {
        var temp = new HttpListener();
        temp.Prefixes.Add("http://127.0.0.1:0/");
        // HttpListener does not accept :0 as a bound port on all platforms.
        // Use a TcpListener to pick a free port instead.
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync()
                    .ConfigureAwait(false);
            }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }

            lock (_gate) { _requestCount++; }

            try
            {
                await ServeAsync(context).ConfigureAwait(false);
            }
            catch
            {
                // A test-fixture failure must not tear down the listener.
            }
        }
    }

    private async Task ServeAsync(HttpListenerContext context)
    {
        byte[] body;
        bool sendContentLength;

        lock (_gate)
        {
            if (_truncateTo is { } n && n <= _payload.Length)
            {
                body = new byte[n];
                Array.Copy(_payload, body, n);
                sendContentLength = false;
            }
            else
            {
                body = _payload;
                sendContentLength = true;
            }
        }

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/octet-stream";
        if (sendContentLength)
        {
            context.Response.ContentLength64 = body.Length;
        }

        try
        {
            await context.Response.OutputStream
                .WriteAsync(body, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            try { context.Response.OutputStream.Close(); } catch { }
            try { context.Response.Close(); } catch { }
        }
    }
}
