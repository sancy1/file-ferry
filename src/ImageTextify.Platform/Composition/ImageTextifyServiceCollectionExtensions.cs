// ============================================================================
// FILE:     src/ImageTextify.Platform/Composition/ImageTextifyServiceCollectionExtensions.cs
// PURPOSE:  The single DI entry point for ImageTextify. Creates the
//           ImageTextify root directory under the supplied application data
//           root and returns the collection unchanged. No services are
//           registered in Phase 1.2; each later phase adds its own registrations
//           here so this method stays the one ImageTextify composition point.
// RELATION: Called by FileHarvester.App/App.axaml.cs immediately after
//           AddSchemaLence. Mirrors the shape of
//           SchemaLenceServiceCollectionExtensions.AddSchemaLence.
// ============================================================================

using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Settings;
using ImageTextify.Platform.Validation;
using ImageTextify.Platform.Intake;
using Microsoft.Extensions.DependencyInjection;
using ImageTextify.Platform.Imaging;
using ImageTextify.Platform.Models;
using ImageTextify.Platform.Extraction;
using ImageTextify.Platform.Media;
using ImageTextify.Platform.Speech;
using ImageTextify.Platform.Audio;
using ImageTextify.Platform.Camera;
using ImageTextify.Platform.Conversion;
using Microsoft.Extensions.Http;
using Portfolio.Resilience.Abstractions;

namespace ImageTextify.Platform.Composition;

/// <summary>
/// Composition entry point for ImageTextify.
/// </summary>
public static class ImageTextifyServiceCollectionExtensions
{
    /// <summary>
    /// Registers ImageTextify with the service collection and ensures the
    /// ImageTextify root directory exists under the supplied application
    /// data root.
    /// </summary>
    /// <param name="services">
    /// The service collection to register into. Not null.
    /// </param>
    /// <param name="applicationDataDirectory">
    /// The application-owned data root. ImageTextify creates its own
    /// <c>ImageTextify</c> subfolder under this path and owns that subfolder.
    /// The caller owns the parent directory. Null, empty, or whitespace is
    /// rejected.
    /// </param>
    /// <param name="qwenTextModelPath">
    /// Optional path to the Qwen2.5-VL-3B text model GGUF file (Q4_K_M, ~1.84 GB).
    /// When both this and <paramref name="qwenMmprojPath"/> are non-null, the
    /// Qwen handwriting pipeline is registered. When either is null, the engine
    /// falls back to TrOCR for handwriting (current behavior preserved).
    /// </param>
    /// <param name="qwenMmprojPath">
    /// Optional path to the Qwen2.5-VL-3B vision projector GGUF file (F16, ~1.28 GB).
    /// Must be provided together with <paramref name="qwenTextModelPath"/>.
    /// </param>
    /// <param name="ffmpegBinDirectory">
    /// Optional path to the directory containing the pinned ffmpeg.exe
    /// and ffprobe.exe (the LGPL build recorded in the technology
    /// decision record, F-AV-012). When provided, the audio/video
    /// transcription pipeline is registered. When null or empty, the
    /// Audio and Video categories are unavailable and the rest of
    /// ImageTextify works as before.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance, for fluent chaining.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="applicationDataDirectory"/> is null, empty,
    /// or whitespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Services registered in this phase.</b> None. ImageTextify interfaces
    /// do not exist until Phase 1.3 and are registered by later phases, each in
    /// the phase that first needs them. Registering speculative services would
    /// violate the smallest-safe-change rule.
    /// </para>
    /// <para>
    /// <b>Subdirectories.</b> Only the ImageTextify root is created here.
    /// Models, Temp, and Exports subdirectories are created by the phases that
    /// first use them, each with its own test. Empty subdirectories at
    /// composition time are speculative state and are avoided.
    /// </para>
    /// <para>
    /// <b>Resilience.</b> ImageTextify consumes the Portfolio.Resilience
    /// registration already installed by AddFileHarvester. It does not
    /// re-register resilience. The Directory.CreateDirectory call below runs
    /// outside a resilience policy as an accepted startup exception, mirroring
    /// finding F-009 recorded in docs/resilience-usage.md. ImageTextify records
    /// the same exception as ImageTextify-012 in its own findings.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddImageTextify(
        this IServiceCollection services,
        string applicationDataDirectory,
        string? qwenTextModelPath = null,
        string? qwenMmprojPath = null,
        string? ffmpegBinDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);

        // Accepted startup exception: Directory.CreateDirectory is not
        // wrapped in a resilience policy. The call runs once at startup
        // on a directory the application owns, and its failure modes are
        // not transient failures a retry could recover from. Mirrors
        // finding F-009 in docs/resilience-usage.md. Recorded as
        // ImageTextify-012.
        var root = Path.Combine(applicationDataDirectory, "ImageTextify");
        Directory.CreateDirectory(root);

        // Phase 1.2 registers no ImageTextify services.
        // Each later phase adds its registrations here.
        // Phase 1.5a registers the file validator.
        // Phase 1.5b registers the intake evaluator, which owns
        // duplicate detection and orchestrates the validator.
        // Later phases add their own services here.
        services.AddSingleton<IImageTextifyFileValidator, ImageTextifyFileValidator>();
        services.AddSingleton<IImageTextifyIntakeService, ImageTextifyIntakeService>();

        // Phase 2.6-PDF-1: digital-PDF text extraction.
        services.AddSingleton<IPdfTextExtractor, PdfPigPdfTextExtractor>();

        // Phase 2.2 registers the model store and its dependencies.
        // The named client is used only by the store.
        services.AddHttpClient("ImageTextifyModelDownload");
        services.AddSingleton<IFileHasher, Sha256FileHasher>();
        services.AddSingleton<IModelStore>(sp => new ModelStore(
            sp.GetRequiredService<IResilienceExecutor>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IFileHasher>(),
            () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileFerry",
                "ImageTextify",
                "Models")));

        // Phase 2.4b2 registers the OCR pipeline.
        // OnnxOcrEngine depends on OnnxModelRunner, ImagePreprocessor,
        // OcrLineRouter, and OcrResultMerger. Download-on-first-use is
        // deferred to Phase 2.4b4 (ImageTextify-143). Models must be
        // present at OnnxModelRunner construction.

        // Phase 2.4d: Qwen handwriting (optional, configured by model paths).
        // If both paths are non-null, register IQwenVisionService and the
        // QwenHandwritingRecognizer. If either is null, OnnxOcrEngine falls back
        // to TrOCR (current behavior preserved).
        if (!string.IsNullOrEmpty(qwenTextModelPath) && !string.IsNullOrEmpty(qwenMmprojPath))
        {
            services.AddSingleton<IQwenVisionService>(sp =>
                new QwenVisionService(qwenTextModelPath!, qwenMmprojPath!));
            services.AddSingleton<QwenHandwritingRecognizer>(sp =>
                new QwenHandwritingRecognizer(sp.GetRequiredService<IQwenVisionService>()));
        }

        // Phase 2.4e: ImageTextify settings store. Singleton, in-memory
        // in B.9; persistence is deferred to a future phase (finding
        // ITX-B9-003).
        services.AddSingleton<IImageTextifySettingsStore, InMemoryImageTextifySettingsStore>();

        // Phase 2.4e: printed-text fallback. Only registered when the
        // Qwen vision service is present, mirroring the handwriting
        // conditional above.
        if (!string.IsNullOrEmpty(qwenTextModelPath) && !string.IsNullOrEmpty(qwenMmprojPath))
        {
            services.AddSingleton<QwenPrintedFallbackRecognizer>(sp =>
                new QwenPrintedFallbackRecognizer(sp.GetRequiredService<IQwenVisionService>()));
        }
        services.AddSingleton<IImagePreprocessor, ImagePreprocessor>();
        services.AddSingleton<IOcrLineRouter, OcrLineRouter>();
        services.AddSingleton<IOcrResultMerger, OcrResultMerger>();

        services.AddSingleton<IOcrModelRunner>(sp =>
        {
            var store = sp.GetRequiredService<IModelStore>();
            var detPath = store.GetLocalPath(ImageTextifyOcrModels.PpOcrDetector);
            var recPath = store.GetLocalPath(ImageTextifyOcrModels.PpOcrRecognizer);
            var encPath = store.GetLocalPath(ImageTextifyOcrModels.TrocrEncoder);
            var decPath = store.GetLocalPath(ImageTextifyOcrModels.TrocrDecoder);
            var dictPath = Path.Combine(Path.GetDirectoryName(recPath)!, "ppocr_dict.txt");
            var tokPath = Path.Combine(Path.GetDirectoryName(encPath)!, "tokenizer.json");

            return new OnnxModelRunner(
                ppocrDetectorPath: detPath,
                ppocrRecognizerPath: recPath,
                ppocrDictionaryPath: dictPath,
                trocrEncoderPath: encPath,
                trocrDecoderPath: decPath,
                trocrTokenizerPath: tokPath);
        });

        services.AddSingleton<IImageOcrEngine>(sp => new OnnxOcrEngine(
            sp.GetRequiredService<IOcrModelRunner>(),
            sp.GetRequiredService<IImagePreprocessor>(),
            sp.GetRequiredService<IOcrLineRouter>(),
            sp.GetRequiredService<IOcrResultMerger>(),
            sp.GetService<IQwenVisionService>(),
            sp.GetService<QwenPrintedFallbackRecognizer>(),
            sp.GetRequiredService<IImageTextifySettingsStore>()));

        // Phase 5: audio and video transcription. Registered only when
        // the FFmpeg bin directory is supplied (the pinned LGPL build
        // recorded in the technology decision record, F-AV-012). When
        // null, the rest of ImageTextify works; the Audio and Video
        // categories are unavailable. This is the same optional-path
        // pattern as the Qwen handwriting registrations above.
        if (!string.IsNullOrEmpty(ffmpegBinDirectory))
        {
            services.AddSingleton<IFfmpegLocator>(sp =>
                new ConfiguredFfmpegLocator(ffmpegBinDirectory!));
            services.AddSingleton<IMediaProbe>(sp =>
                new FfprobeProcessAdapter(sp.GetRequiredService<IFfmpegLocator>()));
            services.AddSingleton<IAudioDecoder>(sp =>
                new FfmpegDecodeAdapter(sp.GetRequiredService<IFfmpegLocator>()));

            // The segmenter and recognizer take model paths resolved
            // synchronously from IModelStore.GetLocalPath. Download on
            // first use is deferred to the app first-run flow (Phase 5.E),
            // which calls IModelStore.EnsureAvailableAsync with progress.
            services.AddSingleton<ISpeechSegmenter>(sp =>
            {
                var store = sp.GetRequiredService<IModelStore>();
                var vadPath = store.GetLocalPath(ModelManifest.SileroVadGgml);
                return new WhisperNetSileroSegmenter(vadPath);
            });
            services.AddSingleton<ISpeechRecognizer>(sp =>
            {
                var store = sp.GetRequiredService<IModelStore>();
                var whisperPath = store.GetLocalPath(ModelManifest.WhisperSmallGgml);
                return new WhisperNetRecognizer(whisperPath);
            });
            services.AddSingleton<IHallucinationSuppressor, HallucinationSuppressor>();

            services.AddSingleton<ITranscriptionPipeline>(sp => new TranscriptionPipeline(
                sp.GetRequiredService<IMediaProbe>(),
                sp.GetRequiredService<IAudioDecoder>(),
                sp.GetRequiredService<ISpeechSegmenter>(),
                sp.GetRequiredService<ISpeechRecognizer>(),
                sp.GetRequiredService<IHallucinationSuppressor>()));

            // Phase 10.D: live transcription. The capture adapter is
            // Windows-only; other platforms get no registration and the
            // ViewModel reports IsAvailable=false.
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IAudioCaptureService, NAudioAudioCaptureService>();
            }

            // Phase 10.D: live transcription service. Transient because the
            // streaming segmenter is single-use per session; a singleton
            // would hold one segmenter for the process lifetime.
            services.AddTransient<ILiveTranscriptionService>(sp =>
            {
                var store = sp.GetRequiredService<IModelStore>();
                var vadPath = store.GetLocalPath(ModelManifest.SileroVadGgml);
                return new LiveTranscriptionService(
                    sp.GetRequiredService<IAudioCaptureService>(),
                    new StreamingSileroSegmenter(vadPath),
                    sp.GetRequiredService<ISpeechRecognizer>(),
                    sp.GetRequiredService<IHallucinationSuppressor>());
            });

            // Phase 10.F: opt-in live audio recording. Transient because a
            // recording service owns one file handle and one sink per session.
            services.AddTransient<ILiveRecordingService, LiveRecordingService>();

            // Phase 13: text-to-speech. Edge neural TTS is the primary engine
            // (natural voices, cloud, no key); System.Speech SAPI is the
            // offline fallback. FallbackTextToSpeechService delegates to SAPI
            // whenever the Edge endpoint fails (network down or blocked).
            // All three are singletons: each owns audio-device state.
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<SystemSpeechTextToSpeechService>();
                services.AddSingleton<EdgeTtsTextToSpeechService>();
                services.AddSingleton<ITextToSpeechService>(CreateTextToSpeechService);
            }
        }


        // Phase 9: camera capture. Periphery.Camera 5.0.0 supports
        // Windows and Linux; macOS is not supported (finding F-008).
        // Other platforms get no registration and the camera surface
        // reports "camera unavailable".
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            services.AddSingleton<ICameraCaptureService, PeripheryCameraCaptureService>();

            // Phase 11: document/image converter. Aspose.Cells_FOSS was verified
            // on Windows only in the Phase 11 spike; the other converters are
            // cross-platform but ship under the same guard for now (F-034).
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IConversionService, ConversionService>();

                // Phase 14: screenshot capture (System.Drawing.Common, Windows-only).
                services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
            }
        }
        return services;
    }

    /// <summary>
    /// Builds the registered TTS service: Edge neural primary, SAPI fallback.
    /// Marked Windows-only because both engines are Windows-only.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ITextToSpeechService CreateTextToSpeechService(IServiceProvider sp)
    {
        return new FallbackTextToSpeechService(
            sp.GetRequiredService<EdgeTtsTextToSpeechService>(),
            sp.GetRequiredService<SystemSpeechTextToSpeechService>());
    }
}
