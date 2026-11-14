# filepath: bench/tools/README.md
# purpose:  LabelImg annotation workflow for the Phase 2.4a eval set.

WORKFLOW OVERVIEW
  The harness needs per-line bounding boxes and their text to compute the
  line-detection-rate metric. You annotate each image in LabelImg, which
  produces one PASCAL VOC XML per image next to the image itself. This tool
  converts those XMLs into the harness label JSON schema.

STEP 1 - INSTALL LABELIMG
  From the repository root:
    bench\.venv\Scripts\pip.exe install labelImg

  Installs labelImg 1.8.6 + PyQt5. ~40 MB. Takes 1 minute. Verified
  compatible with Python 3.11.9 on this machine.

STEP 2 - LAUNCH LABELIMG
  bench\.venv\Scripts\labelImg.exe

  If labelImg.exe is not on the Scripts path, use:
    bench\.venv\Scripts\python.exe -m labelImg

STEP 3 - OPEN THE FOLDER
  In LabelImg: File -> Open Dir -> select C:\Users\HP\Desktop\exracts
  In LabelImg: File -> Change Save Dir -> select C:\Users\HP\Desktop\exracts

  (Saving the XML next to the image keeps everything together. The
   converter expects that layout.)

STEP 4 - CONFIGURE
  - Click "PascalVOC" at bottom-left of the window (must be XML, not JSON).
  - Click the box icon "Create RectBox" (W key) to draw boxes.
  - For each line of text in the image:
    1. Drag a box around the line.
    2. Type the ground-truth text of that line in the label dialog.
    3. Press Enter.
  - When done with an image, press Ctrl+S (save). Advance to next image
    with D key.

STEP 5 - NOTES ON TEXT LABELS
  - One box per line of text.
  - Type the text exactly as a human reads it. Do not correct typos in the
    source image; the OCR engine should be measured against what is
    actually visible.
  - Unicode is fine (accented characters, punctuation).
  - If a line is unreadable, skip it. Do not draw a box.
  - Classes: LabelImg uses the label field as the class name. Every box
    is a distinct class because the class IS the text. The classes.txt
    file will grow as you type unique line texts. That is expected.

STEP 6 - CONVERT XML TO LABELS
  When all 26 images are annotated and saved:
    bench\.venv\Scripts\python.exe -m bench.tools.labelimg_to_labels --eval-dir C:\Users\HP\Desktop\exracts

  The converter:
  - Reads bench/eval-set/manifest.json for the list of images.
  - For each image, finds the sibling .xml produced by LabelImg.
  - Parses <object> entries with <bndbox> + <name> (the text).
  - Sorts lines top-to-bottom, then left-to-right (reading order).
  - Writes bench/eval-set/labels/<name>.json with full_text + lines[].

  Expected output:
    Written: 26 label files

  If any XML is missing, the tool prints which images need annotation.

STEP 7 - RUN THE HARNESS
  bench\.venv\Scripts\python.exe -m bench.harness.main --eval-dir C:\Users\HP\Desktop\exracts --candidate all

  This runs all three candidates (pp-ocr-only, pp-ocr+tr-ocr, tr-ocr-only)
  against all 26 images. ~30-45 minutes. Reports land in bench/reports/.

WHAT IF LABELIMG CRASHES OR A BOX IS WRONG
  - LabelImg saves per-image; a crash on image 15 does not lose images
    1-14. Reopen and continue.
  - To fix a box: click it, drag the corners, edit the label, Ctrl+S.
  - To delete a box: click it, press Delete, Ctrl+S.

FALLBACK IF LABELIMG DOES NOT WORK
  - The converter is not LabelImg-specific. Any tool that produces
    PASCAL VOC XML with <filename>, <object><name>, <bndbox>xmin,ymin,
    xmax,ymax works. CVAT, VoTT, and RectLabel all produce this format.
  - If needed, the operator can write the label JSON directly (see
    bench/eval-set/labels.schema.json for the schema).

CROSS-REFERENCES
  - bench/eval-set/manifest.json   list of images to annotate
  - bench/eval-set/labels.schema.json   target JSON schema
  - bench/harness/main.py   harness entry point
  - docs/phase-2-4-plan.md   overall Phase 2.4a plan