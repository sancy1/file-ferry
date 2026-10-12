using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

internal interface IGoogleDrivePickerCallbackListener : IAsyncDisposable
{
    Task<GoogleDrivePickerCallback> WaitAsync(CancellationToken ct);
}

internal sealed class GoogleDrivePickerLoopbackListener : IGoogleDrivePickerCallbackListener
{
    private readonly int _port;
    private readonly string _path;
    private readonly TimeSpan _timeout;
    private HttpListener? _listener;

    internal GoogleDrivePickerLoopbackListener(
        int port,
        string path,
        TimeSpan timeout)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException("Callback path must start with a slash.", nameof(path));
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        _port = port;
        _path = path;
        _timeout = timeout;
    }

    public async Task<GoogleDrivePickerCallback> WaitAsync(CancellationToken ct)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://127.0.0.1:"
            + _port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
        _listener.Start();

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync()
                        .WaitAsync(linkedCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new OAuthTimeoutException(_timeout);
                }

                if (!string.Equals(
                    NormalizeCallbackPath(context.Request.Url?.AbsolutePath),
                    NormalizeCallbackPath(_path),
                    StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                var callback = GoogleDrivePickerCallback.Parse(
                    context.Request.Url?.Query ?? string.Empty);
                WriteBrowserResponse(context);
                return callback;
            }
        }
        finally
        {
            try { _listener.Stop(); } catch (ObjectDisposedException) { }
            try { _listener.Close(); } catch (ObjectDisposedException) { }
        }
    }

    private static string NormalizeCallbackPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var normalized = path.TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static void WriteBrowserResponse(HttpListenerContext context)
    {
        const string html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>File-Ferry</title></head>" +
            "<body style=\"font-family:sans-serif;padding:2em;\">" +
            "<h1>Selection received</h1>" +
            "<p>You may close this window and return to File-Ferry.</p>" +
            "</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    public ValueTask DisposeAsync()
    {
        try { _listener?.Stop(); } catch (ObjectDisposedException) { }
        try { _listener?.Close(); } catch (ObjectDisposedException) { }
        _listener = null;
        return ValueTask.CompletedTask;
    }
}
