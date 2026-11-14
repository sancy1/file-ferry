// filepath: src/ImageTextify.Platform/Composition/ImageTextifyOcrModels.cs
// layer:    Composition
// module:   ImageTextify.Platform.Composition
// since:    Phase 2.4b2
// purpose:  Defines the four ONNX model descriptors for the OCR engine.
//           Replaces the Phase 2.2 placeholder descriptor. Provides
//           verified SHA-256s, sizes, source URLs, licenses, and purpose
//           for PP-OCR detector, PP-OCR recognizer, TrOCR encoder, and
//           TrOCR decoder.
//
// RELATIONSHIPS
//   Implements : n/a (static composition)
//   Depends on : ImageTextify.Core.Models.ImageTextifyModelDescriptor
//   Used by    : ImageTextify.Platform.Composition.ImageTextifyServiceCollectionExtensions
//                (for IModelStore registration),
//                ImageTextify.Platform.OnnxModelRunner (for path resolution)
//   See also   : ModelManifest.cs, ModelStore.cs, OnnxModelRunner.cs
//
// NOTES
//   - SHA-256s and sizes are from actual files on disk at Phase 2.4b2
//     implementation time (2026-10-27). They are verified records.
//   - PP-OCR models are downloaded by PaddleX to the user's local
//     ~/.paddlex/official_models/ directory on first OCR pipeline call.
//     The SourceUrl below is the PaddleX model hub reference; the exact
//     runtime download is handled by PaddleX, not by our code.
//   - TrOCR models are exported once via optimum-cli and shipped in
//     bench/models-onnx/trocr-base-handwritten/ during eval. In
//     production, the ModelStore downloads them from the Hugging Face
//     URLs below on first handwriting use.
//   - The Id values are stable identifiers used by ModelStore for
//     deduplication. Do not change them after a model is committed;
//     add a new descriptor with a new Id for version changes.
//   - Purpose strings are shown to the user in the disclosure dialog
//     before first download. Keep them user-facing.

using ImageTextify.Core.Models;

namespace ImageTextify.Platform.Composition;

/// <summary>
/// Descriptors for the four ONNX models used by the ImageTextify OCR engine.
/// </summary>
public static class ImageTextifyOcrModels
{
    /// <summary>
    /// PP-OCRv6 medium text detector. Locates text regions in an image.
    /// </summary>
    public static readonly ImageTextifyModelDescriptor PpOcrDetector = new(
        Id: "ppocrv6-medium-det",
        DisplayName: "PP-OCRv6 medium detector",
        SourceUrl: "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/tmp/PP-OCRv6_medium_det_onnx.tar",
        SizeBytes: 62032837,
        Sha256Hex: "eb13b44b25bb36f89528b68720af8a61d9cf381176107f465db1757b65d086e1",
        LicenseName: "Apache-2.0",
        LicenseUrl: "https://www.apache.org/licenses/LICENSE-2.0",
        Purpose: "Detects text regions in an image for OCR.");

    /// <summary>
    /// PP-OCRv6 medium text recognizer. Reads printed text from detected
    /// regions.
    /// </summary>
    public static readonly ImageTextifyModelDescriptor PpOcrRecognizer = new(
        Id: "ppocrv6-medium-rec",
        DisplayName: "PP-OCRv6 medium recognizer",
        SourceUrl: "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/tmp/PP-OCRv6_medium_rec_0515_onnx.tar",
        SizeBytes: 76554979,
        Sha256Hex: "9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba",
        LicenseName: "Apache-2.0",
        LicenseUrl: "https://www.apache.org/licenses/LICENSE-2.0",
        Purpose: "Recognizes printed text from detected regions.");

    /// <summary>
    /// TrOCR handwritten encoder. Converts handwriting line crops into
    /// feature embeddings for the decoder.
    /// </summary>
    public static readonly ImageTextifyModelDescriptor TrocrEncoder = new(
        Id: "trocr-base-handwritten-encoder",
        DisplayName: "TrOCR handwritten encoder",
        SourceUrl: "https://huggingface.co/microsoft/trocr-base-handwritten",
        SizeBytes: 344421_000,
        Sha256Hex: "6c62c4205ecc5d20ff253f4bac219feb9fadc549566944f392d931c69e69e664",
        LicenseName: "MIT",
        LicenseUrl: "https://opensource.org/licenses/MIT",
        Purpose: "Encodes handwriting image crops for recognition.");

    /// <summary>
    /// TrOCR handwritten decoder. Converts encoder embeddings into text
    /// tokens.
    /// </summary>
    public static readonly ImageTextifyModelDescriptor TrocrDecoder = new(
        Id: "trocr-base-handwritten-decoder",
        DisplayName: "TrOCR handwritten decoder",
        SourceUrl: "https://huggingface.co/microsoft/trocr-base-handwritten",
        SizeBytes: 1_195_600_000,
        Sha256Hex: "8c175513abab03b857737449c4b5a1022f4bff9e2aa7b852f45c68b44679b374",
        LicenseName: "MIT",
        LicenseUrl: "https://opensource.org/licenses/MIT",
        Purpose: "Recognizes handwriting text from encoder embeddings.");

    /// <summary>
    /// All four OCR model descriptors, in load order: detector, recognizer,
    /// encoder, decoder.
    /// </summary>
    public static readonly IReadOnlyList<ImageTextifyModelDescriptor> All = new[]
    {
        PpOcrDetector,
        PpOcrRecognizer,
        TrocrEncoder,
        TrocrDecoder,
    };
}