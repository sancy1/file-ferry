# Changelog

All notable changes to File-Ferry are documented in this file.

## [Unreleased]

## [1.2.0] - 2026-11-14
### Prompt App v1

- **Persistence (Phases 0–1):** SQLite schema with seven tables, an FTS5
  full-text search table, and three sync triggers. The seven domain
  interfaces (IPromptStore, ICategoryStore, ISubCategoryStore,
  IPromptVersionStore, IPromptExporter, IPromptImporter,
  IPdfTextExtractor) sit over the four stores, the exporter, and the
  importer. Version snapshots, drafts, and 50-version cap are enforced.
- **UI (Phase 2):** the shell registration, the sidebar tree and prompt
  list, the viewer, the editor, the category manager, the search bar
  (debounced, scope dropdown, tag multi-select, favorites-only toggle),
  and the dialogs (editor, import, export, trash, confirm, history,
  category manager, category picker, tag prompt, shortcuts).
- **Import / export (Phase 3):** .txt / .md / .docx / .pdf import;
  single-file and zip-tree export across all registered format writers
  with a manifest.json in the zip.
- **Safety (Phase 4):** trash + restore, version history with restore,
  2-second auto-save drafts with restore-on-open, favorites, and bulk
  operations (export, move, add tag, delete).
- **Resilience:** three named policies (prompt-store, prompt-render,
  prompt-export) through Portfolio.Resilience 0.8.0; every store,
  exporter, and importer call routed through IResilienceExecutor.
- **Observability (P5.1):** eight structured operation events
  (prompt_created, prompt_updated, prompt_deleted, prompt_restored,
  prompt_exported, prompt_imported, prompt_search_executed,
  prompt_render_failed) emitted via ResilienceEvent.Metadata through
  the existing Log Drawer and Diagnostics views. prompt_render_failed
  is defined and supported; its first call site arrives with the
  rich-text renderer (P2.6, deferred).
- **Keyboard shortcuts (P5.2):** Ctrl+N, Ctrl+F, Ctrl+E, Delete on the
  page; Ctrl+S in the editor; Ctrl+Z / Ctrl+Y as the editor's built-in
  undo/redo. A "?" button at the end of the viewer toolbar opens a
  discoverable Shortcuts dialog listing all six and their actions.
- **Documentation (P5.3):** docs/prompt-app/architecture.md,
  docs/prompt-app/user-guide.md, and the phase-map closures. The
  documentation is machine-checked by PromptAppClaimsTests (10 facts)
  so every doc claim is asserted against a real code artifact.
- **Tests:** 122 → 145 across the Prompt and Documentation suites.
  The existing 126 Prompt tests remain green inside the 145.

### Added
- ImageTextify composition seam: AddImageTextify extension method
  and the ImageTextify-owned policy-name constants.
- CompositionTests covering argument validation, root-directory
  creation, subdirectory non-creation in this phase, fluent return,
  no premature service registration, and policy-name invariants
  (prefix, distinctness, kebab-case, XML documentation).
- ImageTextify Core contracts (Phase 1.3):
  ExtractionKind, ImageTextifySourceFile, ImageTextifyResult,
  ImageTextifyDocument, ImageTextifyRedaction, IImageOcrEngine,
  IPdfTextExtractor, IMediaPreparer, ISpeechTranscriber, ITextCleaner,
  IAudioCaptureService, IAudioEnhancer, ICameraCaptureService,
  ILiveTranscriptionService.
- ImageTextify App-facing consumer contract: navigation member, command,
  active-state property, foundation page, and consumer-driven composition
  tests.
- ImageTextify resilience-backed file validator for intake eligibility checks,
  including cancellation, fail-closed fallback behavior, and test-only
  resilience event capture.
- ImageTextify intake rows now show a human-readable file size and
  the parent directory, populated by the validator at intake time.
- ImageTextify Phase 2.1: added Microsoft.ML.OnnxRuntime 1.31.0 and
  SkiaSharp 3.119.4 package versions to central package management
  and referenced them from ImageTextify.Platform. No model download
  or OCR code yet.
- ImageTextify Phase 2.2: model store with disclosed download,
  streaming SHA-256 verification, .partial-then-atomic-rename
  promotion, cancellation cleanup, and a placeholder-only manifest.
  New image-textify-model-download resilience policy. No OCR engine
  yet.
- ImageTextify Phase 2.3 Block B: ReadingOrderTests.cs with 14 behavior
  facts covering contract validation, determinism, two-column layout,
  column-gap thresholding, median-width defaults, and row-based fallback.
  No production code change.
- ImageTextify Phase 2.3 Block C: 8 new PreprocessTests.cs facts closing the
  remaining Phase 2.3 coverage gaps: deterministic output, both branches of
  the upscale threshold, cancellation safety, the enhanced variant label,
  original-pixel immutability, and sharpen and binarize defaulting to off.
  No production code change.

### Changed
- None.

### Fixed
- ImageTextify Phase 2.3 Block A: removed a use-after-dispose in
  `ImagePreprocessor.PreprocessAsync` where `AreSame` read `decoded.Width`
  after the bitmap had already been released by `DisposeIfOwned`. The
  `OrientationApplied` diagnostic now compares the live `current` bitmap
  dimensions against captured integers from the decoded source. Fixes
  the `0xC0000005` crash that aborted `PreprocessTests` at test host
  startup. (`ImageTextify-084`.)
- `PreprocessTests.DenoisePreservesSharpEdges` now passes `EffectiveDpiHint: 300`
  so the fact tests denoising under a controlled no-upscale precondition.
  The documented default pipeline upscales a 200x200 source 2x to 400; the
  old assertion encoded a contract the pipeline never held. No production
  code changed. (`ImageTextify-094`.)

### Phase 2.4a - Evaluation set and engine selection

- Python evaluation harness under bench/ that measured three OCR candidates
  (PP-OCR alone, PP-OCR+TrOCR router, TrOCR alone) on a 15-image annotated
  eval set across 4 categories. Harness is not part of the app and does not
  ship. Reports in bench/reports/.
- Selection rules proven by data: PP-OCR is the default printed-text
  recognizer (CER 0.04-0.13 on clean_print); TrOCR Handwritten is the
  default handwriting candidate (CER 0.00-0.05 on 5 of 7 clear_handwriting);
  PP-OCR is the default detector.
- Router architecture validated but with a documented limitation: TrOCR on
  PP-OCR box crop performs worse than TrOCR on whole-image for handwriting.
  Phase 2.4b should feed TrOCR a whole-line crop.
- Model verification records for PP-OCRv6_medium_det, PP-OCRv6_medium_rec,
  and microsoft/trocr-base-handwritten. ImageTextify-052 closed.
- Harness defects found and fixed during Phase 2.4a: PaddleOCR 3.7.0 API
  changes (show_log, cls, return format), and a one-image crash in the
  router candidate deferred to Phase 2.4b (ImageTextify-129).
- Documentation drift recorded (ImageTextify-130): findings 110-122 and
  125-126 are referenced in milestones but not present as section 4 rows.
  Cleanup is its own work item.
### Phase 2.4b1 - ONNX model verification

- Phase 2.4b1 Deliverables 1-3 complete: documentation cleanup, router
  crash fix, and ONNX model verification.
- PP-OCRv6 ONNX via PaddleX ONNX Runtime verified at Python-reference
  accuracy (CER within 0.05 on all print images).
- TrOCR ONNX (optimum-cli export, opset 14) verified bit-equivalent to
  Python reference (CER diff 0.0000 on 3 handwriting images).
- Both paths work correctly. Router regressions documented in limitations.md.
- ImageTextify-052 (model verification) closed.
- Zero EXTRACT FAILED across 15 images x 3 candidates after crash fix.
### Phase 2.4b2 - C# ONNX OCR implementation (in progress)

- Nine new files and one edit implementing the full OCR pipeline under
  src/ImageTextify.Platform/Imaging. Approx 2,070 lines.
- OnnxModelRunner: full ONNX Runtime inference for PP-OCR detection +
  recognition and TrOCR handwriting recognition.
- OcrLineRouter: six garbage heuristics for per-line routing (decision
  D1, no confidence bands).
- OcrResultMerger: reading-order merge using existing ReadingOrder.Sort.
- OnnxOcrEngine: orchestrator implementing IImageOcrEngine.
- ImageTextifyOcrModels: four ONNX model descriptors with verified
  SHA-256s and sizes.
- IImageOcrEngine contract updated: OcrResult gains required Lines
  parameter with per-line provenance.
- Build green (0 warnings, 0 errors).
- Tests, DI registration, and end-to-end verification still pending.

### Phase 2.4d / 2.4e - Qwen vision handwriting and printed fallback

- `IQwenVisionService` contract plus `QwenVisionService`, an LLamaSharp
  implementation that loads Qwen2.5-VL-3B (GGUF text model + F16 mmproj)
  once as a singleton and runs image+prompt inference with the Qwen chat
  template.
- `QwenHandwritingRecognizer` (Phase 2.4d) and
  `QwenPrintedFallbackRecognizer` (Phase 2.4e) wired into
  `OnnxOcrEngine` as the opt-in difficult-text path.

### Phase 5 - Media transcription (Whisper)

- Whisper.net speech recognition with Silero VAD segmentation for audio
  and video files, routed through the ImageTextify intake pipeline.

### Phase 9 - Camera capture

- `ICameraCaptureService` contract and `PeripheryCameraCaptureService`
  (Periphery.Camera 5.0.0). Camera workspace enumerates cameras, shows a
  live preview, freezes a frame, and sends it through the same OCR
  pipeline. Registered on Windows and Linux; macOS is unsupported by the
  camera library (finding F-008).

### Phase 10 - Live transcription and recording (Windows)

- `ILiveTranscriptionService` / `ILiveTranscriptionSession` contracts and
  `LiveTranscriptionService` factory.
- `NAudioAudioCaptureService`: WASAPI microphone capture and system
  audio loopback emitting 16 kHz mono float32 PCM. Windows-only;
  macOS/Linux adapters deferred (Phase 10.E).
- `StreamingSileroSegmenter` (batch VAD path) with
  `WhisperVadAdapter`, `UtteranceAudioConditioner` DSP stage, and
  `IHallucinationSuppressor` filtering.
- Opt-in `LiveRecordingService` writing 16 kHz mono PCM16 WAV via
  `TeeStream` / `TransformingSinkStream` (Block 10.F). WAV only; no
  encoders.
- Live Transcription workspace page, view model, and transcript UI.

### Phase 11 - Document and image conversion (Windows)

- `IConversionService` / `IFormatConverter` contracts and
  `ConversionService` dispatcher.
- Converters: `ImageFormatConverter` (png/jpg/jpeg/webp/ico/gif),
  `MarkdownDocxConverter` (MD to DOCX, DOCX to MD),
  `MarkdownPdfConverter`, `TextDocumentConverter`, `ExcelConverter`
  (JSON to CSV, CSV to JSON, any file to XLSX via Aspose.Cells.FOSS).
- Explicitly unsupported pairs return a clear failure rather than a
  silent wrong output (XLSX to CSV/JSON, DOCX to PDF, PDF to DOCX).
- Conversion workspace page and view model. Registered on Windows only
  pending cross-platform verification of Aspose.Cells_FOSS (F-034).

### Phase 13 - Text-to-speech (Windows)

- `ITextToSpeechService` contract with three implementations:
  `EdgeTtsTextToSpeechService` (neural voices, primary),
  `SystemSpeechTextToSpeechService` (SAPI, offline fallback), and
  `FallbackTextToSpeechService` (delegates to SAPI when the Edge endpoint
  fails). Windows-only registration.
- Text-to-Speech workspace page and view model.

### CodeViewer

- `CodeViewer.Core`: project tree model (`ProjectNode`, `NodeKind`,
  `NodeLoadState`), exclusion rules (`ExclusionRule`, `ExclusionKind`,
  `GlobalExclusions`), and `FileRoleClassifier`.
- `CodeViewer.Platform`: project scanning and tree loading.
- Code Viewer workspace: project browser, structure pane, and
  syntax-highlighted code surface (AvaloniaEdit + TextMate grammars)
  with per-file and whole-tree copy.

### Changed
- Unified action-button coloring across workspaces (green primary,
  blue save/export/copy, red destructive, grey secondary) with white
  foreground and rounded corners.
- Pane splitters on the Harvest-adjacent workspaces are now transparent
  with a wider dark gap instead of a visible divider line; dragging is
  unchanged.
- SchemaLence sidebar scroll content reserves a gutter so the overlay
  scrollbar no longer covers input fields, and a Reset command clears all
  connection fields and session state.
- Per-file remove buttons now render the X glyph optically centered.

## [1.1.0] - 2026-10-12

### Added
- Cloud workspace with built-in profiles for AWS S3, S3-compatible services,
  Azure Blob Storage, Google Cloud Storage, Google Drive, OneDrive, and Dropbox.
- Google Drive Picker downloads for one or multiple selected files, with
  Google Workspace files exported to PDF, XLSX, and PPTX.
- PostgreSQL SchemaLence workspace for schema and table inspection, paged
  role-visible data, Excel export, and custom-format database backups.
- Link Joiner for combining base locations with lists of paths.
- Extract workspace for finding links, paths, email addresses, and phone-like
  text in user-provided content.
- Confirmation before replacing existing Harvest output files.
- Secure credential-store integration and provider-specific cloud setup.

### Changed
- Expanded the application navigation, responsive workspaces, diagnostics,
  resilience policies, and release documentation.
- Database backups run without the short timeout intended for ordinary local
  file writes and remain cancellable.
- Reformatted SchemaLence Structure / Definition reports with a readable,
  aligned columns table and clearly separated, icon-led metadata sections.

### Fixed - Overwrite Confirmation
- New `IOverwriteConfirmer` interface and `AlwaysCancelOverwriteConfirmer`
  default (fail-closed). `OutputSink` now asks before replacing an existing
  file, in both merged and separate output modes.
- New `OverwritePromptResult` record and `OverwriteDecision` enum.
- "Apply to all remaining files" for separate-files mode, scoped to the
  current harvest operation only.
- Existence check runs through the `file-read` policy with a fail-closed
  fallback (treats an unavailable check as "file exists").
- Caller cancellation while the prompt is open returns Cancel and does not
  write the file.

- The `----- {path} -----` default delimiter was corrected to
  `===== {path} =====` to match the documented and tested default.
- `ComposerOptions.SingleMerged()` now sets `IncludeHeaders: true`, matching
  `ComposedDocument.ForSingleMerged()`.

### Maintenance
- Version reconciled to `1.1.0` across shared build settings, the app project,
  and the in-app version display.
- Deleted the leftover `dotnet new xunit` template file `UnitTest1.cs`.
