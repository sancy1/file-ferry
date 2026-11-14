filepath: bench/README.md
purpose:  Operator guide for the Phase 2.4a Python evaluation harness.

WHAT THIS FOLDER IS
  A local Python harness that measures the accuracy of candidate OCR engines
  on a labeled set of real images. It is not part of the Avalonia C# app. It
  is not shipped. It is not referenced by any solution file. It is run by an
  operator, produces reports, and stops.

WHAT THIS FOLDER IS NOT
  - Not a NuGet package.
  - Not an ONNX implementation.
  - Not a C# project.
  - Not a runtime dependency of the app.
  - Not committed with eval images. Only the manifest, labels, harness, and
    reports are committed.

PREREQUISITES
  - Python 3.11.9 (verified present on the reference machine).
  - CUDA 11.8 (verified present, Quadro T2000, 4 GB VRAM).
  - 20 GB free disk for model cache.
  - Windows PowerShell 5.1 or later.

INSTALL
  1. From the repository root:
       python -m venv bench\.venv
       bench\.venv\Scripts\python -m pip install --upgrade pip
  2. Install PyTorch with CUDA 11.8 from the official alternate index:
       bench\.venv\Scripts\pip install torch==2.14.1+cu118 --index-url https://download.pytorch.org/whl/cu118
  3. Install the rest from requirements.txt (torch will already be present,
     so pip skips it):
       bench\.venv\Scripts\pip install -r bench\requirements.txt
  4. Verify CUDA is visible:
       bench\.venv\Scripts\python -c "import torch; print(torch.cuda.is_available())"
     Expected: True.

ADD A CANDIDATE
  1. Implement bench/harness/candidates/base.Candidate in a new file under
     bench/harness/candidates/.
  2. Register the class in bench/harness/candidates/__init__.py.
  3. Add its name to the choices in bench/harness/main.py.
  4. Add its name to the report.md header.

UPDATE THE EVAL-SET MANIFEST
  bench/eval-set/manifest.json has one entry per image:
    {
      "file": "img_001.png",
      "sha256": "<64 hex chars>",
      "category": "<one of the eleven categories>",
      "label_file": "labels/img_001.json",
      "notes": ""
    }
  The manifest.json is committed. The images are not. Place the images
  wherever the operator chooses and pass --eval-dir <that path>.

RUN A CANDIDATE
  From the repository root:
    bench\.venv\Scripts\python -m bench.harness.main --eval-dir <path> --candidate pp-ocr-only
    bench\.venv\Scripts\python -m bench.harness.main --eval-dir <path> --candidate pp-ocr+tr-ocr
    bench\.venv\Scripts\python -m bench.harness.main --eval-dir <path> --candidate tr-ocr-only
    bench\.venv\Scripts\python -m bench.harness.main --eval-dir <path> --candidate all

  The Python package is bench.harness. Running with -m requires the
  repository root on sys.path, which -m sets automatically when the cwd is
  the repository root.

WHERE REPORTS GO
  bench/reports/<candidate>-<date>.json   per-candidate metrics
  bench/reports/selection.md              the accuracy table and selected engines
  bench/reports/models.md                 the model verification records
  bench/reports/limitations.md            categories where no candidate reaches acceptable accuracy

PROVISIONAL ACCURACY THRESHOLDS
  - clean_print, small_print, rotated:      CER <= 0.05
  - blurry_print, faint_print:              CER <= 0.15
  - clear_handwriting:                      CER <= 0.20
  - faint_handwriting, cursive:             CER <= 0.35 (or a limitation)
  - objects_receipts:                       CER <= 0.15
  - multi_column, mixed_pages:              CER <= 0.15 AND line_detection_rate >= 0.9

  Threshold changes are recorded in bench/reports/selection.md and opened
  as ImageTextify-107.

CUDA NOTES
  Both paddlepaddle-gpu and torch must use CUDA 11.8 on this machine.
  paddlepaddle-gpu 3.3.1 ships CUDA 11.8 and 12.x wheels. Pin the 11.8
  wheel for both. Mixed CUDA versions will fail at runtime with a
  shared library error that is difficult to diagnose.

FAILURE HANDLING
  Each candidate is isolated. If install or load fails, that candidate is
  recorded as "skipped" with a reason. The other candidates still run.
  Partial results are committed with the failure recorded in the same
  report. No candidate is silently dropped.

CANDIDATE NAMES (four, per ImageTextify-103)
  pp-ocr-only         PP-OCR detector + recognizer (PP-OCR family, C1+C3)
  pp-ocr+tr-ocr       PP-OCR detector + TrOCR Handwritten recognizer (C2)
  tr-ocr-only         TrOCR Handwritten only (C4)
  all                 Run all three of the above.

  The VLM through Ollama is NOT a candidate in Phase 2.4a. It is deferred
  to Phase 2.4c per ImageTextify-103.