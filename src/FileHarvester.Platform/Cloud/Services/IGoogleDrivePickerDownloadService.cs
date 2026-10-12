using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Services;

public sealed record GoogleDrivePickerFile(
    string DownloadHandle,
    string FileName,
    string Extension,
    string? MimeType,
    long SizeBytes,
    string? Error);

public sealed record GoogleDriveDownloadProgress(
    string FileName,
    long BytesWritten,
    long? TotalBytes);

public sealed class GoogleDrivePickerException : InvalidOperationException
{
    public GoogleDrivePickerException(string message)
        : base(message)
    {
    }
}

public interface IGoogleDrivePickerDownloadService
{
    Task<IReadOnlyList<GoogleDrivePickerFile>> SelectFilesAsync(
        string connectedInstanceName,
        CancellationToken cancellationToken = default);

    Task DownloadAsync(
        string downloadHandle,
        Stream destination,
        IProgress<GoogleDriveDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    void DiscardDownloadHandles(IEnumerable<string> downloadHandles);
}
