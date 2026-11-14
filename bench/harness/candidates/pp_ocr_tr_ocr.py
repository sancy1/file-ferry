# filepath: bench/harness/candidates/pp_ocr_tr_ocr.py
# purpose:  Candidate pp-ocr+tr-ocr: two-band router.
#   PP-OCR: PaddleX ONNX pipeline (PP-OCRv6_medium_det_onnx + rec_onnx)
#   TrOCR:  ONNX Runtime (encoder_model.onnx + decoder_model.onnx)
#
# Router bands:
#   conf >= 0.95          -> PP-OCR text
#   0.90 < conf < 0.95    -> run both, prefer TrOCR unless garbage
#   conf <= 0.90          -> TrOCR, fallback to PP-OCR if garbage
#   no PP-OCR boxes found -> TrOCR on whole image
#
# Enterprise check: single box covering > 60% of image -> TrOCR-whole
# if PP-OCR text is implausible (few chars for box width).
from pathlib import Path
import re
import numpy as np
from bench.harness.candidates.base import Candidate, LineResult


HIGH_CONF = 0.95
LOW_CONF = 0.90


def _looks_like_garbage(text: str, crop_width: int, ppocr_text: str = "") -> bool:
    if not text or len(text.strip()) == 0:
        return True
    t = text.strip()
    if re.fullmatch(r"(.)\1{4,}", t):
        return True
    if any(not c.isprintable() for c in t):
        return True
    if len(t) < max(2, crop_width // 30):
        return True
    if len(t) > crop_width // 2:
        return True
    if ppocr_text:
        p = ppocr_text.strip()
        if p:
            longer = max(len(t), len(p))
            if longer > 0 and abs(len(t) - len(p)) > 0.5 * longer:
                return True
            set_t, set_p = set(t.lower()), set(p.lower())
            common = len(set_t & set_p)
            total = max(len(set_t), len(set_p))
            if total > 0 and common / total < 0.3:
                return True
    return False


def _crop_for_tr_ocr(img, x: int, y: int, w: int, h: int):
    pad_x = max(5, int(w * 0.10))
    pad_y = max(5, int(h * 0.10))
    ex0 = max(0, x - pad_x)
    ey0 = max(0, y - pad_y)
    ex1 = min(img.width, x + w + pad_x)
    ey1 = min(img.height, y + h + pad_y)
    if ex1 <= ex0 or ey1 <= ey0:
        rx0 = max(0, min(img.width - 1, x))
        ry0 = max(0, min(img.height - 1, y))
        rx1 = max(0, min(img.width, x + w))
        ry1 = max(0, min(img.height, y + h))
        if rx1 <= rx0 or ry1 <= ry0:
            return img.copy()
        return img.crop((rx0, ry0, rx1, ry1))
    return img.crop((ex0, ey0, ex1, ey1))


class PPOcrTrOcrCandidate(Candidate):
    name = "pp-ocr+tr-ocr"

    def load(self) -> None:
        from paddlex.inference import create_pipeline
        import onnxruntime as ort
        from transformers import TrOCRProcessor

        self.version_info = {
            "paddlex_package": "3.7.2",
            "paddleocr_package": "3.7.0",
            "transformers_package": "4.57.6",
            "pp_ocr_engine": "onnxruntime",
            "pp_ocr_detector": "PP-OCRv6_medium_det_onnx",
            "pp_ocr_recognizer": "PP-OCRv6_medium_rec_onnx",
            "trocr_onnx_dir": "bench/models-onnx/trocr-base-handwritten",
            "high_conf_threshold": HIGH_CONF,
            "low_conf_threshold": LOW_CONF,
        }

        # PP-OCR via PaddleX ONNX
        self.pp_pipeline = create_pipeline(
            pipeline="OCR", engine="onnxruntime", device="cpu",
        )

        # TrOCR via ONNX
        model_dir = Path("C:/Users/HP/Desktop/file-ferry/bench/models-onnx/trocr-base-handwritten")
        self.trocr_processor = TrOCRProcessor.from_pretrained(str(model_dir), use_fast=False)
        self.trocr_encoder = ort.InferenceSession(
            str(model_dir / "encoder_model.onnx"),
            providers=["CPUExecutionProvider"],
        )
        self.trocr_decoder = ort.InferenceSession(
            str(model_dir / "decoder_model.onnx"),
            providers=["CPUExecutionProvider"],
        )
        self.trocr_bos = self.trocr_processor.tokenizer.cls_token_id or 0
        self.trocr_eos = self.trocr_processor.tokenizer.sep_token_id or 2

    def _tr_ocr(self, pil_crop) -> str:
        pixel_values = self.trocr_processor(images=pil_crop, return_tensors="np").pixel_values
        encoder_hidden = self.trocr_encoder.run(None, {"pixel_values": pixel_values})[0]
        input_ids = np.array([[self.trocr_bos]], dtype=np.int64)
        generated = []
        for _ in range(20):
            logits = self.trocr_decoder.run(None, {
                "input_ids": input_ids,
                "encoder_hidden_states": encoder_hidden,
            })[0]
            next_token = int(np.argmax(logits[0, -1, :]))
            generated.append(next_token)
            if next_token == self.trocr_eos:
                break
            input_ids = np.concatenate([input_ids, np.array([[next_token]], dtype=np.int64)], axis=1)
        return self.trocr_processor.tokenizer.decode(generated, skip_special_tokens=True).strip()

    def extract(self, image_path: Path) -> list[LineResult]:
        from PIL import Image

        det_results = self.pp_pipeline.predict(str(image_path))
        img = Image.open(image_path).convert("RGB")

        boxes = []
        texts = []
        scores = []
        if det_results:
            for item in det_results:
                rec_texts = item.get("rec_texts", [])
                rec_scores = item.get("rec_scores", [])
                rec_boxes = item.get("rec_boxes", [])
                for i in range(len(rec_texts)):
                    boxes.append(rec_boxes[i] if i < len(rec_boxes) else None)
                    texts.append(rec_texts[i])
                    scores.append(float(rec_scores[i]) if i < len(rec_scores) else 0.0)

        if not boxes:
            w, h = img.size
            try:
                text = self._tr_ocr(img)
            except Exception:
                text = ""
            if not text:
                return []
            return [LineResult(bbox=(0, 0, w, h), text=text,
                               confidence=None, engine="tr-ocr-whole")]

        if len(boxes) == 1 and boxes[0] is not None:
            x0, y0, x1, y1 = (int(v) for v in boxes[0])
            bw, bh = max(0, x1 - x0), max(0, y1 - y0)
            box_area = bw * bh
            img_area = img.width * img.height
            if img_area > 0 and box_area / img_area > 0.60:
                ppocr_t = texts[0] if texts else ""
                try:
                    tr_whole = self._tr_ocr(img)
                except Exception:
                    tr_whole = ""
                ppocr_expected_min = max(4, bw // 30)
                ppocr_implausible = len(ppocr_t.strip()) < ppocr_expected_min
                tr_plausible = tr_whole and not _looks_like_garbage(tr_whole, img.width, "")
                if tr_plausible and ppocr_implausible:
                    return [LineResult(bbox=(0, 0, img.width, img.height),
                                       text=tr_whole, confidence=None,
                                       engine="tr-ocr-whole")]

        lines: list[LineResult] = []
        for i, box in enumerate(boxes):
            if box is None:
                continue
            xmin, ymin, xmax, ymax = (int(v) for v in box)
            if xmax <= xmin or ymax <= ymin:
                continue
            x, y = xmin, ymin
            w, h = xmax - xmin, ymax - ymin
            if w < 4 or h < 4:
                continue
            ppocr_text = texts[i] if i < len(texts) else ""
            ppocr_conf = scores[i] if i < len(scores) else 0.0

            if ppocr_conf >= HIGH_CONF:
                lines.append(LineResult(bbox=(x, y, w, h), text=ppocr_text,
                                        confidence=ppocr_conf, engine="pp-ocr"))
                continue

            crop = _crop_for_tr_ocr(img, x, y, w, h)
            try:
                tr_text = self._tr_ocr(crop)
            except Exception:
                tr_text = ""
            is_garbage = _looks_like_garbage(tr_text, w, ppocr_text)

            if ppocr_conf <= LOW_CONF:
                if tr_text and not is_garbage:
                    lines.append(LineResult(bbox=(x, y, w, h), text=tr_text,
                                            confidence=None, engine="tr-ocr"))
                else:
                    lines.append(LineResult(bbox=(x, y, w, h), text=ppocr_text,
                                            confidence=ppocr_conf, engine="pp-ocr-fallback"))
                continue

            if tr_text and not is_garbage:
                lines.append(LineResult(bbox=(x, y, w, h), text=tr_text,
                                        confidence=None, engine="tr-ocr-compared"))
            else:
                lines.append(LineResult(bbox=(x, y, w, h), text=ppocr_text,
                                        confidence=ppocr_conf, engine="pp-ocr-compared"))

        return lines