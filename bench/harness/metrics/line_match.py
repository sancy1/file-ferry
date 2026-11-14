# filepath: bench/harness/metrics/line_match.py
# purpose:  IoU-based line matching for the line-detection success rate.


def iou(a: tuple[int, int, int, int], b: tuple[int, int, int, int]) -> float:
    ax, ay, aw, ah = a
    bx, by, bw, bh = b
    x1 = max(ax, bx)
    y1 = max(ay, by)
    x2 = min(ax + aw, bx + bw)
    y2 = min(ay + ah, by + bh)
    iw = max(0, x2 - x1)
    ih = max(0, y2 - y1)
    inter = iw * ih
    if inter == 0:
        return 0.0
    union = aw * ah + bw * bh - inter
    return inter / union if union else 0.0


def match_lines(
    reference_boxes: list[tuple[int, int, int, int]],
    detected_boxes: list[tuple[int, int, int, int]],
    iou_threshold: float = 0.5,
) -> tuple[int, int, int]:
    """Greedy IoU matching. Returns (matched, reference_count, detected_count)."""
    matched = 0
    used = [False] * len(detected_boxes)
    for ref in reference_boxes:
        best_j = -1
        best_iou = iou_threshold
        for j, det in enumerate(detected_boxes):
            if used[j]:
                continue
            v = iou(ref, det)
            if v > best_iou:
                best_iou = v
                best_j = j
        if best_j >= 0:
            used[best_j] = True
            matched += 1
    return matched, len(reference_boxes), len(detected_boxes)