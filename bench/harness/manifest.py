# filepath: bench/harness/manifest.py
# purpose:  Load and minimally validate the eval-set manifest.
import json
from pathlib import Path


def load_manifest(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if data.get("schema_version") != "1.0":
        raise ValueError(f"Unsupported manifest schema_version: {data.get('schema_version')}")
    entries = data.get("entries")
    if not isinstance(entries, list):
        raise ValueError("manifest.entries must be a list")
    for i, e in enumerate(entries):
        for key in ("file", "sha256", "category", "label_file"):
            if key not in e:
                raise ValueError(f"manifest entry {i} missing key {key}")
    return data