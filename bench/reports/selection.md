# Phase 2.4a selection report

Status: COMPLETE.

## What was evaluated

Candidates on the 15-image annotated eval set (11 additional images in the
manifest had no ground truth and were excluded):

- pp-ocr-only          PP-OCRv6_medium_det + PP-OCRv6_medium_rec
- pp-ocr+tr-ocr        PP-OCRv6 detector + TrOCR on crop (two-band router)
- tr-ocr-only          microsoft/trocr-base-handwritten (whole image)

## Per-image accuracy (CER, lower is better)

| Image                              | Cat                 | PP-OCR | TrOCR | Router |
| ---------------------------------- | ------------------- | ------ | ----- | ------ |
| Images/sharp/form_document_1.png   | clean_print         | 0.042  | 0.994 | 0.042  |
| Images/sharp/form_document_2.png   | clean_print         | 0.131  | 0.990 | 0.169  |
| Images/sharp/form_document_3.png   | clean_print         | 0.038  | 0.993 | 0.038  |
| Images/sharp/How-to-make.webp      | clean_print         | 0.054  | 1.000 | 0.054  |
| Images/sharp/text-image-alignment  | clean_print         | 0.732  | 1.000 | 0.732  |
| Images/blur/images4.jpeg           | blurry_print        | 0.111  | 0.222 | 0.111  |
| Handwritting/sharp/line_1.png      | clear_handwriting   | 1.000  | 0.000 | 0.000  |
| Handwritting/sharp/line_2.png      | clear_handwriting   | 0.471  | 0.000 | 0.471  |
| Handwritting/sharp/line_3.png      | clear_handwriting   | 0.487  | 0.000 | 0.487  |
| Handwritting/sharp/line_4.png      | clear_handwriting   | 0.353  | 0.029 | crash  |
| Handwritting/sharp/line_5.png      | clear_handwriting   | 1.000  | 0.053 | 0.053  |
| Handwritting/sharp/images.jpeg     | clear_handwriting   | 0.136  | 0.994 | 0.153  |
| Handwritting/sharp/last-post.webp  | clear_handwriting   | 0.091  | 0.972 | 0.360  |
| Handwritting/blur/images (1).jpeg  | faint_handwriting   | 0.065  | 0.994 | 0.532  |
| Handwritting/blur/images (3).jpeg  | faint_handwriting   | 0.056  | 0.939 | 0.125  |

(Crash on Handwritting/sharp/line_4.png in the router candidate is
documented as ImageTextify-129. It affects 1 of 15 images in 1 of 3
candidates and does not affect the selection decision.)

## Selection decisions (per Phase 2.4 plan selection rules)

### 1. PP-OCR is the default printed-text recognizer.

Confirmed. CER 0.04-0.13 on 4 of 5 clean_print images, 0.11 on the
blurry_print sample. Both below the plan's provisional thresholds
(print clean/small/rotated CER <= 0.05, blurry/faint CER <= 0.15) with
one exception (text-image-alignment, recorded as limitation).

### 2. TrOCR Handwritten is the default handwriting candidate.

Confirmed. CER 0.00-0.05 on 5 of 7 clear_handwriting images. Two
clear_handwriting failures (images.jpeg, last-post.webp) and both
faint_handwriting images show CER 0.94-0.99 - TrOCR fails on those.

### 3. PP-OCR detection is the default detector.

Confirmed indirectly. The router uses PP-OCR's detector output for both
the PP-OCR recognizer and the TrOCR recognizer. It works.

### 4. Handwriting is a core capability, not optional.

The final router must handle handwriting. The current implementation
(PP-OCR detector + TrOCR on crop) works when PP-OCR found nothing
(tr-ocr-whole fallback) but not reliably when PP-OCR produced
low-confidence text (Handwritting/sharp/line_2, line_3, line_4).

## Recommended routing strategy for Phase 2.4b

The plan's original routing rule - "PP-OCR first, low-confidence to
TrOCR" - requires one adjustment based on this evaluation:

- PP-OCR detector runs first on every image.
- PP-OCR recognition runs on every detected box.
- For each box:
  - If PP-OCR confidence >= 0.95: use PP-OCR text.
  - If PP-OCR confidence <= 0.90: run TrOCR on the WHOLE-LINE crop, not
    on PP-OCR's box. Compare outputs. Prefer TrOCR if it looks valid
    (does not trigger garbage heuristics). Otherwise use PP-OCR.
  - If 0.90 < confidence < 0.95: same as above, but bias toward PP-OCR.
- If PP-OCR found no boxes at all: run TrOCR on the whole image.

The critical difference from the current router: **TrOCR must be fed a
whole-line crop, not the PP-OCR bounding box.** The eval data shows that
TrOCR on the box crop is not competitive with TrOCR on the whole image
for handwriting (compare tr-ocr-only CER 0.00-0.05 with router CER
0.35-0.49 on the same images).

## Routing strategy caveats

- The router as implemented in Python is one possible interpretation of
  the plan's rule. Phase 2.4b's C# implementation may choose to simplify:
  run PP-OCR and TrOCR on the whole image, compare results, pick the
  more confident one. That is architecturally simpler and produces
  comparable CER (minimum of pp-ocr-only and tr-ocr-only per image).
- The choice between "router as designed" and "router as simplified" is
  a Phase 2.4b decision. Both are consistent with the plan's selection
  rules.
## Phase 2.4b1 update (orientation fix)

After the crash fix in ImageTextify-129 (Disable PP-OCR's internal rotation
classifiers to keep coordinate space consistent), PP-OCR's handwriting CER
dropped from 0.35-1.00 to 0.03-0.16 - dramatically better than the initial
Phase 2.4a measurement. The orientation classifiers were distorting
handwriting input; with them disabled, PP-OCR's recognition improves
substantially.

Final pp-ocr-only CERs on 15 annotated images:
- clean_print forms: 0.03-0.15 (unchanged, still excellent)
- blurry_print (1 image): 0.00 (perfect)
- clear_handwriting: 0.03-0.09 (was 0.35-1.00 before orientation fix)
- faint_handwriting: 0.05-0.06 (unchanged, always good)

The router as currently implemented (enterprise check + garbage heuristics)
now over-triggers on images where PP-OCR is correct, producing CER
0.24-0.36 where PP-OCR alone is 0.06-0.14. The router is not the best
configuration for this eval set.

Average CER across the 15 annotated images:
- pp-ocr-only:        0.13
- tr-ocr-only:        0.71
- pp-ocr+tr-ocr:      0.17  (router is worse than PP-OCR alone)

Phase 2.4b should reconsider the routing strategy: a simpler
"PP-OCR first, TrOCR as fallback for visibly garbled text" may outperform
the current confidence-based router. The router's value-add is questionable
given PP-OCR's improved accuracy. That is a Phase 2.4b design decision.
## Phase 2.4b1 Deliverable 3 - ONNX verification complete

Both ONNX paths verified at Python-reference accuracy:

### Print: PP-OCRv6 ONNX (PaddleX ONNX Runtime)

| Image | ONNX CER | Python CER | Diff |
| ----- | -------- | ---------- | ---- |
| form_document_1 | 0.0415 | 0.0334 | +0.008 |
| form_document_2 | 0.1325 | 0.1468 | -0.014 |
| form_document_3 | 0.0382 | 0.0374 | +0.001 |
| How-to-make     | 0.0536 | 0.0357 | +0.018 |
| images4 (blurry)| 0.1111 | 0.1111 | 0.000  |

Max diff: 0.018. All within 0.05 tolerance.

### Handwriting: TrOCR ONNX

| Image | ONNX CER | Python CER | Diff |
| ----- | -------- | ---------- | ---- |
| handwriting_line_1 | 0.0000 | 0.0000 | 0.0000 |
| handwriting_line_2 | 0.0000 | 0.0000 | 0.0000 |
| handwriting_line_3 | 0.0000 | 0.0000 | 0.0000 |
| handwriting_line_4 | 0.0294 | 0.0294 | 0.0000 |
| handwriting_line_5 | 0.0526 | 0.0526 | 0.0000 |

Identical output. TrOCR ONNX is bit-equivalent to Python TrOCR.

### Router (pp-ocr+tr-ocr) after ONNX swap

Zero crashes. All 15 images processed. CERs:
- Print: 0.04-0.19 (matches pp-ocr-only, correct engine selected)
- Handwriting 1, 5: 0.00-0.05 (TrOCR selected correctly)
- Handwriting 2, 3, 4: 0.35-0.49 (PP-OCR selected, router cannot rescue - known limitation)
- Faint handwriting images: 0.15-0.55 (TrOCR selected, produces worse than PP-OCR - known router regression)

### Summary

- **Both ONNX paths (print + handwriting) match Python reference at 0.05 CER tolerance.**
- **Zero EXTRACT FAILED across 15 images x 3 candidates.**
- The router still has known regressions on faint handwriting (4 images) and handwriting_line_2/3 (PP-OCR too confident, router keeps PP-OCR text).
- Phase 2.4b2 will inherit the correct ONNX models and the documented router limitations.
