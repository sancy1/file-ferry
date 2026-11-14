// ============================================================================
// FILE:     src/ImageTextify.Platform/Composition/ImageTextifyPolicyNames.cs
// PURPOSE:  Canonical string constants for the ImageTextify-owned
//           Portfolio.Resilience policy names. Each constant is declared here
//           in Phase 1.2 but is registered in the phase that first calls it,
//           following the smallest-safe-change rule and the SchemaLence
//           precedent of keeping feature-specific policy names with the feature.
// RELATION: Consumed by ImageTextifyServiceCollectionExtensions (later phases
//           register each policy there) and by ImageTextify call sites that
//           invoke IResilienceExecutor.ExecuteAsync. Uses the exact API shape of
//           FileHarvester.Core/Composition/PolicyNames.cs (const string).
// ============================================================================

namespace ImageTextify.Platform.Composition;

/// <summary>
/// The ImageTextify-owned Portfolio.Resilience policy names.
/// </summary>
/// <remarks>
/// <para>
/// Each constant names a policy that ImageTextify will register in the phase
/// that first needs it. Declaring the name here makes the ImageTextify
/// composition contract explicit without registering unused policies early.
/// </para>
/// <para>
/// These policies are additive to the FileHarvester baseline
/// (file-read, path-search, bundle-write, cloud-*). Existing policies are
/// reused wherever their failure profile fits; a new ImageTextify policy is
/// added only when the failure profile genuinely differs.
/// </para>
/// </remarks>
public static class ImageTextifyPolicyNames
{
    /// <summary>
    /// Long-running local OCR inference.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Image preprocessing and OCR inference.</para>
    /// <para><b>Failure profile.</b> Deterministic failures (a bad model, an
    /// unsupported image, a decoding error) are not transient. Blind retry would
    /// waste CPU on the same deterministic failure.</para>
    /// <para><b>Retry.</b> Not appropriate for the inference call itself. A
    /// bounded retry may be added only for verified transient failures, and must
    /// be justified at the call site.</para>
    /// <para><b>Timeout and cancellation.</b> A per-attempt timeout bounds
    /// runaway inference. The caller's cancellation token flows through every
    /// call and stops the work promptly without corrupting the result.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated deterministic
    /// failures so a corrupt or missing model does not spin the CPU on every
    /// request.</para>
    /// <para><b>Registered in.</b> Phase 2 (image OCR).</para>
    /// </remarks>
    public const string OcrInference = "image-textify-ocr";

    /// <summary>
    /// PDF page render and embedded-text extraction.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Rendering a PDF page to an image for OCR, and
    /// extracting an embedded text layer when one exists.</para>
    /// <para><b>Failure profile.</b> A malformed or encrypted PDF fails
    /// deterministically. A transient I/O failure on a large file is rare but
    /// possible.</para>
    /// <para><b>Retry.</b> Bounded, transient-only. The pipeline classification
    /// already treats the usual failures as permanent.</para>
    /// <para><b>Timeout and cancellation.</b> Per-attempt timeout bounds large
    /// renders. Cancellation stops the render promptly and leaves no partial
    /// file behind.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated failures so a broken
    /// PDF does not stall the queue.</para>
    /// <para><b>Registered in.</b> Phase 3 (PDF extraction).</para>
    /// </remarks>
    public const string PdfRender = "image-textify-pdf-render";

    /// <summary>
    /// FFmpeg probing, audio extraction, and any approved media conversion.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Probing a media file and extracting or converting
    /// its audio track.</para>
    /// <para><b>Failure profile.</b> An unsupported codec or a corrupt container
    /// fails deterministically. A transient failure can occur on a large file
    /// whose bytes are only partially cached.</para>
    /// <para><b>Retry.</b> Bounded, transient-only. Never retry a decode
    /// failure.</para>
    /// <para><b>Timeout and cancellation.</b> Per-attempt timeout bounds FFmpeg.
    /// Cancellation must terminate the child process and remove any partial
    /// output.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated failures so a bad
    /// binary or a bad file does not stall the queue.</para>
    /// <para><b>Registered in.</b> Phase 5 (audio transcription), after the
    /// FFmpeg distribution approval gate.</para>
    /// </remarks>
    public const string Media = "image-textify-media";

    /// <summary>
    /// Local speech-to-text transcription.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Running the local Whisper-family model against
    /// prepared 16 kHz mono audio.</para>
    /// <para><b>Failure profile.</b> Deterministic failures (missing model,
    /// incompatible input) are not transient. Long inference can time out if the
    /// timeout is not sized to the input length.</para>
    /// <para><b>Retry.</b> Not appropriate for the inference call itself.</para>
    /// <para><b>Timeout and cancellation.</b> Timeout must be generous and
    /// proportional to input length. Cancellation stops decoding promptly.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated deterministic failures
    /// so a missing model does not repeatedly spin the CPU.</para>
    /// <para><b>Registered in.</b> Phase 5 (audio transcription).</para>
    /// </remarks>
    public const string Speech = "image-textify-speech";

    /// <summary>
    /// Model download and checksum verification.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Downloading an OCR or speech model over the
    /// network and verifying its checksum before use.</para>
    /// <para><b>Failure profile.</b> Network failures are transient; a checksum
    /// mismatch is permanent and must delete the partial download.</para>
    /// <para><b>Retry.</b> Bounded, transient-only. A checksum mismatch is never
    /// retried in the same operation; the file is deleted and the user is
    /// told.</para>
    /// <para><b>Timeout and cancellation.</b> Per-attempt timeout bounds each
    /// attempt; total time is bounded by the retry budget. Cancellation stops
    /// the transfer and removes the partial file.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated failures so a down
    /// endpoint does not stall the user.</para>
    /// <para><b>Registered in.</b> Phase 2 (model store), on first use by the
    /// OCR engine.</para>
    /// </remarks>
    public const string ModelDownload = "image-textify-model-download";

    /// <summary>
    /// Opening a camera capture session and reading a still frame.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Enumerating cameras, opening a device, reading a
    /// still frame, releasing the device.</para>
    /// <para><b>Failure profile.</b> A denied permission, an absent device, a
    /// device that is in use by another app, and a device disconnect are not
    /// transient for an immediate retry; a bounded start retry covers a device
    /// that is momentarily busy. Capture itself is a single call with no retry.</para>
    /// <para><b>Retry.</b> Bounded retry on session start only, never on capture.</para>
    /// <para><b>Timeout and cancellation.</b> A short start timeout (10 s) and a
    /// short per-frame timeout (5 s). Cancellation stops the open or the read
    /// promptly and leaves no partial frame.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated start failures so a
    /// dead device does not repeatedly prompt the OS.</para>
    /// <para><b>Registered in.</b> Phase 9 (camera capture).</para>
    /// </remarks>
    public const string CameraCapture = "image-textify-camera-capture";

    /// <summary>
    /// One file conversion from a source format to a target format.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation.</b> Reading a source file, converting it, writing the
    /// target. All local; no network.</para>
    /// <para><b>Failure profile.</b> Most failures are deterministic (an unsupported
    /// pair, a malformed input, a missing library). Some I/O failures can be
    /// transient.</para>
    /// <para><b>Retry.</b> Bounded, transient-only. Unsupported pairs are
    /// rejected before the policy runs (named reason), so this policy only
    /// sees calls that are actually supported.</para>
    /// <para><b>Timeout and cancellation.</b> 60 s per call is generous for one
    /// file. Cancellation stops the conversion and leaves no partial output.</para>
    /// <para><b>Circuit breaker.</b> Opens after repeated failures so a missing
    /// native library does not stall the queue.</para>
    /// <para><b>Registered in.</b> Phase 11 (document/image conversion).</para>
    /// </remarks>
    public const string Conversion = "image-textify-conversion";
}
