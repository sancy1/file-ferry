# filepath: bench/tools/labelimg_to_labels.py
# purpose:  Convert LabelImg PASCAL VOC XML output to harness label JSON.
#
# Usage:
#   bench\.venv\Scripts\python -m bench.tools.labelimg_to_labels ^
#       --eval-dir C:\Users\HP\Desktop\exracts ^
#       --manifest bench\eval-set\manifest.json ^
#       --out-dir bench\eval-set\labels
#
# For each image referenced in the manifest, looks for a sibling .xml file
# produced by LabelImg. Converts each <object> with a <bndbox> and a <name>
# into a lines[] entry, sorts by reading order, composes full_text, and
# writes the corresponding label JSON file. Aborts on any missing XML.
import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def parse_voc(xml_path: Path) -> tuple[str, list[dict]]:
    """Return (image_filename, lines) from a LabelImg PASCAL VOC XML file."""
    tree = ET.parse(xml_path)
    root = tree.getroot()
    filename = root.findtext("filename", "").strip()
    if not filename:
        raise ValueError(f"{xml_path}: missing <filename> element")
    lines: list[dict] = []
    for obj in root.findall("object"):
        name = (obj.findtext("name", "") or "").strip()
        bb = obj.find("bndbox")
        if bb is None:
            continue
        try:
            xmin = int(float(bb.findtext("xmin")))
            ymin = int(float(bb.findtext("ymin")))
            xmax = int(float(bb.findtext("xmax")))
            ymax = int(float(bb.findtext("ymax")))
        except (TypeError, ValueError):
            continue
        w = xmax - xmin
        h = ymax - ymin
        if w <= 0 or h <= 0:
            continue
        lines.append({"bbox": [xmin, ymin, w, h], "text": name})
    return filename, lines


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="bench.tools.labelimg_to_labels")
    ap.add_argument("--eval-dir", required=True, type=Path)
    ap.add_argument("--manifest", default=Path("bench/eval-set/manifest.json"), type=Path)
    ap.add_argument("--out-dir", default=Path("bench/eval-set/labels"), type=Path)
    args = ap.parse_args(argv)

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    entries = manifest.get("entries", [])
    if not entries:
        print(f"ERROR: manifest has no entries: {args.manifest}", file=sys.stderr)
        return 2

    args.out_dir.mkdir(parents=True, exist_ok=True)

    missing_xml: list[str] = []
    written = 0
    errors: list[str] = []

    for e in entries:
        rel = e["file"].replace("\\", "/")
        img_path = args.eval_dir / rel
        if not img_path.is_file():
            errors.append(f"image missing: {rel}")
            continue
        xml_path = img_path.with_suffix(".xml")
        if not xml_path.is_file():
            missing_xml.append(rel)
            continue
        try:
            filename_in_xml, lines = parse_voc(xml_path)
        except Exception as ex:
            errors.append(f"{xml_path}: {ex}")
            continue
        # sort reading order: top-to-bottom, then left-to-right
        lines.sort(key=lambda l: (l["bbox"][1], l["bbox"][0]))
        full_text = "\n".join(l["text"] for l in lines).strip()
        label = {
            "schema_version": "1.0",
            "image_sha256": e["sha256"],
            "category": e["category"],
            "full_text": full_text,
            "lines": lines,
        }
        label_path = args.out_dir / Path(e["label_file"]).name
        label_path.write_text(json.dumps(label, indent=2) + "\n", encoding="utf-8")
        written += 1

    print(f"Written: {written} label files")
    if missing_xml:
        print(f"\nMissing XML for {len(missing_xml)} images:")
        for m in missing_xml:
            print(f"  {m}")
    if errors:
        print(f"\nErrors ({len(errors)}):")
        for er in errors:
            print(f"  {er}")
    if missing_xml or errors:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())