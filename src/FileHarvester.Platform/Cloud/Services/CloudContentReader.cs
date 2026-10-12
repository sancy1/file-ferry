// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/CloudContentReader.cs
// PURPOSE:  ICloudContentReader implementation. Reads bytes from a cloud
//           provider and produces FileContent records. Non-fail-fast:
//           a single unreadable file produces a placeholder, not a
//           throw. Mirrors ContentReader.
// RELATION: Implements ICloudContentReader. Consumed by
//           DispatchingContentReader. Uses EncodingDetector from Core.
//           Provider selection is by scheme and optional instance, via an
//           async factory injected at construction.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using Portfolio.Resilience.Errors;

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// Reads cloud-resolved files into FileContent records.
/// </summary>
public sealed class CloudContentReader : ICloudContentReader
{
    private readonly Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>> _providerFactory;

    /// <summary>Creates a reader.</summary>
    /// <param name="providerFactory">
    /// Resolves the provider for a scheme and optional instance. Never null.
    /// </param>
    public CloudContentReader(
        Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>> providerFactory)
    {
        _providerFactory = providerFactory ??
            throw new ArgumentNullException(nameof(providerFactory));
    }

    /// <inheritdoc/>
    public async Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        PathSchemeInfo scheme;
        try
        {
            scheme = PathScheme.Parse(file.AbsolutePath);
        }
        catch (FormatException)
        {
            return FileContent.Unreadable(
                file,
                "Malformed cloud path: " + file.AbsolutePath);
        }

        if (scheme.Kind == PathSchemeKind.Local)
        {
            return FileContent.Unreadable(
                file,
                "Not a cloud path: " + file.AbsolutePath);
        }

        var resolution = await _providerFactory(
            scheme.Kind, scheme.Instance, cancellationToken).ConfigureAwait(false);

        if (!resolution.IsResolved)
        {
            return FileContent.Unreadable(
                file,
                CloudFileResolver.BuildFailureNote(scheme, resolution.Failure));
        }

        var provider = resolution.Provider!;

        try
        {
            await using var stream = await provider.OpenReadAsync(
                file.AbsolutePath, cancellationToken).ConfigureAwait(false);

            var bytes = await ReadAllAsync(stream, cancellationToken)
                .ConfigureAwait(false);

            var decoded = EncodingDetector.Decode(bytes);
            var sha256 = ComputeSha256(bytes);

            return new FileContent(
                File: file,
                RawBytes: bytes,
                DecodedText: decoded.Text,
                Encoding: decoded.Encoding,
                IsBinary: decoded.IsBinary,
                Sha256: sha256);
        }
        catch (ResilienceException ex)
        {
            return FileContent.Unreadable(
                file,
                ex.Category + ": " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileContent>> ReadManyAsync(
        IReadOnlyList<ResolvedFile> files,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return Array.Empty<FileContent>();
        }

        var results = new List<FileContent>(files.Count);
        var processed = 0;
        var bytesRead = 0L;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await ReadAsync(file, cancellationToken)
                .ConfigureAwait(false);

            results.Add(content);
            processed++;
            bytesRead += content.RawBytes.Length;

            progress?.Report(new ReadProgress(
                Index: processed - 1,
                Total: files.Count,
                Processed: processed,
                BytesRead: bytesRead,
                Status: content.IsUnreadable
                    ? FileStatus.ReadError
                    : FileStatus.Found));
        }

        return results;
    }

    private static async Task<byte[]> ReadAllAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            var length = (int)stream.Length;
            var buffer = new byte[length];
            await stream.ReadExactlyAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            return buffer;
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
