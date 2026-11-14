// ============================================================================
// FILE:     src/ImageTextify.Platform/Models/ModelStore.cs
// PURPOSE:  Downloads, verifies, stores, and reports status for ImageTextify
//           models. Never contacts any URL except the one declared in the
//           descriptor. Never accepts a runtime-supplied URL or hash.
//           Uses a named IHttpClientFactory client, streaming SHA-256
//           hashing, and .partial-then-atomic-rename promotion.
// RELATION: Implements IModelStore (ImageTextify.Core.Abstractions).
//           Registered as a singleton in AddImageTextify.
//           Depends on IFileHasher for the SHA-256 computation so the
//           failure paths are deterministic in tests.
// ============================================================================

using System.Net.Http;
using System.Security.Cryptography;
using FileHarvester.Core.Composition;
using ImageTextify.Platform.Composition;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Models;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace ImageTextify.Platform.Models;

/// <summary>
/// The default <see cref="IModelStore"/>. Stateless and thread-safe; the
/// only state it holds is its injected dependencies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust boundary.</b> The store accepts only descriptors supplied by
/// the caller. It exposes no API that accepts a runtime URL or hash. The
/// descriptor's URL must use HTTPS; the descriptor's SHA-256 must be 64
/// lowercase hex characters. The hash comes from the caller's manifest,
/// never from the download source.
/// </para>
/// <para>
/// <b>Download mechanism.</b> The download runs inside the
/// image-textify-model-download Portfolio.Resilience policy. Bytes
/// are read from the response stream in bounded chunks, hashed
/// incrementally, and written to a .partial file in the same directory.
/// The partial file is promoted to its final name only after the byte
/// count and SHA-256 both match the descriptor. Any failure,
/// cancellation, or mismatch deletes the partial file and leaves the
/// destination untouched.
/// </para>
/// <para>
/// <b>HttpClient.</b> The store uses the named client
/// ImageTextifyModelDownload through an injected IHttpClientFactory. A
/// fresh HttpClient per download would risk socket exhaustion; the
/// factory manages handler lifetime.
/// </para>
/// </remarks>
public sealed class ModelStore : IModelStore
{
    private const string HttpClientName = "ImageTextifyModelDownload";
    private const string ModelFileName = "model.bin";
    private const int ReadBufferSize = 64 * 1024;

    private readonly IResilienceExecutor _resilience;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileHasher _fileHasher;
    private readonly Func<string> _modelRootDirectoryProvider;

    /// <summary>
    /// Creates the store using the production SHA-256 file hasher.
    /// </summary>
    /// <param name="resilience">The resilience executor. Not null.</param>
    /// <param name="httpClientFactory">The HTTP client factory. Not null.</param>
    /// <param name="modelRootDirectoryProvider">
    /// A delegate returning the root directory where models are stored.
    /// Not null. In production it returns the LocalApplicationData
    /// ImageTextify Models folder.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is null.
    /// </exception>
    public ModelStore(
        IResilienceExecutor resilience,
        IHttpClientFactory httpClientFactory,
        Func<string> modelRootDirectoryProvider)
        : this(resilience, httpClientFactory, new Sha256FileHasher(), modelRootDirectoryProvider)
    {
    }

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="resilience">The resilience executor. Not null.</param>
    /// <param name="httpClientFactory">The HTTP client factory. Not null.</param>
    /// <param name="fileHasher">The SHA-256 file hasher. Not null.</param>
    /// <param name="modelRootDirectoryProvider">
    /// A delegate returning the root directory where models are stored. Not
    /// null. In production it returns the LocalApplicationData ImageTextify
    /// Models folder. Tests provide a per-test temp directory.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is null.
    /// </exception>
    internal ModelStore(
        IResilienceExecutor resilience,
        IHttpClientFactory httpClientFactory,
        IFileHasher fileHasher,
        Func<string> modelRootDirectoryProvider)
    {
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _fileHasher = fileHasher ?? throw new ArgumentNullException(nameof(fileHasher));
        _modelRootDirectoryProvider = modelRootDirectoryProvider
            ?? throw new ArgumentNullException(nameof(modelRootDirectoryProvider));
    }

    /// <inheritdoc/>
    public string GetLocalPath(ImageTextifyModelDescriptor descriptor)
    {
        ValidateDescriptor(descriptor);
        return ComputeFinalPath(descriptor);
    }

    /// <inheritdoc/>
    public async Task<ImageTextifyModelStatus> GetStatusAsync(
        ImageTextifyModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateDescriptor(descriptor);
        }
        catch (ModelDownloadException)
        {
            return ImageTextifyModelStatus.Unavailable;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var finalPath = ComputeFinalPath(descriptor);
        if (!File.Exists(finalPath))
        {
            return ImageTextifyModelStatus.NotDownloaded;
        }

        try
        {
            var actualHash = await _fileHasher
                .ComputeSha256Async(finalPath, cancellationToken)
                .ConfigureAwait(false);

            return HashMatches(actualHash, descriptor.Sha256Hex)
                ? ImageTextifyModelStatus.Downloaded
                : ImageTextifyModelStatus.Corrupted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return ImageTextifyModelStatus.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return ImageTextifyModelStatus.Unavailable;
        }
    }

    /// <inheritdoc/>
    public async Task<string> EnsureAvailableAsync(
        ImageTextifyModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor);

        var finalPath = ComputeFinalPath(descriptor);

        // Fast path: a valid existing file is returned without contacting
        // the network.
        if (File.Exists(finalPath))
        {
            string existingHash;
            try
            {
                existingHash = await _fileHasher
                    .ComputeSha256Async(finalPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                existingHash = string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                existingHash = string.Empty;
            }

            if (HashMatches(existingHash, descriptor.Sha256Hex))
            {
                return finalPath;
            }
            // The existing file is corrupt or unreadable. Do not delete it
            // yet: a valid replacement has not been secured. The atomic
            // rename at the end replaces it only after verification.
        }

        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        var partialPath = finalPath + ".partial";

        try
        {
            await _resilience.ExecuteAsync(
                policyName: ImageTextifyPolicyNames.ModelDownload,
                operation: token => DownloadCoreAsync(
                    descriptor, partialPath, progress, token),
                fallback: _ => throw new ModelDownloadException(
                    "The model download circuit is open or the operation timed out."),
                ct: cancellationToken).ConfigureAwait(false);

            File.Move(partialPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ResilienceException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "The model download was cancelled.",
                ex,
                cancellationToken);
        }
        catch (ResilienceException ex)
        {
            throw new ModelDownloadException(
                "The model download failed: "
                + (ex.InnerException?.Message ?? ex.Message),
                ex);
        }
        finally
        {
            TryDeletePartial(partialPath);
        }
    }

    // ------------------------------------------------------------------
    // Download core - runs inside the resilience policy.
    // ------------------------------------------------------------------

    private async Task DownloadCoreAsync(
        ImageTextifyModelDescriptor descriptor,
        string partialPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.GetAsync(
            descriptor.SourceUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is { } declared && declared != descriptor.SizeBytes)
        {
            throw new ModelDownloadException(
                "The server reported " + declared + " bytes but the descriptor "
                + "declares " + descriptor.SizeBytes + ".");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long totalRead = 0;

        await using (var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(
            partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
            ReadBufferSize, useAsync: true))
        {
            var buffer = new byte[ReadBufferSize];
            int read;

            while ((read = await source
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                totalRead += read;

                progress?.Report(new ModelDownloadProgress(
                    BytesReceived: totalRead,
                    TotalBytes: contentLength,
                    PercentComplete: contentLength is { } total && total > 0
                        ? (double)totalRead / total * 100.0
                        : null));
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // Byte-count check. Content-Length may be absent; the received count
        // must still match the descriptor. This runs before the hash check
        // so a truncated download reports the byte mismatch, not a hash
        // mismatch, in the exception message.
        if (totalRead != descriptor.SizeBytes)
        {
            throw new ModelDownloadException(
                "The downloaded model contained " + totalRead
                + " bytes, but the descriptor declares "
                + descriptor.SizeBytes + " bytes.");
        }

        var actualHash = Convert.ToHexString(hasher.GetHashAndReset())
            .ToLowerInvariant();
        if (!HashMatches(actualHash, descriptor.Sha256Hex))
        {
            throw new ModelDownloadException(
                "The downloaded model's SHA-256 does not match the descriptor.");
        }
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private string ComputeFinalPath(ImageTextifyModelDescriptor descriptor)
    {
        var root = _modelRootDirectoryProvider();
        return Path.Combine(root, descriptor.Id, ModelFileName);
    }

    private static bool HashMatches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static void TryDeletePartial(string partialPath)
    {
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        catch
        {
            // Best-effort cleanup. A failure to delete must not mask the
            // primary exception.
        }
    }

    /// <summary>
    /// Validates the descriptor. Rejects every invalid shape before any
    /// filesystem or network access.
    /// </summary>
    /// <param name="descriptor">The descriptor to validate.</param>
    /// <exception cref="ModelDownloadException">
    /// Thrown for any invalid field.
    /// </exception>
    private static void ValidateDescriptor(ImageTextifyModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.Id)
            || !System.Text.RegularExpressions.Regex.IsMatch(
                descriptor.Id, "^[a-z0-9][a-z0-9-]*$"))
        {
            throw new ModelDownloadException(
                "Model id must match ^[a-z0-9][a-z0-9-]*$.");
        }

        if (descriptor.Id.Contains("..")
            || descriptor.Id.Contains('/')
            || descriptor.Id.Contains('\\')
            || Path.IsPathRooted(descriptor.Id))
        {
            throw new ModelDownloadException(
                "Model id must not contain path separators or traversal.");
        }

        if (!Uri.TryCreate(descriptor.SourceUrl, UriKind.Absolute, out var uri))
        {
            throw new ModelDownloadException(
                "Model source URL must be an absolute URI.");
        }

        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isAllowedLoopbackHttp = IsLoopbackHttp(uri);
        if (!isHttps && !isAllowedLoopbackHttp)
        {
            throw new ModelDownloadException(
                "Model source URL must use HTTPS, except for a loopback test server.");
        }

        if (descriptor.SizeBytes <= 0)
        {
            throw new ModelDownloadException(
                "Model size must be greater than zero.");
        }

        if (descriptor.Sha256Hex is null
            || descriptor.Sha256Hex.Length != 64
            || !descriptor.Sha256Hex.All(c =>
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
        {
            throw new ModelDownloadException(
                "Model SHA-256 must be exactly 64 lowercase hexadecimal characters.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName)
            || string.IsNullOrWhiteSpace(descriptor.LicenseName)
            || string.IsNullOrWhiteSpace(descriptor.Purpose))
        {
            throw new ModelDownloadException(
                "Model descriptor metadata fields must not be empty.");
        }

        if (!Uri.TryCreate(descriptor.LicenseUrl, UriKind.Absolute, out var licenseUri)
            || licenseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ModelDownloadException(
                "Model license URL must be an absolute HTTPS URI.");
        }
    }

    private static bool IsLoopbackHttp(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return System.Net.IPAddress.TryParse(uri.Host, out var address)
            && System.Net.IPAddress.IsLoopback(address);
    }

}
