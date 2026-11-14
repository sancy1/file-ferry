// filepath: src/ImageTextify.Core/Domain/OcrLineProvenance.cs
// layer:    Contracts
// module:   ImageTextify.Core.Domain
// since:    1.2.0
// purpose:  Identifies which recognizer produced a given OCR output line
//           so the UI and export can show per-line provenance

namespace ImageTextify.Core.Domain;

/// <summary>
/// Provenance of a single OCR output line: which recognizer produced it.
/// </summary>
/// <remarks>
/// Carried per line from the engine to the UI. Consumers render
/// <see cref="Ppocr"/> and <see cref="Qwen"/> distinctly. This enum does
/// not persist; if a future phase serializes <c>OcrResult</c> to disk,
/// the numeric values below become a wire contract and must not change.
/// NOT responsible for: deciding which recognizer to call; that is the
/// engine's responsibility.
/// </remarks>
public enum OcrLineProvenance
{
    /// <summary>Produced by PP-OCR. The default for printed text.</summary>
    Ppocr = 0,

    /// <summary>Produced by the Qwen printed-text fallback.</summary>
    Qwen = 1
}
