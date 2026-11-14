# filepath: bench/harness/main.py
# purpose:  CLI entry point for the Phase 2.4a evaluation harness.
#
# Usage (from repository root):
#   bench\.venv\Scripts\python -m bench.harness.main \
#       --eval-dir <path> --candidate <name> [--out-dir <path>] [--limit N]
#
# See bench/README.md for full instructions.
import argparse
import json
import sys
import traceback
from pathlib import Path
from datetime import datetime, timezone

from bench.harness import __version__
from bench.harness.manifest import load_manifest
from bench.harness.labels import load_labels
from bench.harness import report
from bench.harness.metrics.cer import cer
from bench.harness.metrics.wer import wer
from bench.harness.metrics.line_match import match_lines
from bench.harness.metrics.timing import measure


CANDIDATE_CHOICES = ["pp-ocr-only", "pp-ocr+tr-ocr", "tr-ocr-only", "all"]


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="bench.harness.main")
    p.add_argument("--eval-dir", required=True, help="Folder containing the eval images.")
    p.add_argument("--manifest", default="bench/eval-set/manifest.json")
    p.add_argument("--candidate", choices=CANDIDATE_CHOICES, required=True)
    p.add_argument("--out-dir", default="bench/reports")
    p.add_argument("--limit", type=int, default=0,
                   help="Process at most N images per candidate (0 = all).")
    return p


def resolve_candidates(name: str) -> list[str]:
    if name == "all":
        return ["pp-ocr-only", "pp-ocr+tr-ocr", "tr-ocr-only"]
    return [name]


def load_candidate(name: str):
    # Import lazily so a missing optional dependency in one candidate does not
    # prevent another from loading.
    if name == "pp-ocr-only":
        from bench.harness.candidates.pp_ocr_only import PPOcrOnlyCandidate
        return PPOcrOnlyCandidate()
    if name == "pp-ocr+tr-ocr":
        from bench.harness.candidates.pp_ocr_tr_ocr import PPOcrTrOcrCandidate
        return PPOcrTrOcrCandidate()
    if name == "tr-ocr-only":
        from bench.harness.candidates.tr_ocr_only import TrOcrOnlyCandidate
        return TrOcrOnlyCandidate()
    raise ValueError(f"Unknown candidate: {name}")


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    eval_dir = Path(args.eval_dir).resolve()
    manifest_path = Path(args.manifest).resolve()
    out_dir = Path(args.out_dir).resolve()

    if not eval_dir.is_dir():
        print(f"ERROR: --eval-dir not a directory: {eval_dir}", file=sys.stderr)
        return 2
    if not manifest_path.is_file():
        print(f"ERROR: --manifest not a file: {manifest_path}", file=sys.stderr)
        return 2

    out_dir.mkdir(parents=True, exist_ok=True)

    manifest = load_manifest(manifest_path)
    entries = manifest["entries"]
    if args.limit > 0:
        entries = entries[:args.limit]
    print(f"Loaded manifest with {len(entries)} entries (from {manifest_path})")

    summary: dict[str, dict] = {}
    for cand_name in resolve_candidates(args.candidate):
        print(f"\n=== Candidate: {cand_name} ===")
        try:
            cand = load_candidate(cand_name)
        except Exception as ex:
            print(f"  SKIPPED: could not import candidate: {ex}", file=sys.stderr)
            summary[cand_name] = {"status": "skipped", "reason": f"import: {ex}"}
            continue

        try:
            cand.load()
        except Exception as ex:
            print(f"  SKIPPED: candidate load() failed: {ex}", file=sys.stderr)
            summary[cand_name] = {"status": "skipped", "reason": f"load: {ex}"}
            continue

        per_image: list[dict] = []
        for i, entry in enumerate(entries, start=1):
            img_path = eval_dir / entry["file"]
            if not img_path.is_file():
                print(f"  [{i}/{len(entries)}] MISSING: {img_path}", file=sys.stderr)
                continue
            label = load_labels(Path("bench/eval-set") / entry["label_file"])
            ref_text_check = (label.get("full_text") or "").strip()
            if not ref_text_check:
                print(f"  [{i}/{len(entries)}] {entry['file']}  SKIP: empty reference")
                per_image.append({
                    "file": entry["file"],
                    "status": "skipped",
                    "reason": "empty reference (annotator left blank)",
                })
                continue
            try:
                (lines, wall_s, peak_mb) = measure(lambda: cand.extract(img_path))
            except Exception as ex:
                print(f"  [{i}/{len(entries)}] EXTRACT FAILED: {ex}", file=sys.stderr)
                per_image.append({
                    "file": entry["file"],
                    "status": "error",
                    "error": str(ex),
                })
                continue

            ref_text = ref_text_check
            hyp_text = " ".join(l.text for l in lines)
            cer_val = cer(ref_text, hyp_text)
            wer_val = wer(ref_text, hyp_text)
            ref_boxes = [tuple(l["bbox"]) for l in label.get("lines", []) if "bbox" in l]
            det_boxes = [l.bbox for l in lines]
            matched, ref_n, det_n = match_lines(ref_boxes, det_boxes) if ref_boxes else (0, 0, len(det_boxes))
            rate = matched / ref_n if ref_n else None
            avg_conf = (
                sum(l.confidence for l in lines if l.confidence is not None)
                / max(1, sum(1 for l in lines if l.confidence is not None))
                if any(l.confidence is not None for l in lines)
                else None
            )

            per_image.append({
                "file": entry["file"],
                "status": "ok",
                "category": entry["category"],
                "cer": cer_val,
                "wer": wer_val,
                "line_detection_rate": rate,
                "avg_confidence": avg_conf,
                "wall_time_s": wall_s,
                "peak_rss_mb": peak_mb,
            })
            print(f"  [{i}/{len(entries)}] {entry['file']}  cer={cer_val:.4f}  wer={wer_val:.4f}  t={wall_s:.2f}s")

        cand.teardown()
        summary[cand_name] = {
            "status": "ok",
            "version_info": getattr(cand, "version_info", {}),
            "images": per_image,
        }

    date_tag = datetime.now(timezone.utc).strftime("%Y%m%d")
    report.write_summary(out_dir, summary, date_tag, version=__version__)
    print(f"\nReports written to {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())