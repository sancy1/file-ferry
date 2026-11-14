// filepath: src/ImageTextify.Core/Settings/IImageTextifySettings.cs
// layer:    Contracts
// module:   ImageTextify.Core.Settings
// since:    1.2.0
// purpose:  Read-only snapshot of user-configurable ImageTextify settings

namespace ImageTextify.Core.Settings;

/// <summary>
/// Read-only snapshot of user-configurable settings for the ImageTextify
/// OCR pipeline. Consumers hold a snapshot for the duration of one operation
/// so that a setting change mid-run cannot produce mixed results.
/// </summary>
/// <remarks>
/// Assumptions about callers: read-only access; no getter blocks.
/// Thread-safety: snapshots are immutable; concurrent reads are safe.
/// This type is NOT the mutable surface; see
/// <see cref="IImageTextifySettingsStore"/> for the set operation.
/// </remarks>
public interface IImageTextifySettings
{
    /// <summary>
    /// When true, printed lines whose PP-OCR confidence is below
    /// <see cref="LowConfidenceThreshold"/> are re-run through the Qwen
    /// printed-text fallback. Default is false: PP-OCR remains the only
    /// printed-text recognizer unless the user opts in.
    /// </summary>
    bool EnableQwenPrintedFallback { get; }

    /// <summary>
    /// PP-OCR confidence (0.0 to 1.0 inclusive) below which a printed line
    /// is considered low confidence and is eligible for Qwen fallback when
    /// <see cref="EnableQwenPrintedFallback"/> is true.
    /// </summary>
    double LowConfidenceThreshold { get; }
}
