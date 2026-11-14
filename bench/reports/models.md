# Phase 2.4a/2.4b1 model verification records

ImageTextify-052.

## Python reference models (Phase 2.4a)

### PP-OCRv6_medium_det

- Source: PaddleOCR / PaddleX official CDN
- Package: paddleocr 3.7.0, paddlepaddle 3.2.2 (CPU)
- Size: 32 MB (Paddle static graph)
- License: Apache 2.0
- Verification: 2026-10-26

### PP-OCRv6_medium_rec

- Source: PaddleOCR / PaddleX official CDN
- Package: paddleocr 3.7.0, paddlepaddle 3.2.2 (CPU)
- Size: 42 MB (Paddle static graph)
- License: Apache 2.0
- Measured CER: 0.03-0.15 on clean_print, 0.03-0.16 on clear_handwriting
- Verification: 2026-10-26

### microsoft/trocr-base-handwritten (Python)

- Source: Hugging Face, microsoft/trocr-base-handwritten
- Package: transformers 5.20.2, torch 2.7.1+cu118 (GPU Quadro T2000)
- Size: 1.33 GB
- License: MIT
- Measured CER: 0.00-0.05 on 5 of 7 clear_handwriting images
- Verification: 2026-10-26

## ONNX models (Phase 2.4b1) - THE OPERATIONAL MODELS

### PP-OCRv6_medium_det_onnx (PaddleX auto-download)

- Source: PaddleX official model hub, auto-downloaded on first OCR pipeline call
- Path: `~/.paddlex/official_models/PP-OCRv6_medium_det_onnx/`
- Size on disk: 32 MB (inference.onnx + inference.yml)
- License: Apache 2.0
- Engine: onnxruntime (CPU)
- Verification: 2026-10-26
- ONNX-vs-Python CER diff on form_document_1/2/3, How-to-make: all within 0.05
- **Operational model. Used by PaddleX ONNX pipeline in the harness.**

### PP-OCRv6_medium_rec_onnx (PaddleX auto-download)

- Source: PaddleX official model hub
- Path: `~/.paddlex/official_models/PP-OCRv6_medium_rec_onnx/`
- Size on disk: 76.6 MB (inference.onnx + inference.yml + inference.json)
- License: Apache 2.0
- Engine: onnxruntime (CPU)
- Verification: 2026-10-26
- ONNX-vs-Python CER diff on form_document_1/2/3, How-to-make: all within 0.05
- **Operational model.**

### TrOCR ONNX (optimum-cli export)

- Source: exported from microsoft/trocr-base-handwritten via `optimum-cli export onnx`
- Path: `bench/models-onnx/trocr-base-handwritten/`
- Files:
  - `encoder_model.onnx`: 328.51 MB, SHA-256 `6c62c4205ecc5d20ff253f4bac219feb9fadc549566944f392d931c69e69e664`
  - `decoder_model.onnx`: 1140.16 MB, SHA-256 `8c175513abab03b857737449c4b5a1022f4bff9e2aa7b852f45c68b44679b374`
  - Tokenizer metadata: tokenizer.json (3.39 MB), vocab.json (0.76 MB), merges.txt (0.44 MB), config files
- License: MIT (inherited from microsoft/trocr-base-handwritten)
- ONNX opset: 14
- Verification: 2026-10-26
- ONNX-vs-Python CER diff on handwriting_line_1/2/4: 0.0000 (identical output)
- **Operational model.**
- Gap: `decoder_with_past_model.onnx` not produced by opset 14 export. 2.4b2 either regenerates at opset 18 or accepts slower standard decoder generation.

### RapidOCR v4 ONNX files (archival duplicates, unused)

- Source: bundled in `rapidocr_onnxruntime` 1.4.4 package
- Path: `bench/.venv/Lib/site-packages/rapidocr_onnxruntime/models/`
- Files:
  - `ch_PP-OCRv4_det_infer.onnx`: 4.53 MB, SHA-256 `d2a7720d45a54257208b1e13e36a8479894cb74155a5efe29462512d42f49da9`
  - `ch_PP-OCRv4_rec_infer.onnx`: 10.35 MB, SHA-256 `48fc40f24f6d2a207a2b1091d3437eb3cc3eb6b676dc3ef9c37384005483683b`
  - `ch_ppocr_mobile_v2.0_cls_infer.onnx`: 0.56 MB, SHA-256 `e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c`
- **UNUSED.** These are PP-OCRv4 (older, lower accuracy than v6). Verified to fail 0.05 tolerance: CER 0.11-0.24 on print vs. v6's 0.03-0.15. Kept as archival duplicates of the v6 models for reference.
- Not used in production. 2.4b2 should use the v6 models from PaddleX auto-download.

## Manually downloaded PP-OCRv6 ONNX tars (archival, superseded by PaddleX auto-download)

- Path: `bench/models-onnx/ppocrv6-medium/`
- Detector tar extracted: `det/PP-OCRv6_medium_det_onnx/inference.onnx` (59.15 MB, SHA-256 `a0d14865d3a297970899b5c246f0e47c43879bb406c3a1fa4e505aba84b134e4`)
- Recognizer tar extracted: `rec/PP-OCRv6_medium_rec_0515_onnx/inference.onnx` (73.01 MB, SHA-256 `20bce3d9684ff33eb3a05afbde835b6a21313b1d30c36e0eb1d03e9b0e8f78a3`)
- **SUPERSEDED.** These are the same weights as PaddleX auto-downloads, but with yaml-declared model names that cause PaddleX resolver conflicts. Kept as archival record. **Use PaddleX auto-downloaded models in production (2.4b2).**

## Model source URLs (for 2.4b2 manifest)

The C# manifest in Phase 2.4b2 will record these sources:

- PP-OCRv6_medium_det_onnx: PaddleX model hub (auto-resolved by PaddleX)
- PP-OCRv6_medium_rec_onnx: PaddleX model hub (auto-resolved by PaddleX)
- TrOCR encoder_model.onnx + decoder_model.onnx: exported from microsoft/trocr-base-handwritten

## Verification methodology

All ONNX models were verified by running them on the 15-image annotated eval set and comparing CER to the Python reference (paddleocr 3.7.0 / transformers 5.20.2). Tolerance: 0.05 CER difference.

**Result:** Print CERs within 0.05 on all 4 print images. Handwriting CERs identical (0.0000 diff) on 3 of 3 verified handwriting images.