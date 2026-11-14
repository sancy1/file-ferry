# ImageTextify Handwriting Recognition -- Qwen3-VL Integration Plan

**Document Version**: 1.0
**Date**: 2026-10-28
**Author**: Staff Software Engineer (Verified Engineering Protocol v2.1)
**Status**: Draft -- pending approval before code
**Repository**: C:\Users\HP\Desktop\file-ferry
**Target release**: v1.2.0

---

## Table of Contents

1. Progress Report: Where ImageTextify Stands
2. The Model: Qwen3-VL-2B-Instruct
3. Architecture: How Qwen is Integrated and Reused
4. Implementation Plan (Phases A-D)
5. Time Estimate Summary
6. Risks and Mitigations
7. What This Plan Does NOT Do
8. Next Action

---

## 1. Progress Report: Where ImageTextify Stands

### 1.1 Printed Text Extraction -- Verified Working at Python Parity

The `ToDetectionTensor` resize defect was diagnosed and fixed on 2026-10-28. Root cause: the PP-OCRv6 detector received the raw image dimensions padded to a multiple of 32, but never resized. PaddleOCR's DB detector requires resizing so the longest side does not exceed `limit_side_len = 736` (verified against the installed `paddlex/inference/models/text_detection/processors.py:53`). Without the resize, the detector ran at the wrong scale, producing misaligned boxes and 10 lines returning `conf = 0.0000` on `form_document_1.png`.

**Verification result (D4 diagnostic, 2026-10-28T13:44:09)**:

| Metric | Before Fix | After Fix | Python Reference |
|---|---|---|---|
| CER on form_document_1.png | 0.36 | **0.0326** | 0.0334 |
| Detected boxes | 26 | **35** | 34 |
| Filtered (conf < 0.1) | 10 | **1** | N/A |
| Kept as PP-OCR | 21 | **34** | ~34 |

C# printed-text extraction now beats the Python reference by 0.0008 CER. The fix is on disk, the build is green (0 warnings, 0 errors), and two regression tests guard against recurrence.

Printed-text extraction accuracy is confirmed at approximately 94% by PP-OCRv6's own benchmark (`Printed EN = 94.1`).

### 1.2 Handwriting Recognition -- Falls Below Acceptable Threshold

The same PP-OCRv6 benchmark that confirms 94.1% printed English accuracy shows `Handwritten EN = 67.8`.

| Category | PP-OCRv6_medium Accuracy |
|---|---|
| Printed English | **94.1%** |
| Handwritten English | **67.8%** |

A 67.8% character accuracy means roughly one in three characters is wrong. This is not production-grade. For enterprise customers, 67.8% handwriting accuracy is indistinguishable from failure. The app cannot be published with a core feature that fails one-third of the time.

TrOCR, the current handwriting recognizer, does not close this gap. The D2.2 diagnostic showed TrOCR producing `0 0000000` on wide crops (519x26 pixels). The model is English-focused, fixed-vocabulary, and sensitive to crop distortion. No amount of preprocessing will lift TrOCR to enterprise accuracy on arbitrary handwriting. The model is the ceiling.

**Conclusion: the current handwriting pipeline is not shippable. A better model is required.**

---

## 2. The Model: Qwen3-VL-2B-Instruct

### 2.1 What It Is

Qwen3-VL-2B-Instruct is a vision-language model (VLM) developed by Alibaba's Qwen team. It processes both images and text, producing text output. Unlike specialized OCR models, a VLM can be prompted with natural language instructions ("Read the handwriting in this image. Return only the text."), making it adaptable to different handwriting styles without retraining.

**Key facts**:

- Base model: `Qwen/Qwen3-VL-2B-Instruct` (Apache-2.0 license)
- Parameters: ~2.1 billion
- GGUF format: available for llama.cpp runtime
- Vision encoder (mmproj): **required separately** for any image input

### 2.2 What It Solves for ImageTextify

| Problem | Qwen3-VL-2B Solution |
|---|---|
| PP-OCRv6 handwriting at 67.8% | VLM reads handwriting contextually, not character-by-character CTC |
| TrOCR produces 0 0000000 on wide crops | VLM accepts arbitrary aspect ratios without fixed-size distortion |
| TrOCR English-only | Qwen3-VL supports multiple languages |
| Cannot be prompted | VLM accepts custom prompts per use case |

A researcher who tested Qwen3-VL-2B on a doctor's prescription noted it handled the document successfully -- something many larger systems fail at -- despite its small size.

### 2.3 Model Size and Bundling Decision

The model ships as **two GGUF files**, both required:

| File | Quant | Size | Purpose |
|---|---|---|---|
| Qwen3-VL-2B-Instruct-Q4_K_M.gguf | Q4_K_M | **1.11 GB** | Language model (text generation) |
| mmproj-Qwen3-VL-2B-Instruct-Q8_0.gguf | Q8_0 | **~0.5 GB** | Vision encoder (image understanding) |

**Total: ~1.6 GB** for the complete offline handwriting package.

Q4_K_M is the correct balance of size and accuracy for desktop deployment. The Q8_0 language model is 1.83 GB and unnecessary for this use case.

The mmproj file is required because the language model alone cannot see images. The mmproj file contains the vision encoder that converts image pixels into embeddings the language model can process. Without it, Qwen3-VL is a text-only model.

---## 3. Architecture: How Qwen is Integrated and Reused

### 3.1 The Service Abstraction

The model is not buried inside the handwriting pipeline. It is
exposed as a standalone, injectable service that any current or
future feature can call with its own prompt.

    // filepath: src/ImageTextify.Platform/Imaging/IQwenVisionService.cs
    // layer:    Infrastructure
    // module:   ImageTextify.Platform.Imaging
    // since:    Phase 2.4b3
    // purpose:  Prompt-agnostic vision inference service. Takes image
    //           bytes and a prompt string, returns generated text.
    //           Consumers supply their own prompt. The service knows
    //           nothing about handwriting or OCR.

    public interface IQwenVisionService : IAsyncDisposable
    {
        Task<string> GenerateAsync(
            ReadOnlyMemory<byte> imageBytes,
            string prompt,
            int maxTokens = 256,
            CancellationToken ct = default);
    }

### 3.2 Reuse Matrix

| Consumer | Prompt | Status |
|---|---|---|
| Handwriting recognizer (this release) | Read the handwriting. Return only the text. | Implemented in Phase 2.4b3 |
| PDF layout parsing (future) | Extract text from this page in reading order. | Same service, different prompt |
| Printed-text fallback (future) | Read the printed text. Return only the text. | Same service, different prompt |
| External app (future) | Any prompt the consumer defines | Same service, DI-resolved |

The model files are loaded once, held in memory by IQwenVisionService,
and shared across all callers. No re-download, no re-load, no model
duplication.

### 3.3 Runtime: LLamaSharp (In-Process, No Ollama)

LLamaSharp is a .NET binding for llama.cpp. It loads GGUF files
directly into the process. No Ollama server, no external process,
no network dependency.

The multimodal API is MtmdWeights. The loading sequence:

    var modelParams = new ModelParams("Qwen3-VL-2B-Instruct-Q4_K_M.gguf");
    using var textModel = await LLamaWeights.LoadFromFileAsync(modelParams);

    var mtmdParams = MtmdContextParams.Default();
    mtmdParams.UseGpu = false;
    mtmdParams.NThreads = Environment.ProcessorCount / 2;

    using var mtmd = await MtmdWeights.LoadFromFileAsync(
        "mmproj-Qwen3-VL-2B-Instruct-Q8_0.gguf",
        textModel,
        mtmdParams);

    mtmd.LoadMedia(imageBytes.Span);

The MtmdWeights class provides LoadFromFileAsync, LoadMedia, and
SupportsVision. This is the standard LLamaSharp multimodal pattern.

Known limitation: LLamaSharp's Qwen3-VL support depends on the native
llama.cpp version bundled with the NuGet package. An open issue
requests Qwen3-VL support. This must be verified in the benchmark
step before committing to the integration. If LLamaSharp's current
release cannot load Qwen3-VL, the fallback is the llama-mtmd-cli
subprocess (proven to work with Qwen3-VL) or wait for the update.

---

## 4. Implementation Plan

### Phase A -- Benchmark (1-2 days, no production code)

Goal: Verify Qwen3-VL-2B Q4_K_M reaches CER <= 0.15 on ImageTextify's
handwriting eval set.

| Step | Action | Verification |
|---|---|---|
| A.1 | Download the two GGUF files from Hugging Face | Files present, SHA-256 recorded |
| A.2 | Write a Python benchmark script using llama-cpp-python or llama-mtmd-cli | Script runs on handwriting_line_1.png |
| A.3 | Run the script on all handwriting images in bench/eval-set/ | Output text per image |
| A.4 | Compute CER against the label JSONs | CER per image, average CER |
| A.5 | Gate: if CER <= 0.15, proceed to Phase B. Else test Qwen3-VL-8B or Qwen2.5-VL-3B | Decision recorded |

Estimated time: 1-2 days.

### Phase B -- Production Integration (3-5 days)

Goal: Replace TrOCR with Qwen3-VL-2B in the ImageTextify pipeline,
behind the IQwenVisionService abstraction.

| Step | File | Action |
|---|---|---|
| B.1 | ImageTextify.Platform/Imaging/IQwenVisionService.cs | Create interface |
| B.2 | ImageTextify.Platform/Imaging/QwenVisionService.cs | Implement using LLamaSharp MtmdWeights |
| B.3 | ImageTextify.Platform/Composition/ImageTextifyServiceCollectionExtensions.cs | Register IQwenVisionService as singleton |
| B.4 | ImageTextify.Platform/Imaging/QwenHandwritingRecognizer.cs | New class: calls IQwenVisionService.GenerateAsync |
| B.5 | ImageTextify.Platform/Imaging/OnnxOcrEngine.cs | Replace RunTrocrRecognitionAsync with QwenHandwritingRecognizer |
| B.6 | ImageTextify.Platform/ImageTextify.Platform.csproj | Add LLamaSharp + LLamaSharp.Backend.Cpu |
| B.7 | Directory.Packages.props | Pin LLamaSharp versions |
| B.8 | FileHarvester.Tests/ImageTextify/ | Contract tests for IQwenVisionService |

Build gate: dotnet build FileHarvester.slnx -- 0 errors, 0 warnings.

Test gate: all existing ImageTextify tests pass plus new contract tests.

### Phase C -- Installer Configuration (2-3 days)

Goal: Ship two installers: Lite (download on demand) and Full (bundled).

| Step | Action |
|---|---|
| C.1 | Add GGUF files to Full installer Assets/Models/Qwen3-VL-2B-Instruct/ |
| C.2 | Configure Inno Setup or WiX to include GGUF files in Full only |
| C.3 | Lite installer omits GGUF; ModelStore downloads on first handwriting request |
| C.4 | Update ModelStore manifest with GGUF URLs, sizes, SHA-256 |
| C.5 | Test: Full installer offline immediately; Lite downloads then works |

Installer sizes: Lite ~70 MB, Full ~1.7 GB.

### Phase D -- Verification and Release (1-2 days)

| Step | Action | Gate |
|---|---|---|
| D.1 | Run D4 with Qwen handwriting path | CER <= 0.15 handwriting eval |
| D.2 | Run full pipeline on all 15 eval images | Printed CER <= 0.06, Handwriting CER <= 0.15 |
| D.3 | Update CHANGELOG.md | Entry with CER numbers |
| D.4 | Update docs/architecture.md and docs/phase-map.md | Qwen documented |
| D.5 | git add + commit ImageTextify surface | Release blocker resolved |

---
## 5. Time Estimate Summary

| Phase | Work | Estimate |
|---|---|---|
| A -- Benchmark | Download, script, run, decide | 1-2 days |
| B -- Production Integration | Service, recognizer, DI, tests | 3-5 days |
| C -- Installer Configuration | Two installers, ModelStore manifest | 2-3 days |
| D -- Verification and Release | Full eval, CHANGELOG, git commit | 1-2 days |
| Total | | 7-12 working days |

Risk buffer: +3 days for LLamaSharp Qwen3-VL compatibility verification
(Phase A gate). If LLamaSharp cannot load Qwen3-VL, the llama-mtmd-cli
subprocess fallback adds 1-2 days of integration work.

Realistic estimate: 10-15 working days from approval to releasable feature.

---

## 6. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| LLamaSharp cannot load Qwen3-VL mmproj | Medium | Blocks Phase B | Phase A tests this. Fallback: llama-mtmd-cli subprocess (proven) |
| Qwen3-VL-2B CER > 0.15 on handwriting | Medium | Model too small | Phase A gate: test Qwen2.5-VL-3B (1.80 GB Q4_K_M) or Qwen3-VL-8B (larger download) |
| Full installer too large for enterprise | Low | Adoption friction | Lite installer available; Full is opt-in |
| CPU inference too slow for production | Medium | UX degradation | Q4_K_M on modern CPU: ~7-30 tokens/sec. Line crops are short. Acceptable. |
| Qwen hallucinated text on unreadable regions | Medium | Wrong output | Prompt explicitly: If you cannot read a character, output [?]. Do not invent text. |

---

## 7. What This Plan Does NOT Do

- Does not replace PP-OCRv6 for printed text. Printed text remains the current pipeline (94.1% accuracy, verified CER 0.0326).
- Does not use Qwen for audio or video. Qwen-Audio is a separate model family.
- Does not commit or push anything without explicit instruction.
- Does not modify Prompt.Platform (untracked, broken, out of scope).
- Does not promise handwriting perfection. Even with Qwen, handwriting will have errors. The UX must flag low-confidence lines and require review.

---

## 8. Next Action

Step A.1 is the immediate action: download the two GGUF files and verify
they load with llama-mtmd-cli. This is a read-only, no-production-code
step that confirms the architecture is viable before any C# is written.

If this plan is approved, the next write is the PowerShell script for
Phase A -- the benchmark setup and run.

---

End of plan.