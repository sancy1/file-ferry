using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Providers;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;

namespace FileHarvester.Platform.Cloud.Services;

internal sealed record GoogleDrivePickerFileMetadata(
    string? Name,
    string? MimeType,
    long? Size,
    bool? CanDownload);

internal interface IGoogleDrivePickerFileClient : IDisposable
{
    Task<string?> GetCurrentUserPermissionIdAsync(CancellationToken ct);
    Task<GoogleDrivePickerFileMetadata> GetMetadataAsync(string fileId, CancellationToken ct);
    Task DownloadAsync(string fileId, Stream destination, CancellationToken ct);
    Task ExportAsync(string fileId, string mimeType, Stream destination, CancellationToken ct);
}

internal sealed class GoogleDrivePickerFileClient : IGoogleDrivePickerFileClient
{
    private readonly DriveService _service;

    internal GoogleDrivePickerFileClient(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var credential = GoogleCredential.FromAccessToken(
            accessToken,
            new BearerToken.AuthorizationHeaderAccessMethod());
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "File-Ferry",
        });
    }

    public async Task<string?> GetCurrentUserPermissionIdAsync(CancellationToken ct)
    {
        var request = _service.About.Get();
        request.Fields = "user(permissionId)";
        var about = await request
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        return about.User?.PermissionId;
    }

    public async Task<GoogleDrivePickerFileMetadata> GetMetadataAsync(
        string fileId,
        CancellationToken ct)
    {
        var request = _service.Files.Get(fileId);
        request.Fields = "id,name,mimeType,size,capabilities(canDownload)";
        var file = await request.ExecuteAsync(ct).ConfigureAwait(false);
        return new GoogleDrivePickerFileMetadata(
            file.Name,
            file.MimeType,
            file.Size,
            file.Capabilities?.CanDownload);
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken ct)
    {
        await _service.Files.Get(fileId)
            .DownloadAsync(destination, ct)
            .ConfigureAwait(false);
    }

    public async Task ExportAsync(
        string fileId,
        string mimeType,
        Stream destination,
        CancellationToken ct)
    {
        await _service.Files.Export(fileId, mimeType)
            .DownloadAsync(destination, ct)
            .ConfigureAwait(false);
    }

    public void Dispose() => _service.Dispose();
}
