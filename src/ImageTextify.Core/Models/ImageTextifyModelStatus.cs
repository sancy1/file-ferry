// ============================================================================
// FILE:     src/ImageTextify.Core/Models/ImageTextifyModelStatus.cs
// PURPOSE:  The status of a model on the local machine.
// RELATION: Returned by IModelStore.GetStatusAsync.
// ============================================================================

namespace ImageTextify.Core.Models;

/// <summary>
/// The status of a model on the local machine.
/// </summary>
public enum ImageTextifyModelStatus
{
    /// <summary>No file exists for the model.</summary>
    NotDownloaded = 0,

    /// <summary>The file exists and its SHA-256 matches the descriptor.</summary>
    Downloaded = 1,

    /// <summary>
    /// The file exists but its SHA-256 does not match the descriptor. The
    /// file is untrusted and must not be used.
    /// </summary>
    Corrupted = 2,

    /// <summary>
    /// The status could not be determined: the descriptor is invalid, or
    /// the local filesystem could not be read safely.
    /// </summary>
    Unavailable = 3,
}