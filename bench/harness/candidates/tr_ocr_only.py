# filepath: bench/harness/candidates/tr_ocr_only.py
# purpose:  Candidate tr-ocr-only (C4: TrOCR Handwritten recognizer, whole image as one crop).
#
# TrOCR is a recognizer, not a detector. To run "TrOCR only" we feed the whole
# image as a single crop. This is the plan's C4 configuration and will perform
# poorly on multi-line images; it exists to measure exactly that.
from pathlib import Path
from bench.harness.candidates.base import Candidate, LineResult


class TrOcrOnlyCandidate(Candidate):
    name = "tr-ocr-only"

    def load(self) -> None:
        from transformers import TrOCRProcessor, VisionEncoderDecoderModel
        import torch

        self.version_info = {
            "torch_package": "2.14.1+cu118",
            "transformers_package": "5.20.2",
            "recognizer": "microsoft/trocr-base-handwritten",
        }
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.processor = TrOCRProcessor.from_pretrained("microsoft/trocr-base-handwritten")
        self.model = VisionEncoderDecoderModel.from_pretrained("microsoft/trocr-base-handwritten").to(self.device).eval()

    def extract(self, image_path: Path) -> list[LineResult]:
        from PIL import Image
        import torch

        img = Image.open(image_path).convert("RGB")
        w, h = img.size
        pixel_values = self.processor(images=img, return_tensors="pt").pixel_values.to(self.device)
        with torch.no_grad():
            generated_ids = self.model.generate(pixel_values)
        text = self.processor.batch_decode(generated_ids, skip_special_tokens=True)[0]
        return [LineResult(bbox=(0, 0, w, h), text=text, confidence=None, engine="tr-ocr")]