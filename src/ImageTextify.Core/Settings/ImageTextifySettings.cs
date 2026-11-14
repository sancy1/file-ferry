// filepath: src/ImageTextify.Core/Settings/ImageTextifySettings.cs
// layer:    Domain
// module:   ImageTextify.Core.Settings
// since:    1.2.0
// purpose:  Immutable default implementation of IImageTextifySettings

namespace ImageTextify.Core.Settings;

/// <summary>
/// Immutable implementation of <see cref="IImageTextifySettings"/>. Safe
/// for concurrent reads. No mutation after construction.
/// </summary>
public sealed record ImageTextifySettings : IImageTextifySettings
{
    /// <summary>
    /// Default confidence threshold for low-confidence printed lines:
    /// 0.3, matching the Phase 2.4e design.
    /// </summary>
    public const double DefaultLowConfidenceThreshold = 0.3;

    /// <summary>
    /// Default settings: Qwen printed fallback off, threshold 0.3.
    /// PP-OCR is the default printed-text recognizer.
    /// </summary>
    public static ImageTextifySettings Default { get; } = new();

    /// <inheritdoc />
    public bool EnableQwenPrintedFallback { get; init; }

    /// <inheritdoc />
    public double LowConfidenceThreshold { get; init; } = DefaultLowConfidenceThreshold;

    /// <summary>
    /// Initializes a new instance with default values: fallback disabled,
    /// threshold 0.3.
    /// </summary>
    public ImageTextifySettings() { }

    /// <summary>
    /// Initializes a new instance with explicit values.
    /// </summary>
    /// <param name="enableQwenPrintedFallback">
    /// When true, enables the Qwen printed fallback.
    /// </param>
    /// <param name="lowConfidenceThreshold">
    /// PP-OCR confidence (0.0 to 1.0 inclusive) below which a printed line
    /// is eligible for Qwen fallback. Must be in [0, 1].
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="lowConfidenceThreshold"/> is NaN, less
    /// than 0.0, or greater than 1.0.
    /// </exception>
    public ImageTextifySettings(
        bool enableQwenPrintedFallback,
        double lowConfidenceThreshold = DefaultLowConfidenceThreshold)
    {
        if (double.IsNaN(lowConfidenceThreshold)
            || lowConfidenceThreshold < 0.0
            || lowConfidenceThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowConfidenceThreshold),
                lowConfidenceThreshold,
                "Must be in [0, 1].");
        }
        EnableQwenPrintedFallback = enableQwenPrintedFallback;
        LowConfidenceThreshold = lowConfidenceThreshold;
    }
}
