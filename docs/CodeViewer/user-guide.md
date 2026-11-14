# Code Viewer — User Guide

Code Viewer is a File-Ferry module for inspecting the **structure and source
code of project folders already on your computer**. It opens a local folder,
shows its folder/file tree, and renders the selected files as read-only,
syntax-highlighted code cards. It does not connect to remote servers and never
modifies your project.

Open it from the File-Ferry navigation bar: **Code Viewer** (between **Prompt**
and **SchemaLence**).

---

## Opening a project

1. Click the **+** button on the tab strip.
2. Choose a local folder in the picker.

The folder opens as a **tab**. Only the project root is shown at first —
collapsed and unselected. Code Viewer does **not** scan the whole project when
you open it.

### Multiple projects (tabs)

Each project opens in its own tab along the top.

- **Click a project's name** to switch to it.
- **Click the ✕** on a tab to close that project.

Every tab keeps its own selections, expansion, display mode, scroll position,
and pane sizes. Switching tabs never carries one project's state into another.

---

## The three panes

| Pane | Position | What it shows |
| ---- | -------- | ------------- |
| **Project Browser** | Left | The folder/file tree with expand arrows and checkboxes. |
| **Project Tree Structure** | Upper right | The selected folders/files as an ASCII tree (`├── └── │`). |
| **Code Content** | Lower right | One read-only code card per selected file. |

All three panes scroll independently. Drag the **vertical splitter** between
the browser and the right workspace to resize them. In **Structure + Code**
mode, drag the **horizontal splitter** between the two right-hand panes; one
contracts as the other expands. Pane sizes are remembered per project.

---

## Browsing and selecting

- Click a folder's **arrow** (▸/▾) to expand or collapse it. Folder contents
  load the first time you expand them.
- **Check** a file to add it to the Structure and Code viewers.
- **Check a folder** to check everything inside it, recursively. Unchecking a
  folder removes all of its contents. A folder with only some contents checked
  shows a partial (indeterminate) checkbox.
- Clicking a **file name** marks it as the selected card.

Only checked items appear in the Structure and Code viewers.

---

## Display modes

The toolbar's display-mode selector offers three presentations. The Project
Browser stays visible in all of them.

- **Structure Only** — the Project Tree Structure fills the right workspace.
- **Code Only** — the Code Content viewer fills the right workspace.
- **Structure + Code** — both viewers appear, structure above code.

---

## Auto Display

The **Auto Display** toggle controls automatic selection.

- **ON** — selects every folder and file, expands the tree, and fills both
  viewers. The scan runs in the background and can be cancelled by toggling
  it off, so the app stays responsive even on large projects.
- **OFF** — clears all selections and both viewers, but **keeps your folder
  expansion** so you can continue browsing manually.

Auto Display is **off by default** when you open a project. Reopening a
project restores its previous state without re-scanning; newly added files are
shown but not auto-selected until you toggle Auto Display off and on again.

---

## Code cards

Each selected file appears as a card showing:

- **File name**, **full path**, and **role** (for example "Python Source").
- The **line count** ("26 / 26 lines").
- The **source code** with line numbers and IDE-quality syntax highlighting.
- A **Copy** button for that file. The pane header also has **Copy All**.

The code surface is **read-only** — there is no editing, terminal, or
debugging. Long lines scroll horizontally; the pane scrolls vertically.

**Large and binary files.** There is no file-size limit: any supported text
file is shown in full. Binary or unreadable files stay visible in the browser
and structure, but their card shows a "No content preview available" notice
instead of code.

---

## Project menu (⋮)

The toolbar's **⋮** menu offers:

- **Refresh Project** — re-reads the folders you have expanded, preserving
  your expansion and selection where entries still exist.
- **Show / Hide system files** — reveals or hides operating-system clutter
  (`.DS_Store`, `Thumbs.db`, `desktop.ini`, `$RECYCLE.BIN`,
  `System Volume Information`). Project folders such as `__pycache__`,
  `.git`, and `node_modules` are always shown.
- **Close Project** — closes the current tab.

---

## What Code Viewer remembers

For each project, Code Viewer remembers (under
`%AppData%\FileHarvester\CodeViewer\`):

- Expanded folders and checked items.
- The selected file and display mode.
- The Auto Display and "Show system files" toggles.
- Scroll positions and pane sizes.
- How many times you have opened it ("Opened: N times").

Your **source code is never saved** — only paths and interface state. If the
saved state is missing, corrupt, or belongs to a different folder, Code Viewer
starts fresh and logs the issue; it never blocks File-Ferry from starting. If
you move or rename a project folder, it opens as a new project with its saved
state left intact for the old location.
