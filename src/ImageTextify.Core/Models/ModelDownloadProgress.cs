// ============================================================================
// FILE:     src/ImageTextify.Core/Models/ModelDownloadProgress.cs
// PURPOSE:  The progress of an in-flight model download. Reported through
//           IProgress<ModelDownloadProgress>.
// RELATION: Reported by IModelStore.EnsureAvailableAsync.
// ============================================================================

namespace ImageTextify.Core.Models;

/// <summary>
/// The progress of an in-flight model download.
/// </summary>
/// <param name="BytesReceived">
/// The number of bytes received so far. Non-negative.
/// </param>
/// <param name="TotalBytes">
/// The total number of bytes expected, when the server provided a
/// Content-Length header. Null when the server did not provide it. Never
/// fabricated.
/// </param>
/// <param name="PercentComplete">
/// The completion percentage in the range zero to one hundred, computed
/// from <see cref="BytesReceived"/> and <see cref="TotalBytes"/>. Null when
/// <see cref="TotalBytes"/> is null. Never fabricated.
/// </param>
public sealed record ModelDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double? PercentComplete);