# filepath: bench/harness/report.py
# purpose:  Write per-candidate JSON and aggregate summary markdown.
import json
from pathlib import Path


def write_summary(out_dir: Path, summary: dict, date_tag: str, version: str) -> None:
    # 1) One JSON per candidate
    for name, data in summary.items():
        p = out_dir / f"{name}-{date_tag}.json"
        with p.open("w", encoding="utf-8") as f:
            json.dump({"harness_version": version, "candidate": name, "data": data}, f, indent=2)

    # 2) selection.md skeleton (metrics are filled in at 2.4a closure by the
    # operator after reviewing the JSON reports).
    sel = out_dir / "selection.md"
    if not sel.exists():
        sel.write_text(
            "# Phase 2.4a selection report\n\n"
            "Status: pending operator analysis. The per-candidate JSON files "
            "in this directory contain the raw metrics. Fill in the table "
            "below after reviewing them.\n\n"
            "## Accuracy table\n\n"
            "| Category | pp-ocr-only | pp-ocr+tr-ocr | tr-ocr-only |\n"
            "| -------- | ----------- | ------------- | ----------- |\n"
            "| clean_print | | | |\n"
            "| blurry_print | | | |\n"
            "| small_print | | | |\n"
            "| faint_print | | | |\n"
            "| clear_handwriting | | | |\n"
            "| faint_handwriting | | | |\n"
            "| cursive | | | |\n"
            "| mixed_pages | | | |\n"
            "| objects_receipts | | | |\n"
            "| rotated | | | |\n"
            "| multi_column | | | |\n\n"
            "## Selected engines\n\n"
            "(to be filled)\n\n"
            "## Routing strategy\n\n"
            "(to be filled)\n",
            encoding="utf-8",
        )

    # 3) limitations.md skeleton
    lim = out_dir / "limitations.md"
    if not lim.exists():
        lim.write_text(
            "# Phase 2.4a limitations\n\n"
            "Categories where no candidate reaches acceptable accuracy. "
            "One row per limitation, with the metric and the reason.\n\n"
            "| Category | Reason |\n"
            "| -------- | ------ |\n",
            encoding="utf-8",
        )

    # 4) models.md skeleton
    mod = out_dir / "models.md"
    if not mod.exists():
        mod.write_text(
            "# Phase 2.4a model verification records\n\n"
            "For every model selected, record: source URL, exact revision or "
            "commit, file size, license, SHA-256, ONNX opset, input and output "
            "tensor shapes, tokenizer or dictionary requirements, verification "
            "date and verifier, measured CER/WER on the evaluation set. This is "
            "ImageTextify-052.\n\n"
            "## Selected models\n\n"
            "(to be filled after selection.md is complete)\n",
            encoding="utf-8",
        )