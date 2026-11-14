import sys, base64, io
from pathlib import Path
from llama_cpp import Llama
from llama_cpp.llama_chat_format import Llava15ChatHandler
from PIL import Image

REPO = Path(r"C:\Users\HP\Desktop\file-ferry")
MODELS = REPO / "bench" / "models-qwen"
TEXT_MODEL = MODELS / "Qwen3VL-2B-Instruct-Q4_K_M.gguf"
MMPROJ = MODELS / "mmproj-Qwen3VL-2B-Instruct-F16.gguf"

def image_to_data_uri(pil_img):
    buf = io.BytesIO()
    pil_img.save(buf, format="PNG")
    return f"data:image/png;base64,{base64.b64encode(buf.getvalue()).decode('ascii')}"

print("Loading with Llava15ChatHandler...")
try:
    handler = Llava15ChatHandler(clip_model_path=str(MMPROJ), verbose=False)
    llm = Llama(model_path=str(TEXT_MODEL), chat_handler=handler, n_ctx=4096, n_threads=4, n_gpu_layers=0, verbose=False)
    print("Loaded.")
except Exception as e:
    print(f"LOAD FAILED: {type(e).__name__}: {e}")
    sys.exit(1)

img_path = Path(r"C:\Users\HP\Desktop\exracts\Handwritting\sharp\handwriting_line_2.png")
img = Image.open(img_path).convert("RGB")
uri = image_to_data_uri(img)

print("Running inference on handwriting_line_2.png...")
try:
    resp = llm.create_chat_completion(
        messages=[{"role":"user","content":[
            {"type":"image_url","image_url":{"url":uri}},
            {"type":"text","text":"Read the handwriting in this image. Return only the text."}
        ]}],
        max_tokens=256, temperature=0.0)
    print("OUTPUT:", resp["choices"][0]["message"]["content"])
except Exception as e:
    print(f"INFER FAILED: {type(e).__name__}: {e}")
    sys.exit(1)