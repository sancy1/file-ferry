# filepath: bench/tools/generate_full_text_template.py
# purpose:  Generate a text-only annotation template from the eval manifest.
#
# Usage (from any directory):
#   C:\Users\HP\Desktop\file-ferry\bench\.venv\Scripts\python.exe ^
#     C:\Users\HP\Desktop\file-ferry\bench\tools\generate_full_text_template.py ^
#     --manifest C:\Users\HP\Desktop\file-ferry\bench\eval-set\manifest.json ^
#     --template C:\Users\HP\Desktop\file-ferry\bench\eval-set\full_text_input.txt
#
# The output file has one section per image:
#   [<relative-path>]
#   <blank line for text>
#   ---
# The operator types the ground-truth text under each header. Blank = skip.
import argparse
import json
import sys
from pathlib import Path


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="generate_full_text_template")
    ap.add_argument("--manifest", required=True, type=Path)
    ap.add_argument("--template", required=True, type=Path)
    args = ap.parse_args(argv)

    if not args.manifest.is_file():
        print(f"ERROR: manifest not found: {args.manifest}", file=sys.stderr)
        return 2

    data = json.loads(args.manifest.read_text(encoding="utf-8"))
    entries = data.get("entries", [])
    if not entries:
        print(f"ERROR: manifest has no entries: {args.manifest}", file=sys.stderr)
        return 2

    lines: list[str] = []
    lines.append("# ImageTextify Phase 2.4a - ground-truth text template")
    lines.append("#")
    lines.append("# Instructions:")
    lines.append("# - For each image below, type the exact text you see in the image")
    lines.append("#   under its [filename] header.")
    lines.append("# - One line of text per line in the image. Single-line images get")
    lines.append("#   a single line.")
    lines.append("# - Leave the section blank (header immediately followed by ---) if")
    lines.append("#   the image is unreadable. Do NOT type placeholder words like")
    lines.append("#   'unreadable' - those would be treated as the ground truth.")
    lines.append("# - Do not type --- on its own line as text. It is the separator.")
    lines.append("# - Preserve case, punctuation, and spacing exactly.")
    lines.append("#")
    lines.append("")

    for e in entries:
        rel = e["file"].replace("\\", "/")
        lines.append(f"[{rel}]")
        lines.append("")
        lines.append("---")

    args.template.parent.mkdir(parents=True, exist_ok=True)
    args.template.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"Wrote template: {args.template}")
    print(f"Images to annotate: {len(entries)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())