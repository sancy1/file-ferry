// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/ModelDownloadException.cs
// PURPOSE:  The exception thrown when a model download fails, the checksum
//           does not match, or the model descriptor is invalid.
// RELATION: Thrown by IModelStore.EnsureAvailableAsync.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Thrown when a model cannot be made available locally.
/// </summary>
public sealed class ModelDownloadException : Exception
{
    /// <summary>
    /// Creates the exception with a default message.
    /// </summary>
    public ModelDownloadException()
        : base("The model could not be downloaded or verified.")
    {
    }

    /// <summary>
    /// Creates the exception with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public ModelDownloadException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ModelDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}