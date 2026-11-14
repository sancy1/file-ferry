// ============================================================================
// FILE:     src/ImageTextify.Platform/Models/ModelManifest.cs
// PURPOSE:  The approved ImageTextify model set. Descriptors are declared
//           in code so a runtime configuration file cannot point the store
//           at an attacker-controlled URL. Values are placeholders in
//           Phase 2.2; the real URL, size, and SHA-256 are verified and
//           committed in Phase 2.4 before the OCR engine consumes the model.
// RELATION: Consumed by ModelStore and, in Phase 2.4, by the OCR engine.
// ============================================================================

using ImageTextify.Core.Models;

namespace ImageTextify.Platform.Models;

/// <summary>
/// The approved ImageTextify model set.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placeholder state in Phase 2.2.</b> The descriptor below declares a
/// URL using the reserved <c>.invalid</c> TLD, a positive placeholder size,
/// and an all-zero SHA-256. No code path may treat this descriptor as
/// usable for a real download. A test asserts the placeholder state.
/// </para>
/// <para>
/// <b>Phase 2.4 commit.</b> The real source URL, exact artifact size,
/// SHA-256, license text, publisher provenance, verification date, and the
/// human who verified the artifact are committed here, alongside a finding
/// (ImageTextify-052) that records the verification. At that point the
/// placeholder test is removed.
/// </para>
/// </remarks>
public static class ModelManifest
{
    /// <summary>
    /// The first OCR model ImageTextify supports. Phase 2.2 placeholder;
    /// the URL, size, and SHA-256 are verified against the publisher
    /// artifact in Phase 2.4 before this descriptor is used.
    /// </summary>
    public static readonly ImageTextifyModelDescriptor PaddleOcrV1 = new(
        Id: "paddleocr-v1",
        DisplayName: "PaddleOCR v1",
        SourceUrl: "https://placeholder.invalid/paddleocr-v1.onnx",
        SizeBytes: 1L,
        Sha256Hex: "0000000000000000000000000000000000000000000000000000000000000000",
        LicenseName: "Apache-2.0",
        LicenseUrl: "https://www.apache.org/licenses/LICENSE-2.0",
        Purpose: "Optical character recognition for printed text.");

    /// <summary>
    /// Whisper small (GGML) speech-to-text model.
    /// </summary>
    /// <remarks>
    /// Phase 5 Block 5.D.1. SHA-256 and size verified against the model
    /// file downloaded during Gate 0.B (2026-11-08). The URL is the
    /// canonical ggerganov/whisper.cpp Hugging Face path.
    /// </remarks>
    public static readonly ImageTextifyModelDescriptor WhisperSmallGgml = new(
        Id: "whisper-small-ggml",
        DisplayName: "Whisper Small (GGML)",
        SourceUrl: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
        SizeBytes: 487601967L,
        Sha256Hex: "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b",
        LicenseName: "MIT",
        LicenseUrl: "https://github.com/ggerganov/whisper.cpp/blob/master/LICENSE",
        Purpose: "Speech-to-text transcription.");

    /// <summary>
    /// Silero VAD (GGML) voice activity detection model.
    /// </summary>
    /// <remarks>
    /// Phase 5 Block 5.D.1. SHA-256 and size verified against the model
    /// file downloaded during Gate 0.B (2026-11-08).
    /// UNVERIFIED: the SourceUrl filename (ggml-silero-v6.2.0.bin) is
    /// inferred from a whisper.rn example, not from the Whisper.net
    /// downloader. First production download confirms or refutes it.
    /// </remarks>
    public static readonly ImageTextifyModelDescriptor SileroVadGgml = new(
        Id: "silero-vad-ggml",
        DisplayName: "Silero VAD (GGML)",
        SourceUrl: "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v6.2.0.bin",
        SizeBytes: 885098L,
        Sha256Hex: "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987",
        LicenseName: "MIT",
        LicenseUrl: "https://github.com/ggerganov/whisper.cpp/blob/master/LICENSE",
        Purpose: "Voice activity detection for speech segmentation.");

    /// <summary>
    /// Every model in the manifest, in the order they appear above. Used by
    /// later phases to iterate the approved set.
    /// </summary>
    public static readonly IReadOnlyList<ImageTextifyModelDescriptor> All = new[]
    {
        PaddleOcrV1,
        WhisperSmallGgml,
        SileroVadGgml,
    };
}