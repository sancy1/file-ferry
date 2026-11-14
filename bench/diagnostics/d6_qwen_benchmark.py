"""
D6 CORRECTED: Qwen3-VL-2B handwriting benchmark.
Uses base64 data URI for image input (handler expects string, not PIL object).
"""
import sys, json, time, base64, io
from pathlib import Path
from llama_cpp import Llama
from llama_cpp.llama_chat_format import Qwen25VLChatHandler
from PIL import Image

REPO = Path(r"C:\Users\HP\Desktop\file-ferry")
MODELS = REPO / "bench" / "models-qwen"
IMAGES_ROOT = Path(r"C:\Users\HP\Desktop\exracts")
LABELS_ROOT = REPO / "bench" / "eval-set" / "labels"

TEXT_MODEL = MODELS / "Qwen3VL-2B-Instruct-Q4_K_M.gguf"
MMPROJ = MODELS / "mmproj-Qwen3VL-2B-Instruct-F16.gguf"

targets = [
    ("Handwritting/sharp/handwriting_line_2.png", "labels/Handwritting_sharp_handwriting_line_2_png.json"),
    ("Handwritting/sharp/handwriting_line_5.png", "labels/Handwritting_sharp_handwriting_line_5_png.json"),
    ("Handwritting/sharp/images.jpeg", "labels/Handwritting_sharp_images_jpeg.json"),
    ("Handwritting/sharp/last-post-was-blurry-sorry-v0-edsdh7x0cjtd1.webp", "labels/Handwritting_sharp_last-post-was-blurry-sorry-v0-edsdh7x0cjtd1_webp.json"),
]

def lev(a, b):
    n, m = len(a), len(b)
    if n == 0: return m
    if m == 0: return n
    prev = list(range(m + 1))
    for i in range(1, n + 1):
        cur = [i] + [0] * m
        for j in range(1, m + 1):
            cost = 0 if a[i-1] == b[j-1] else 1
            cur[j] = min(cur[j-1] + 1, prev[j] + 1, prev[j-1] + cost)
        prev = cur
    return prev[m]

def cer(reference, hypothesis):
    if len(reference) == 0: return 1.0 if len(hypothesis) > 0 else 0.0
    return lev(reference, hypothesis) / len(reference)

def read_label(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        return data.get("full_text", "")
    except Exception as e:
        print(f"  WARN: could not read label: {e}")
        return ""

def image_to_data_uri(pil_img):
    """Convert PIL Image to base64 data URI string for llama-cpp-python."""
    buf = io.BytesIO()
    pil_img.save(buf, format="PNG")
    data = base64.b64encode(buf.getvalue()).decode("ascii")
    return f"data:image/png;base64,{data}"

PROMPT = (
    "Read the handwriting in this image. Return only the text you see, "
    "preserving line breaks. If a character is unclear, output [?]. "
    "Do not invent text."
)

def main():
    if not TEXT_MODEL.exists():
        print(f"ABORT: text model not found: {TEXT_MODEL}")
        sys.exit(1)
    if not MMPROJ.exists():
        print(f"ABORT: mmproj not found: {MMPROJ}")
        sys.exit(1)

    print(f"Text model: {TEXT_MODEL.name} ({TEXT_MODEL.stat().st_size} bytes)")
    print(f"Vision:     {MMPROJ.name} ({MMPROJ.stat().st_size} bytes)")
    print()
    print("Loading model (this takes ~30-60 seconds on CPU)...")

    try:
        handler = Qwen25VLChatHandler(clip_model_path=str(MMPROJ), verbose=False)
    except Exception as e:
        print(f"ABORT: could not create chat handler: {type(e).__name__}: {e}")
        sys.exit(1)

    try:
        llm = Llama(
            model_path=str(TEXT_MODEL),
            chat_handler=handler,
            n_ctx=4096,
            n_threads=4,
            n_gpu_layers=0,
            verbose=False,
        )
    except Exception as e:
        print(f"ABORT: could not load model: {type(e).__name__}: {e}")
        sys.exit(1)

    print("Model loaded.")
    print()
    print(f"{'image':<50} {'pp_ocr':>8} {'qwen':>8} {'delta':>8} {'verdict':>10}")
    print("-" * 90)

    pp_baseline = {
        "handwriting_line_2.png": 0.1176,
        "handwriting_line_5.png": 0.2105,
        "images.jpeg": 0.2881,
        "last-post-was-blurry-sorry-v0-edsdh7x0cjtd1.webp": 0.8545,
    }

    qwen_pass = 0
    results = []

    for rel_img, rel_label in targets:
        img_path = IMAGES_ROOT / rel_img
        label_path = REPO / "bench" / "eval-set" / rel_label
        display = Path(rel_img).name

        if not img_path.exists():
            print(f"{display:<50} MISSING")
            continue
        if not label_path.exists():
            print(f"{display:<50} LABEL-MISSING")
            continue

        reference = read_label(label_path)
        if not reference:
            print(f"{display:<50} EMPTY-LABEL")
            continue

        try:
            img = Image.open(img_path).convert("RGB")
            max_side = 1024
            w, h = img.size
            if max(w, h) > max_side:
                scale = max_side / max(w, h)
                img = img.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
            data_uri = image_to_data_uri(img)
        except Exception as e:
            print(f"{display:<50} DECODE-FAIL: {e}")
            continue

        start = time.time()
        try:
            resp = llm.create_chat_completion(
                messages=[
                    {"role": "user", "content": [
                        {"type": "image_url", "image_url": {"url": data_uri}},
                        {"type": "text", "text": PROMPT},
                    ]}
                ],
                max_tokens=256,
                temperature=0.0,
            )
            hypothesis = resp["choices"][0]["message"]["content"]
        except Exception as e:
            err = f"{type(e).__name__}: {str(e)[:60]}"
            print(f"{display:<50} INFER-ERR: {err}")
            continue

        elapsed = time.time() - start
        score = cer(reference, hypothesis)
        pp = pp_baseline.get(display, float("nan"))
        delta = score - pp if pp == pp else float("nan")
        verdict = "PASS" if score <= 0.15 else "FAIL"
        if score <= 0.15:
            qwen_pass += 1

        print(f"{display:<50} {pp:>8.4f} {score:>8.4f} {delta:>8.4f} {verdict:>10}")
        results.append({
            "image": display,
            "pp_ocr_cer": pp,
            "qwen_cer": score,
            "elapsed_s": round(elapsed, 2),
            "qwen_text": hypothesis[:500],
            "reference_text": reference[:500],
            "verdict": verdict,
        })

    print()
    print("=== GATE ===")
    print(f"Qwen CER <= 0.15 on {qwen_pass} of 4 images")
    if qwen_pass >= 3:
        print("PASS: Phase A gate met. Proceed to Phase B.")
    else:
        print(f"FAIL: gate requires 3 of 4. Got {qwen_pass} of 4.")

    out_path = REPO / "bench" / "reports" / f"qwen-benchmark-{time.strftime('%Y%m%d-%H%M%S')}.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"gate_pass_count": qwen_pass, "results": results}, f, indent=2)
    print()
    print(f"Report saved: {out_path}")

if __name__ == "__main__":
    main()