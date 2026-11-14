# filepath: bench/harness/metrics/cer.py
# purpose:  Character error rate via Levenshtein distance.


def _normalize(s: str, mode: str) -> str:
    if mode == "none":
        return s
    s = " ".join(s.split())  # collapse whitespace
    if mode == "whitespace-lower":
        s = s.lower()
    return s


def _levenshtein(a: str, b: str) -> int:
    if len(a) < len(b):
        a, b = b, a
    if not b:
        return len(a)
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, start=1):
        cur = [i]
        for j, cb in enumerate(b, start=1):
            cur.append(min(
                prev[j] + 1,       # deletion
                cur[j - 1] + 1,    # insertion
                prev[j - 1] + (ca != cb),  # substitution
            ))
        prev = cur
    return prev[-1]


def cer(reference: str, hypothesis: str, normalization: str = "whitespace-lower") -> float:
    """Character error rate after whitespace and case normalization."""
    ref = _normalize(reference, normalization)
    hyp = _normalize(hypothesis, normalization)
    if not ref:
        return float(len(hyp))
    return _levenshtein(ref, hyp) / len(ref)