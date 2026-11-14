// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IModelStore.cs
// PURPOSE:  Contract for the ImageTextify model store: local status,
//           ensure-available (download if missing), and local path
//           resolution. The implementation owns the network call and the
//           checksum verification.
// RELATION: Implemented by ModelStore in ImageTextify.Platform.
//           Consumed by the OCR engine in Phase 2.4.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

using ImageTextify.Core.Models;

/// <summary>
/// Stores and retrieves ImageTextify models on the local machine.
/// </summary>
/// <remarks>
/// <para>
/// The store never uploads anything and never contacts a network endpoint
/// except the model's declared <c>SourceUrl</c>. It never accepts a URL or
/// hash supplied at runtime.
/// </para>
/// <para>
/// The store verifies the SHA-256 of every downloaded model before it is
/// used. A partial download is written with a <c>.partial</c> suffix and is
/// promoted to its final name only after the checksum matches. A failure,
/// a checksum mismatch, or a cancellation deletes the partial file.
/// </para>
/// </remarks>
public interface IModelStore
{
    /// <summary>
    /// Returns the local status of the model without downloading it and
    /// without contacting the network. The status is
    /// <see cref="ImageTextifyModelStatus.Downloaded"/> only when the local
    /// file exists and its SHA-256 matches the descriptor.
    /// </summary>
    /// <param name="descriptor">The model descriptor. Not null.</param>
    /// <param name="cancellationToken">
    /// Cancels the status check. The store honors the token.
    /// </param>
    /// <returns>The local status.</returns>
    Task<ImageTextifyModelStatus> GetStatusAsync(
        ImageTextifyModelDescriptor descriptor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the model is present locally and returns its final path.
    /// Downloads and verifies the model when it is not already present and
    /// valid. A valid existing file is returned without contacting the
    /// network.
    /// </summary>
    /// <param name="descriptor">The model descriptor. Not null.</param>
    /// <param name="progress">
    /// An optional progress reporter. May be null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the download. On cancellation no partial file is left
    /// behind.
    /// </param>
    /// <returns>
    /// The absolute path to the verified model file.
    /// </returns>
    /// <exception cref="ModelDownloadException">
    /// Thrown when the download fails, the checksum does not match, or the
    /// descriptor is invalid.
    /// </exception>
    Task<string> EnsureAvailableAsync(
        ImageTextifyModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the local path where the model lives or would live. Does not
    /// check whether the file exists and does not contact the network.
    /// </summary>
    /// <param name="descriptor">The model descriptor. Not null.</param>
    /// <returns>The absolute path the store would use.</returns>
    string GetLocalPath(ImageTextifyModelDescriptor descriptor);
}