# filepath: bench/harness/metrics/wer.py
# purpose:  Word error rate via Levenshtein distance at word level.
from bench.harness.metrics.cer import _levenshtein, _normalize


def wer(reference: str, hypothesis: str, normalization: str = "whitespace-lower") -> float:
    """Word error rate after whitespace and case normalization."""
    ref_tokens = _normalize(reference, normalization).split()
    hyp_tokens = _normalize(hypothesis, normalization).split()
    if not ref_tokens:
        return float(len(hyp_tokens))
    # Levenshtein over token sequences: reuse the character-level implementation
    # by joining tokens with a separator unlikely to appear in real text.
    sep = "  \t  "
    return _levenshtein(sep.join(ref_tokens), sep.join(hyp_tokens)) / len(sep.join(ref_tokens)) * len(sep.join(ref_tokens)) / max(len(ref_tokens), 1)