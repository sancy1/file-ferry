# filepath: bench/harness/metrics/timing.py
# purpose:  Measure wall time and peak RSS for one extract call.
import time
from typing import Callable, TypeVar
import psutil

T = TypeVar("T")


def measure(fn: Callable[[], T]) -> tuple[T, float, float]:
    """Run fn, return (result, wall_seconds, peak_rss_delta_mb).

    peak_rss_delta_mb is the process peak working set increase over the run.
    On Windows, psutil.Process().memory_info().peak_wset is the process
    high-water mark; the delta is measured against the value seen at start.
    """
    proc = psutil.Process()
    before_peak = proc.memory_info().peak_wset / (1024 * 1024)
    t0 = time.perf_counter()
    result = fn()
    wall_s = time.perf_counter() - t0
    after_peak = proc.memory_info().peak_wset / (1024 * 1024)
    return result, wall_s, max(0.0, after_peak - before_peak)