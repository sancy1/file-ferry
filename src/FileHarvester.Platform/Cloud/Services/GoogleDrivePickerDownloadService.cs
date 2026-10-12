using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using Google;

namespace FileHarvester.Platform.Cloud.Services;

internal sealed class GoogleDrivePickerDownloadService : IGoogleDrivePickerDownloadService, IDisposable
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
    private const string DocsMime = "application/vnd.google-apps.document";
    private const string SheetsMime = "application/vnd.google-apps.spreadsheet";
    private const string SlidesMime = "application/vnd.google-apps.presentation";
    private const string DocsExportMime = "application/pdf";
    private const string SheetsExportMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string SlidesExportMime =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly IOAuthClientConfigStore _configStore;
    private readonly Func<OAuthClientConfig, string, CancellationToken, Task<string>> _getConnectedAccessToken;
    private readonly IGoogleDrivePickerOAuthFlow _pickerFlow;
    private readonly Func<string, IGoogleDrivePickerFileClient> _clientFactory;
    private readonly Func<DateTime> _utcNow;
    private readonly ConcurrentDictionary<string, PendingDownload> _pending = new(StringComparer.Ordinal);
    private readonly Timer _expiryTimer;

    internal GoogleDrivePickerDownloadService(
        IOAuthClientConfigStore configStore,
        Func<OAuthClientConfig, string, CancellationToken, Task<string>> getConnectedAccessToken,
        IGoogleDrivePickerOAuthFlow pickerFlow,
        Func<string, IGoogleDrivePickerFileClient>? clientFactory = null,
        Func<DateTime>? utcNow = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _getConnectedAccessToken = getConnectedAccessToken ?? throw new ArgumentNullException(nameof(getConnectedAccessToken));
        _pickerFlow = pickerFlow ?? throw new ArgumentNullException(nameof(pickerFlow));
        _clientFactory = clientFactory ?? (token => new GoogleDrivePickerFileClient(token));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _expiryTimer = new Timer(
            _ => RemoveExpiredSessions(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public async Task<IReadOnlyList<GoogleDrivePickerFile>> SelectFilesAsync(
        string connectedInstanceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectedInstanceName);
        var config = await _configStore.ReadAsync("gdrive", cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            throw new GoogleDrivePickerException("Google Drive is not registered. Check the Cloud connection setup.");
        }

        var connectedAccessToken = await _getConnectedAccessToken(
            config, connectedInstanceName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(connectedAccessToken))
        {
            throw new GoogleDrivePickerException("The selected Google Drive connection has no usable access token.");
        }

        var pickerGrant = await _pickerFlow.AuthorizeAsync(config, cancellationToken).ConfigureAwait(false);
        if (pickerGrant.ExpiresAtUtc <= _utcNow())
        {
            throw new GoogleDrivePickerException("Google Picker authorization expired. Choose the file again.");
        }

        using (var connectedClient = _clientFactory(connectedAccessToken))
        using (var pickerClient = _clientFactory(pickerGrant.AccessToken))
        {
            string? connectedPermissionId;
            string? pickerPermissionId;
            try
            {
                connectedPermissionId = await connectedClient
                    .GetCurrentUserPermissionIdAsync(cancellationToken)
                    .ConfigureAwait(false);
                pickerPermissionId = await pickerClient
                    .GetCurrentUserPermissionIdAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is GoogleApiException or HttpRequestException)
            {
                throw new GoogleDrivePickerException(
                    "Google Drive could not verify the connected account identity. Check the connection and try again.");
            }

            if (string.IsNullOrWhiteSpace(connectedPermissionId)
                || string.IsNullOrWhiteSpace(pickerPermissionId)
                || !string.Equals(connectedPermissionId, pickerPermissionId, StringComparison.Ordinal))
            {
                throw new GoogleDrivePickerException(
                    "The Google account selected in Picker does not match this Cloud connection, or Google did not return a verifiable account identity.");
            }
        }

        RemoveExpiredSessions();
        var results = new List<GoogleDrivePickerFile>(pickerGrant.FileIds.Count);
        var createdHandles = new List<string>();
        try
        {
            foreach (var fileId in pickerGrant.FileIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var client = _clientFactory(pickerGrant.AccessToken);
                    var metadata = await client.GetMetadataAsync(fileId, cancellationToken)
                        .ConfigureAwait(false);
                    if (metadata.CanDownload != true)
                    {
                        results.Add(new GoogleDrivePickerFile(
                            string.Empty,
                            SafeFileName(metadata.Name, fileId),
                            string.Empty,
                            metadata.MimeType,
                            metadata.Size ?? 0,
                            "Google Drive does not allow this file to be downloaded."));
                        continue;
                    }

                    if (!TryResolveOutput(
                        metadata.MimeType, metadata.Name, out var extension, out var exportMime))
                    {
                        results.Add(new GoogleDrivePickerFile(
                            string.Empty,
                            SafeFileName(metadata.Name, fileId),
                            string.Empty,
                            metadata.MimeType,
                            metadata.Size ?? 0,
                            "This Google Drive file type is not supported for download."));
                        continue;
                    }

                    var fileName = AddExtension(metadata.Name, extension, exportMime is not null);
                    var handle = Guid.NewGuid().ToString("N");
                    var expiresAt = Min(pickerGrant.ExpiresAtUtc, _utcNow().Add(SessionLifetime));
                    _pending[handle] = new PendingDownload(
                        pickerGrant.AccessToken,
                        fileId,
                        fileName,
                        extension,
                        exportMime,
                        metadata.Size,
                        expiresAt);
                    createdHandles.Add(handle);
                    results.Add(new GoogleDrivePickerFile(
                        handle,
                        fileName,
                        extension,
                        metadata.MimeType,
                        metadata.Size ?? 0,
                        null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (GoogleApiException ex)
                {
                    results.Add(new GoogleDrivePickerFile(
                        string.Empty,
                        "Selected Drive file",
                        string.Empty,
                        null,
                        0,
                        ApiFailure(ex)));
                }
                catch (HttpRequestException)
                {
                    results.Add(new GoogleDrivePickerFile(
                        string.Empty,
                        "Selected Drive file",
                        string.Empty,
                        null,
                        0,
                        "Google Drive could not reach the service for this file. Check your connection and try again."));
                }
            }
        }
        catch
        {
            DiscardDownloadHandles(createdHandles);
            throw;
        }

        return results;
    }

    public async Task DownloadAsync(
        string downloadHandle,
        Stream destination,
        IProgress<GoogleDriveDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadHandle);
        ArgumentNullException.ThrowIfNull(destination);
        if (!_pending.TryRemove(downloadHandle, out var pending))
        {
            throw new GoogleDrivePickerException("This selected-file download has expired or was already used. Choose the file again.");
        }
        if (pending.ExpiresAtUtc <= _utcNow())
        {
            throw new GoogleDrivePickerException("This selected-file download has expired. Choose the file again.");
        }

        try
        {
            using var client = _clientFactory(pending.AccessToken);
            var currentMetadata = await client.GetMetadataAsync(pending.FileId, cancellationToken)
                .ConfigureAwait(false);
            if (currentMetadata.CanDownload != true)
            {
                throw new GoogleDrivePickerException("Google Drive no longer allows this file to be downloaded.");
            }

            using var counting = new ProgressWriteStream(destination, written =>
                progress?.Report(new GoogleDriveDownloadProgress(
                    pending.FileName,
                    written,
                    pending.SizeBytes)));

            if (pending.ExportMimeType is null)
            {
                await client.DownloadAsync(pending.FileId, counting, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await client.ExportAsync(
                    pending.FileId, pending.ExportMimeType, counting, cancellationToken)
                    .ConfigureAwait(false);
            }
            await counting.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GoogleApiException ex)
        {
            throw new GoogleDrivePickerException(ApiFailure(ex));
        }
        catch (HttpRequestException)
        {
            throw new GoogleDrivePickerException(
                "Google Drive could not reach the service for this file. Check your connection and try again.");
        }
    }

    public void DiscardDownloadHandles(IEnumerable<string> downloadHandles)
    {
        ArgumentNullException.ThrowIfNull(downloadHandles);
        foreach (var handle in downloadHandles)
        {
            if (!string.IsNullOrWhiteSpace(handle))
            {
                _pending.TryRemove(handle, out _);
            }
        }
    }

    public void Dispose()
    {
        _expiryTimer.Dispose();
        _pending.Clear();
    }

    private void RemoveExpiredSessions()
    {
        var now = _utcNow();
        foreach (var pair in _pending)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _pending.TryRemove(pair.Key, out _);
            }
        }
    }

    private static bool TryResolveOutput(
        string? mimeType,
        string? name,
        out string extension,
        out string? exportMime)
    {
        exportMime = null;
        switch (mimeType)
        {
            case DocsMime:
                extension = ".pdf";
                exportMime = DocsExportMime;
                return true;
            case SheetsMime:
                extension = ".xlsx";
                exportMime = SheetsExportMime;
                return true;
            case SlidesMime:
                extension = ".pptx";
                exportMime = SlidesExportMime;
                return true;
            default:
                if (mimeType?.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) == true)
                {
                    extension = string.Empty;
                    return false;
                }

                extension = Path.GetExtension(SafeFileName(name, "Drive file"));
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ExtensionFromMimeType(mimeType);
                }
                return true;
        }
    }

    private static string ExtensionFromMimeType(string? mimeType) => mimeType switch
    {
        "application/pdf" => ".pdf",
        "application/zip" => ".zip",
        "application/json" => ".json",
        "text/plain" => ".txt",
        "text/csv" => ".csv",
        "text/html" => ".html",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/svg+xml" => ".svg",
        "audio/mpeg" => ".mp3",
        "video/mp4" => ".mp4",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        _ => ".bin",
    };

    private static string SafeFileName(string? name, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(name) ? fallback : name;
        var invalid = Path.GetInvalidFileNameChars();
        var chars = candidate.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "Drive file" : safe;
    }

    private static string AddExtension(string? name, string extension, bool forceExtension)
    {
        var safe = SafeFileName(name, "Drive file");
        if (!forceExtension && Path.HasExtension(safe))
        {
            return safe;
        }
        if (forceExtension)
        {
            return safe.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? safe
                : safe + extension;
        }
        return safe + extension;
    }

    private static string ApiFailure(GoogleApiException ex) => ex.HttpStatusCode switch
    {
        HttpStatusCode.Forbidden => "Google Drive denied access to this file. Check its sharing and download permissions.",
        HttpStatusCode.NotFound => "The selected Google Drive file is no longer available.",
        HttpStatusCode.TooManyRequests => "Google Drive is temporarily rate limiting requests. Try again shortly.",
        _ => "Google Drive could not complete this file request (HTTP "
            + ((int)ex.HttpStatusCode).ToString(CultureInfo.InvariantCulture) + ").",
    };

    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;

    private sealed record PendingDownload(
        string AccessToken,
        string FileId,
        string FileName,
        string Extension,
        string? ExportMimeType,
        long? SizeBytes,
        DateTime ExpiresAtUtc);

    private sealed class ProgressWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long> _report;
        private long _written;

        internal ProgressWriteStream(Stream inner, Action<long> report)
        {
            _inner = inner;
            _report = report;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            Report(count);
        }
        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            Report(buffer.Length);
        }
        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            Report(count);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
            }
            base.Dispose(disposing);
        }
        private void Report(int count)
        {
            _written += count;
            _report(_written);
        }
    }
}
