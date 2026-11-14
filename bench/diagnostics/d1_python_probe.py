# filepath: bench/diagnostics/d1_python_probe.py
# purpose:  D1 diagnostic. Run PaddleOCR reference on form_document_1.png.
#           Save raw probability map (as .npy), final boxes (as JSON),
#           original dimensions, and preprocessing parameters so that
#           C# Test A can load the exact same intermediate data and run
#           only its own post-processing.
#
# Run with the bench venv python:
#   bench\.venv\Scripts\python.exe bench\diagnostics\d1_python_probe.py
#
# Outputs (all in bench/diagnostics/output/):
#   form_document_1.png_pred.npy     raw detector probability map
#   form_document_1.png_boxes.json   final Python boxes
#   form_document_1.png_meta.json    original and preprocessed dimensions

import json
import sys
from pathlib import Path

import numpy as np

print("=== D1: Python probe ===", flush=True)
print(f"Python: {sys.version.split()[0]}", flush=True)

# Outputs directory
out_dir = Path(__file__).parent / "output"
out_dir.mkdir(exist_ok=True)
print(f"Output dir: {out_dir}", flush=True)

# Input image
image_path = Path(r"C:\Users\HP\Desktop\exracts\Images\sharp\form_document_1.png")
print(f"Input: {image_path}", flush=True)
print(f"Input exists: {image_path.is_file()}", flush=True)

if not image_path.is_file():
    print("ABORT: input image not found", flush=True)
    sys.exit(1)

# Import PaddleOCR
try:
    from paddleocr import PaddleOCR
except ImportError as e:
    print(f"ABORT: paddleocr not importable: {e}", flush=True)
    sys.exit(1)

# Construct PaddleOCR exactly as the eval harness does
# (from bench/harness/candidates/pp_ocr_only.py)
print("Loading PaddleOCR (lang=en) with default pipeline...", flush=True)
ocr = PaddleOCR(lang="en")
print("Loaded.", flush=True)

# Run inference. We need the raw detector output, not just recognition.
# PaddleOCR 3.7.0's ocr.ocr() call hides detector intermediates.
# We need to use the underlying pipeline to access them.
# Inspect what PaddleOCR exposes.

# Try the .predict() approach which may expose more
print("", flush=True)
print("Attempting to access detector internals...", flush=True)

# PaddleOCR 3.7.0 structure: ocr.ocr(path) -> list[OCRResult]
# OCRResult is a dict-like with dt_polys, rec_texts, rec_scores, etc.
# The raw probability map is not exposed by the high-level API.
# The only way to get it is via the underlying pipeline.

# Inspect ocr attributes to find the detector pipeline
attrs = [a for a in dir(ocr) if not a.startswith('_')]
print(f"PaddleOCR attributes: {attrs}", flush=True)

# Try to find the text_detector
text_det = None
for attr_name in ['text_detector', 'text_det', 'det_model', '_text_detector']:
    if hasattr(ocr, attr_name):
        text_det = getattr(ocr, attr_name)
        print(f"Found detector attribute: {attr_name} = {type(text_det).__name__}", flush=True)
        break

if text_det is None:
    print("", flush=True)
    print("WARNING: raw probability map not directly accessible via PaddleOCR", flush=True)
    print("Fallback: save the final boxes and original dimensions only.", flush=True)
    print("Test A will not be possible without the raw pred.", flush=True)
    print("Recording this limitation.", flush=True)

# Regardless of whether pred is accessible, get the final boxes
print("", flush=True)
print("Running full OCR pipeline...", flush=True)
result = ocr.ocr(str(image_path))

if not result or not result[0]:
    print("ABORT: OCR returned no result", flush=True)
    sys.exit(1)

item = result[0]
boxes = []
rec_boxes = item.get("rec_boxes", [])
rec_texts = item.get("rec_texts", [])
rec_scores = item.get("rec_scores", [])

for i in range(len(rec_boxes)):
    box = rec_boxes[i]
    if hasattr(box, 'tolist'):
        box = box.tolist()
    boxes.append({
        "index": i,
        "bbox": [int(v) for v in box],
        "text": rec_texts[i] if i < len(rec_texts) else "",
        "score": float(rec_scores[i]) if i < len(rec_scores) else 0.0,
    })

# Original dimensions
import cv2
img = cv2.imread(str(image_path))
orig_h, orig_w = img.shape[:2]

# Save boxes
boxes_path = out_dir / "form_document_1_boxes.json"
with open(boxes_path, "w") as f:
    json.dump({
        "image_path": str(image_path),
        "original_width": orig_w,
        "original_height": orig_h,
        "box_count": len(boxes),
        "boxes": boxes,
    }, f, indent=2)
print(f"Saved boxes: {boxes_path} ({len(boxes)} boxes)", flush=True)

# Attempt to save raw pred
pred_saved = False
if text_det is not None:
    try:
        # PaddleOCR 3.7.0's text_detector pipeline has a .predict() method
        # that returns a result with the probability map.
        det_result = text_det.predict(str(image_path))
        if det_result:
            det_item = det_result[0]
            # Look for the pred attribute
            keys = [k for k in det_item.keys() if 'pred' in k.lower() or 'prob' in k.lower() or 'map' in k.lower()]
            print(f"Detector result keys matching pred/prob/map: {keys}", flush=True)
            print(f"All detector result keys: {list(det_item.keys())}", flush=True)

            if keys:
                pred = det_item[keys[0]]
                if hasattr(pred, 'numpy'):
                    pred = pred.numpy()
                pred_path = out_dir / "form_document_1_pred.npy"
                np.save(pred_path, pred)
                print(f"Saved pred: {pred_path} shape={pred.shape} dtype={pred.dtype}", flush=True)
                pred_saved = True
    except Exception as e:
        print(f"WARNING: could not save raw pred: {type(e).__name__}: {e}", flush=True)

# Save metadata
meta = {
    "image_path": str(image_path),
    "original_width": orig_w,
    "original_height": orig_h,
    "pred_saved": pred_saved,
    "pred_path": str(out_dir / "form_document_1_pred.npy") if pred_saved else None,
    "boxes_path": str(boxes_path),
}
meta_path = out_dir / "form_document_1_meta.json"
with open(meta_path, "w") as f:
    json.dump(meta, f, indent=2)
print(f"Saved meta: {meta_path}", flush=True)

print("", flush=True)
print("=== D1 COMPLETE ===", flush=True)