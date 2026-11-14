# filepath: bench/harness/labels.py
# purpose:  Load and minimally validate a per-image label file.
import json
from pathlib import Path


def load_labels(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if data.get("schema_version") != "1.0":
        raise ValueError(f"Unsupported labels schema_version: {data.get('schema_version')}")
    if "full_text" not in data:
        raise ValueError(f"{path} missing full_text")
    return data