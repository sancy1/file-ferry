# filepath: bench/harness/candidates/base.py
# purpose:  Abstract Candidate interface and LineResult dataclass.
from abc import ABC, abstractmethod
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class LineResult:
    """A single recognized line: bbox in (x, y, w, h), text, confidence, engine id."""
    bbox: tuple[int, int, int, int]
    text: str
    confidence: float | None
    engine: str


class Candidate(ABC):
    """One OCR engine configuration under evaluation."""
    name: str = ""
    version_info: dict = {}

    @abstractmethod
    def load(self) -> None:
        """Load models. Called once before extract(). May be slow."""

    @abstractmethod
    def extract(self, image_path: Path) -> list[LineResult]:
        """Run detection + recognition on one image. Return lines in reading order."""

    def teardown(self) -> None:
        """Release resources. Called once after all extracts."""
        pass