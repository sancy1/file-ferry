// ============================================================================
// FILE:     src/FileHarvester.Core/Implementation/ContentReader.cs
// PURPOSE:  Implementation of IContentReader. Reads file bytes through the
//           "file-read" resilience policy, detects encoding, computes a
//           SHA-256 hash, and returns a FileContent record. Never throws
//           for normal I/O failures — produces an unreadable placeholder.
// RELATION: Implements IContentReader (Phase 1.0). Consumed by the
//           JobRunner (Phase 3.4) as the second stage of the harvest
//           pipeline. Depends on IResilienceExecutor and EncodingDetector.
// ============================================================================

using System.Security.Cryptography;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace FileHarvester.Core.Implementation;

/// <summary>
/// Reads file bytes and produces FileContent records.
/// </summary>
public sealed class ContentReader : IContentReader, ILocalContentReader
{
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates a reader with its resilience dependency.
    /// </summary>
    public ContentReader(IResilienceExecutor resilience)
    {
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    /// <inheritdoc/>
    public async Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            return await _resilience.ExecuteAsync(
                policyName: PolicyNames.FileRead,
                operation: token => ReadCoreAsync(file, token),
                fallback: _ => Task.FromResult(
                    FileContent.Unreadable(file, "Circuit open or timeout")),
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (ResilienceException ex)
        {
            // The pipeline reports permanent failures (FileNotFound,
            // UnauthorizedAccess, etc.) by throwing. Convert to a
            // placeholder so a single bad file cannot fail the batch.
            return FileContent.Unreadable(
                file,
                $"{ex.Category}: {ex.InnerException?.Message ?? ex.Message}");
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
                Index:     processed - 1,
                Total:     files.Count,
                Processed: processed,
                BytesRead: bytesRead,
                Status:    content.IsUnreadable
                    ? FileStatus.ReadError
                    : FileStatus.Found));
        }

        return results;
    }

    // -----------------------------------------------------------------
    // Core read — runs inside the resilience pipeline.
    // -----------------------------------------------------------------

    private static async Task<FileContent> ReadCoreAsync(
        ResolvedFile file,
        CancellationToken cancellationToken)
    {
        // FileShare.ReadWrite allows reading files that other processes
        // hold open — essential for logs, lock files, and live configs.
        await using var stream = new FileStream(
            path:         file.AbsolutePath,
            mode:         FileMode.Open,
            access:       FileAccess.Read,
            share:        FileShare.ReadWrite,
            bufferSize:   64 * 1024,
            useAsync:     true);

        var length = (int)stream.Length;
        var bytes = new byte[length];

        await stream.ReadExactlyAsync(bytes, cancellationToken)
            .ConfigureAwait(false);

        var decoded = EncodingDetector.Decode(bytes);
        var sha256 = ComputeSha256(bytes);

        return new FileContent(
            File:        file,
            RawBytes:    bytes,
            DecodedText: decoded.Text,
            Encoding:    decoded.Encoding,
            IsBinary:    decoded.IsBinary,
            Sha256:      sha256);
    }

    // -----------------------------------------------------------------
    // SHA-256 helper — lowercase hex.
    // -----------------------------------------------------------------

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
