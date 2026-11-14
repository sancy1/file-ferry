<!--
FILE:     docs/prompt-app/user-guide.md
PURPOSE:  The user-facing guide for the Prompt App. How to create,
          edit, delete, search, import, export, and use the keyboard
          shortcuts. Every claim here is asserted against the code by
          src/FileHarvester.Tests/Documentation/PromptAppClaimsTests.cs.
-->
# Prompt App User Guide

The Prompt App is File-Ferry's prompt library. It stores prompts in a
local SQLite database, organizes them by category and sub-category,
supports full-text search, imports from common document formats, and
exports in any registered format.

## Creating a prompt

Click **+ New Prompt** in the sidebar footer. The editor opens with
empty Title, Body, Category, Sub-category, and Tags fields. **Save is
disabled until both Title and Body are non-empty.**

## Editing a prompt

Select a prompt from the list, then click **Edit** in the viewer
toolbar. The editor opens pre-populated. Save updates the prompt and
creates a version snapshot.

## Deleting a prompt

Select a prompt, then click **Delete** in the viewer toolbar (or press
the **Delete** key). The prompt moves to the Trash. It can be restored
from the Trash dialog (the **Trash** button in the sidebar footer).

## Searching

The search bar at the top of the page filters the prompt list as you
type. The scope dropdown limits the search to the current category or
sub-category. The Tags field matches prompts with any of the listed
tags. The **Favorites only** checkbox limits results to favorites.

## Importing a prompt

Click **+ Import** in the sidebar footer. Pick a `.txt`, `.md`, `.docx`,
or `.pdf` file. Choose a category and sub-category. The importer reads
the file's text and creates a new prompt.

## Exporting a prompt

Select a prompt, then click **Export** in the viewer toolbar (or press
**Ctrl+E**). Choose a format, destination folder, and filename. The
file is written to the destination.

To export multiple prompts at once, select them in the list and use
the bulk export from the bulk-action bar.

## Keyboard shortcuts

The Prompt App supports six keyboard shortcuts. They are listed in the
**?** dialog opened from the viewer toolbar.

| Shortcut | Action |
|---|---|
| **Ctrl+N** | New prompt |
| **Ctrl+F** | Focus the search box |
| **Ctrl+E** | Export the selected prompt |
| **Delete** | Move the selected prompt to the Trash |
| **Ctrl+S** | Save the prompt being edited |
| **Ctrl+Z / Ctrl+Y** | Undo / redo in the editor body |

Click the **?** button at the end of the viewer toolbar to open the
shortcuts dialog at any time. The dialog is the authoritative list of
the shortcuts and their actions.