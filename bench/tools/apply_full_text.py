# filepath: bench/tools/apply_full_text.py
# purpose:  Apply filled-in ground-truth text template to the harness label files.
#
# Usage (from any directory):
#   C:\Users\HP\Desktop\file-ferry\bench\.venv\Scripts\python.exe ^
#     C:\Users\HP\Desktop\file-ferry\bench\tools\apply_full_text.py ^
#     --manifest C:\Users\HP\Desktop\file-ferry\bench\eval-set\manifest.json ^
#     --template C:\Users\HP\Desktop\file-ferry\bench\eval-set\full_text_input.txt ^
#     --labels-dir C:\Users\HP\Desktop\file-ferry\bench\eval-set\labels
#
# Reads the template, splits by --- separator, matches each [header] to the
# corresponding manifest entry, and writes full_text into the label JSON.
# Does not modify lines[] (remains whatever is currently there, usually []).
# Blank sections are recorded as skipped (full_text = "").
import argparse
import json
import sys
from pathlib import Path


def parse_template(text: str) -> dict[str, str]:
    """Return {relative-path: text-content}. Empty content means skipped."""
    result: dict[str, str] = {}
    current_header: str | None = None
    current_lines: list[str] = []

    for raw in text.splitlines():
        line = raw.rstrip("\r")
        if line.startswith("#"):
            # comment line, ignore
            continue
        if line.startswith("[") and line.endswith("]"):
            # new section; commit previous
            if current_header is not None:
                result[current_header] = "\n".join(current_lines).strip()
            current_header = line[1:-1].strip()
            current_lines = []
        elif line.strip() == "---":
            # section separator; commit and reset
            if current_header is not None:
                result[current_header] = "\n".join(current_lines).strip()
                current_header = None
                current_lines = []
        else:
            if current_header is not None:
                current_lines.append(line)

    if current_header is not None:
        result[current_header] = "\n".join(current_lines).strip()

    return result


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="apply_full_text")
    ap.add_argument("--manifest", required=True, type=Path)
    ap.add_argument("--template", required=True, type=Path)
    ap.add_argument("--labels-dir", required=True, type=Path)
    args = ap.parse_args(argv)

    if not args.manifest.is_file():
        print(f"ERROR: manifest not found: {args.manifest}", file=sys.stderr)
        return 2
    if not args.template.is_file():
        print(f"ERROR: template not found: {args.template}", file=sys.stderr)
        return 2

    data = json.loads(args.manifest.read_text(encoding="utf-8"))
    entries = data.get("entries", [])

    template_text = args.template.read_text(encoding="utf-8")
    parsed = parse_template(template_text)

    args.labels_dir.mkdir(parents=True, exist_ok=True)

    written = 0
    skipped: list[str] = []
    missing_from_template: list[str] = []
    unknown_headers: list[str] = []

    # Build a set of manifest relative paths (normalised) for validation
    manifest_paths: set[str] = {e["file"].replace("\\", "/") for e in entries}

    # Detect any template header that is not in the manifest
    for hdr in parsed.keys():
        if hdr not in manifest_paths:
            unknown_headers.append(hdr)

    for e in entries:
        rel = e["file"].replace("\\", "/")
        if rel not in parsed:
            missing_from_template.append(rel)
            continue
        full_text = parsed[rel]
        label_name = Path(e["label_file"]).name
        label_path = args.labels_dir / label_name

        # Preserve existing lines[] if present; set full_text
        existing: dict = {}
        if label_path.is_file():
            try:
                existing = json.loads(label_path.read_text(encoding="utf-8"))
            except Exception:
                existing = {}

        label = {
            "schema_version": "1.0",
            "image_sha256": e["sha256"],
            "category": e["category"],
            "full_text": full_text,
            "lines": existing.get("lines", []),
        }
        label_path.write_text(json.dumps(label, indent=2) + "\n", encoding="utf-8")

        if full_text == "":
            skipped.append(rel)
        else:
            written += 1

    print(f"Applied labels: {written} with text, {len(skipped)} skipped (blank)")
    if skipped:
        print(f"\nSkipped ({len(skipped)}):")
        for s in skipped:
            print(f"  {s}")
    if missing_from_template:
        print(f"\nWARNING: {len(missing_from_template)} manifest entries had no [header] in the template:")
        for m in missing_from_template:
            print(f"  {m}")
    if unknown_headers:
        print(f"\nWARNING: {len(unknown_headers)} template headers did not match any manifest entry (typo?):")
        for u in unknown_headers:
            print(f"  {u}")
    if missing_from_template or unknown_headers:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())