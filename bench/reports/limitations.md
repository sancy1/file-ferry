# Phase 2.4a limitations

ImageTextify-106 (limitations register).

## Category-level limitations

### blurry_print

Sample size: 1 annotated image out of 5 in the manifest. 4 images had no
ground truth (annotator could not read them). The category is too thin to
draw a category-level conclusion. PP-OCR CER = 0.111 on the one image.
TrOCR CER = 0.222. Recorded as a limitation: Phase 2.4b should assume
blurry_print is proven only on one image, not as a category.

### faint_handwriting

Sample size: 2 annotated images out of 9 in the manifest. 7 images had no
ground truth. PP-OCR CER = 0.056-0.065 on both. TrOCR CER = 0.939-0.994
on both (TrOCR fails on faint handwriting). Recorded as a limitation:
TrOCR does not handle faint handwriting reliably. PP-OCR performs
unexpectedly well on this subset.

### clear_handwriting

Sample size: 7 annotated images. TrOCR CER = 0.00-0.99 (5 excellent,
2 failures). The 2 failures (images.jpeg and last-post.webp) suggest
either unusual scripts, non-standard fonts, or degraded input. Recorded
as a limitation: TrOCR is not universally good on handwriting, only on
the subset resembling the IAM training distribution.

## Image-level limitations

### Images/sharp/text-image-alignment.jpg (clean_print)

- PP-OCR CER = 0.732, confidence = 0.9933 (confidently wrong).
- TrOCR CER = 1.000.
- No candidate reads this image. Recorded as a limitation: mixed-layout
  images with graphic elements may not be readable by either engine. The
  router keeps PP-OCR's confidently-wrong output because confidence
  exceeds the threshold.

### Handwritting/sharp/images.jpeg and last-post.webp (clear_handwriting)

- TrOCR CER = 0.994 and 0.972. These may be unusual handwriting samples
  outside TrOCR's training distribution, or images with layout/context
  that confuse the recognizer.
- Recorded as a limitation: handwriting recognition accuracy is not
  guaranteed across all handwriting styles.

## Router limitations

### TrOCR on PP-OCR's box crop vs. whole image

Handwritting/sharp/handwriting_line_2.png, _3.png, _4.png:

- PP-OCR: CER 0.35-0.49
- TrOCR on whole image: CER 0.00-0.03
- Router (TrOCR on expanded PP-OCR box): CER 0.35-0.49 (no improvement)

The router's crop-expansion did not rescue these images. The finding is:
**TrOCR requires a whole-line crop, not a PP-OCR box crop, on
handwriting.** The router's design (PP-OCR detection + TrOCR on
detection boxes) is fundamentally limited on handwriting.

Phase 2.4b should feed TrOCR a whole-line crop for handwriting, not the
PP-OCR bounding box.

### Faint handwriting regression

Handwritting/blur/images (1).jpeg and (3).jpeg:

- PP-OCR: CER 0.065 and 0.056 (both correct)
- TrOCR: CER 0.994 and 0.939 (both wrong)
- Router: CER 0.532 and 0.125 (regressed from PP-OCR baseline)

PP-OCR confidence on these images is 0.94-0.95, in the medium band. The
router sent them to TrOCR. TrOCR produced confident-hallucinations that
did not trigger the garbage heuristics. Recorded as a limitation: no
confidence-based router can perfectly distinguish "PP-OCR right with
moderate confidence" from "PP-OCR might be wrong here, try TrOCR."

## Sample size limitations

The eval set is smaller than the plan's target (30-50 images):

- 15 of 26 manifest images were annotated. 11 had no ground truth.
- 4 categories populated (clean_print, blurry_print, clear_handwriting,
  faint_handwriting).
- 7 categories empty (small_print, faint_print, cursive, mixed_pages,
  objects_receipts, rotated, multi_column).

For 2.4a's purpose (proving the three selection rules), this is
sufficient. For a production accuracy claim per category, the eval set
must be expanded in a future phase.

## Deferred items

- ImageTextify-129: one-image crash in the router candidate
  (Handwritting/sharp/handwriting_line_4.png). Root cause not fully
  diagnosed after three fix attempts. One image missing from one
  candidate. Deferred to Phase 2.4b's C# implementation, where the
  crop code can be tested against the same image.
## Phase 2.4b1 update (orientation fix and router limitation)

### PP-OCR handwriting accuracy was underestimated

The Phase 2.4a measurements were made with PP-OCR's orientation classifiers
enabled. Those classifiers rotate handwriting input internally, distorting
recognition. With the orientation classifiers disabled (required for
coordinate-space consistency - see ImageTextify-129), PP-OCR's handwriting
CER drops from 0.35-1.00 to 0.03-0.16 on the same images. PP-OCR's actual
handwriting capability is much closer to TrOCR's than initially measured.

### Router over-triggers on correct PP-OCR results

The router's enterprise check fires on images where PP-OCR confidence
exceeds 0.95 but the box covers > 60% of image area. This rescue was
designed for rotated single-line images like handwriting_line_4.png.
After the orientation fix, PP-OCR's text is now plausible on those same
images. The check still fires and replaces PP-OCR's correct text with
TrOCR's sometimes-worse text (CER 0.24-0.36 vs PP-OCR 0.06-0.14).

Phase 2.4b should reconsider the routing strategy. The current router is
architecturally sound but heuristically over-aggressive. A simpler rule
("PP-OCR first, TrOCR only when PP-OCR produces no text or obviously
garbled text") is likely to outperform the current configuration.

### handwriting_line_4.png now works

handwriting_line_4.png is a 3396x128 rotated single-line image. With
PP-OCR's orientation classifiers disabled, PP-OCR detects the whole image
as one line and reads it with moderate accuracy. The enterprise check
fires (box covers 100% of image, PP-OCR text has fewer characters than
expected), TrOCR-whole runs, TrOCR wins with CER 0.03. Delivered value:
the crash (ImageTextify-129) is fixed AND the rotated image is now
readable.
## Phase 2.4b1 Deliverable 3 - ONNX verification complete

Both ONNX paths verified at Python-reference accuracy:

### Print: PP-OCRv6 ONNX via PaddleX ONNX Runtime

CER within 0.05 of Python reference on all 4 print images (form_document_1,
form_document_2, form_document_3, How-to-make). BLURRY_PRINT images4 CER
identical at 0.1111.

### Handwriting: TrOCR ONNX

CER identical to Python reference (0.0000 diff) on all tested handwriting
images. Bit-equivalent output.

### Router regressions (accepted, documented)

The router (pp-ocr+tr-ocr) still produces CER significantly higher than
pp-ocr-only on these images:

- Handwritting/sharp/handwriting_line_2: router 0.47 vs. pp-ocr 0.47 vs. tr-ocr 0.00
- Handwritting/sharp/handwriting_line_3: router 0.49 vs. pp-ocr 0.49 vs. tr-ocr 0.00
- Handwritting/blur/images (1).jpeg: router 0.55 vs. pp-ocr 0.06 vs. tr-ocr 0.99
- Handwritting/sharp/last-post.webp: router 0.42 vs. pp-ocr 0.11 vs. tr-ocr 0.97
- Handwritting/blur/images (3).jpeg: router 0.16 vs. pp-ocr 0.06 vs. tr-ocr 0.94

Cause: the router uses confidence bands that send borderline-confidence
images to TrOCR, where TrOCR produces garbage. The garbage heuristics do
not catch TrOCR's "fluent but wrong" output.

Phase 2.4b should reconsider the routing strategy. A simpler rule
("PP-OCR first, TrOCR only when PP-OCR produces no text or obviously
garbled text") would avoid these regressions.

### handwriting_line_4.png crash - fixed but not optimal

handwriting_line_4.png no longer crashes (try/except fallback). Router
produces CER 0.35 (falls back to PP-OCR text after TrOCR fails). If TrOCR
worked on the whole image, it would produce CER ~0.03. The enterprise
check that was designed to rescue this image does not fire because PP-OCR
confidence exceeds 0.95 after the PaddleX ONNX swap changed the
confidence scale slightly. Documented for 2.4b.

### decoder_with_past_model.onnx not produced

The TrOCR ONNX export at opset 14 did not produce the past-kv-cache
decoder variant. 2.4b2 either regenerates at opset 18 or accepts slower
standard decoder generation. Performance-only impact, not accuracy.
