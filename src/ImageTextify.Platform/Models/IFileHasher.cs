// ============================================================================
// FILE:     src/ImageTextify.Platform/Models/IFileHasher.cs
// PURPOSE:  Internal seam for computing the SHA-256 of a file. Exists so
//           ModelStore can be tested deterministically for IOException and
//           UnauthorizedAccessException without manipulating the filesystem
//           or platform permissions.
// RELATION: Consumed by ModelStore. Implemented by Sha256FileHasher.
//           Visible to FileHarvester.Tests via InternalsVisibleTo.
// ============================================================================

namespace ImageTextify.Platform.Models;

/// <summary>
/// Computes the SHA-256 of a file.
/// </summary>
/// <remarks>
/// <para>
/// Internal seam. The interface exists so the store's failure paths
/// (<see cref="System.IO.IOException"/>, <see cref="System.UnauthorizedAccessException"/>)
/// can be exercised deterministically in tests without relying on
/// platform-specific file locking or permission manipulation.
/// </para>
/// <para>
/// The single implementation, <see cref="Sha256FileHasher"/>, is stateless
/// and thread-safe.
/// </para>
/// </remarks>
internal interface IFileHasher
{
    /// <summary>
    /// Computes the SHA-256 of the file at the supplied path and returns it
    /// as 64 lowercase hexadecimal characters.
    /// </summary>
    /// <param name="path">
    /// The absolute path of the file. Not null or empty.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the hashing. The hasher honors the token.
    /// </param>
    /// <returns>
    /// The SHA-256 as 64 lowercase hexadecimal characters.
    /// </returns>
    /// <exception cref="System.IO.IOException">
    /// Thrown when the file cannot be read due to an I/O failure, for
    /// example a sharing violation.
    /// </exception>
    /// <exception cref="System.UnauthorizedAccessException">
    /// Thrown when the file cannot be read due to insufficient permissions.
    /// </exception>
    Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken);
}