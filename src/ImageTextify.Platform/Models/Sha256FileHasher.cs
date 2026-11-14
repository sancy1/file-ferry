// ============================================================================
// FILE:     src/ImageTextify.Platform/Models/Sha256FileHasher.cs
// PURPOSE:  The default IFileHasher. Streams the file through an
//           IncrementalHash so the whole file is never buffered in memory.
// RELATION: Implements IFileHasher. Registered as a singleton in
//           AddImageTextify.
// ============================================================================

using System.Security.Cryptography;

namespace ImageTextify.Platform.Models;

/// <summary>
/// Computes a file's SHA-256 using bounded streaming.
/// </summary>
/// <remarks>
/// Stateless and thread-safe. Never buffers the whole file in memory;
/// reads in 64 KiB chunks and feeds each chunk to
/// <see cref="IncrementalHash"/>.
/// </remarks>
internal sealed class Sha256FileHasher : IFileHasher
{
    private const int ReadBufferSize = 64 * 1024;

    /// <inheritdoc/>
    public async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferSize,
            useAsync: true);

        var buffer = new byte[ReadBufferSize];
        int read;

        while ((read = await stream
            .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
            .ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}