# filepath: bench/harness/candidates/pp_ocr_only.py
# purpose:  Candidate pp-ocr-only using PaddleX ONNX Runtime pipeline.
#
# Uses paddlex.inference.create_pipeline with engine="onnxruntime".
# PaddleX auto-downloads PP-OCRv6_medium_det_onnx and
# PP-OCRv6_medium_rec_onnx to ~/.paddlex/official_models/ and manages
# preprocessing, post-processing, and CTC decode.
#
# Output structure (PaddleX OCRResult):
#   result is a list of OCRResult objects (dict-like)
#   rec_texts : list[str]      recognized text per box
#   rec_scores: list[float]    confidence per text (0-1)
#   rec_boxes : ndarray (N,4)  [xmin, ymin, xmax, ymax] per box
from pathlib import Path
from bench.harness.candidates.base import Candidate, LineResult


class PPOcrOnlyCandidate(Candidate):
    name = "pp-ocr-only"

    def load(self) -> None:
        from paddlex.inference import create_pipeline
        self.version_info = {
            "paddlex_package": "3.7.2",
            "paddleocr_package": "3.7.0",
            "engine": "onnxruntime",
            "detector": "PP-OCRv6_medium_det_onnx (auto-downloaded by PaddleX)",
            "recognizer": "PP-OCRv6_medium_rec_onnx (auto-downloaded by PaddleX)",
        }
        self.pipeline = create_pipeline(
            pipeline="OCR",
            engine="onnxruntime",
            device="cpu",
        )

    def extract(self, image_path: Path) -> list[LineResult]:
        results = self.pipeline.predict(str(image_path))
        lines: list[LineResult] = []
        if not results:
            return lines
        for item in results:
            texts = item.get("rec_texts", [])
            scores = item.get("rec_scores", [])
            boxes = item.get("rec_boxes", [])
            for i in range(len(texts)):
                text = texts[i]
                score = float(scores[i]) if i < len(scores) else None
                if i < len(boxes):
                    xmin, ymin, xmax, ymax = (int(v) for v in boxes[i])
                    x, y = xmin, ymin
                    w, h = xmax - xmin, ymax - ymin
                else:
                    x, y, w, h = 0, 0, 0, 0
                lines.append(LineResult(bbox=(x, y, w, h), text=text,
                                        confidence=score, engine="pp-ocr"))
        return lines