# File-Ferry - User Guide

Status: Living document.

## Quick Start

1. Paste file paths or bare filenames into the intake panel.
2. Choose Separate Files or Single Merged File.
3. Pick a format.
4. Pick a destination or accept the Downloads default.
5. Click Harvest Now.

## Overwrite behavior

When an output file already exists at the destination, File-Ferry asks
before replacing it. A modal dialog shows the target path and offers
Replace or Cancel.

- Cancel leaves the existing file untouched and marks the harvest as
  failed with the reason "Cancelled by user: file exists".
- Replace overwrites the file with the harvest output.
- In Separate Files mode, the dialog offers an "Apply to all remaining
  files" option. When checked, the same choice is applied to every
  subsequent collision in the current harvest. The choice is reset for
  the next harvest; it is never remembered across operations.
- Cancelling the dialog, closing it with the window X, or pressing
  Escape all cancel the overwrite. The existing file is not touched.

This modal behavior applies to local Harvest output. Google Drive
downloads use the Save dialog's overwrite confirmation described below.

## Downloading from Google Drive

1. Connect Google Drive on the Cloud page and choose the connected account.
2. Select **Choose Google Drive file** next to that account.
3. Complete Google's system-browser file selection and consent flow.
   File-Ferry verifies that the selected Google account matches the
   connected Cloud account before accessing selected files.
4. Review the selected-file list. Choose **Save separately** to choose a
   destination for each file, or **Save as ZIP** to combine multiple
   downloadable files into one archive. Downloads is suggested; files are
   not saved until you confirm in the Save dialog.
5. Google Docs are exported as PDF, Sheets as XLSX, and Slides as PPTX.
   Other supported files are downloaded in their existing format.
   Unsupported or non-downloadable selections are identified and skipped;
   remaining files continue.

The Save dialog asks before replacing an existing file. A partial download
is written to a temporary sibling file and removed if the transfer fails
or is cancelled; if the operating system prevents cleanup, File-Ferry
identifies the temporary file so you can remove it manually. Picker access
is limited to `drive.file`; Picker
authorization tokens are kept in memory for the active selection only and
are not stored as cloud connections.

Google's `prompt=consent` Picker flow can show Google's consent interface
for each selection. If Google cannot verify the selected account identity
or the Drive API says the file cannot be downloaded, File-Ferry fails
closed and explains the issue.

This downloads only files returned by the user's current Picker selection.
It does not browse or download all of Drive.

## Exploring databases with SchemaLence

Open **SchemaLence** from the title bar to browse PostgreSQL databases
without changing data. Connect with a PostgreSQL URI or enter the host,
port, database, username, password, and SSL mode. Saved profiles contain
connection details but not passwords. If **Remember password** is enabled,
the password is stored through the operating system credential-store
integration; on platforms where secure storage is unavailable, the app
reports that it cannot remember the password rather than saving it in a
plain file.

After connecting, select a schema and an object to inspect its structure
and permission-visible metadata. The data view displays bounded pages
(100 rows by default, maximum 500) and may be sorted by a selected column.
Displayed row counts are estimates, not exact counts. The connected
PostgreSQL role's permissions continue to govern visible schemas,
objects, and rows. SchemaLence does not provide arbitrary SQL execution or
database editing.

The structure report includes a JSON example row when the current data
page contains readable rows, check constraints and enum allowed values
when present, and an explicit note when none are reported. Exact table
counts can be expensive on large or busy databases, so they are not run
automatically: select a table and use **Count rows** to request one. The
exact count visible to the connected role, including row-level security
filtering, is then added to the report.

The **Data grid** shows spreadsheet-style, read-only pages. Use **Export
Excel** to save every row from the selected table or view to an `.xlsx`
workbook; large exports are streamed in batches, and very large results
continue on additional worksheets. Values are written as text, and export
can be cancelled while it is running.

Use **Backup database** to save a PostgreSQL custom-format `.dump` archive
to a chosen local path. The archive contains the selected database's
schema and data that the connected role can access; it is not a live
clone and does not include cluster-global roles or settings. The
PostgreSQL client tool `pg_dump` must be installed and available on PATH.
Restore an archive with PostgreSQL's `pg_restore` into a database; File-Ferry
does not restore or validate backups. Backup creation can be cancelled.
Backups are allowed to run for as long as `pg_dump` needs; they are not
subject to the shorter timeout used for ordinary local file writes.

The PostgreSQL provider recognizes pgvector columns and reports vector
type/dimension metadata when PostgreSQL exposes it. It does not create,
modify, or query vector indexes. PostgreSQL support is the current
database-provider milestone; MongoDB is the planned NoSQL provider and is
not included in this release.

## Joining paths
Open **Link Joiner** from the title bar to combine one or more local base
folders with lists of relative paths. Each section has an independent base
and input list, so one base can be applied to many paths and multiple bases
can be processed together.

- Enter an absolute local folder path or use **Browse folder** to choose one.
- Paste one path per line, or use **Add files** to append one or more
  selected files. Selected files are already absolute and pass through
  unchanged.
- **Join All** updates the results. Results remain visible beside the inputs
  when the window is wide; the panes stack on narrower windows.
- Absolute paths and URI values (including cloud URIs) pass through without
  being joined to a local base. Relative paths that traverse above the base,
  ambiguous drive-relative paths, and blank lines are reported rather than
  silently joined or discarded.
- Duplicate results are retained by default. Enable **Remove duplicate
  results** to omit repeats. Separator normalization can be selected per
  section.
- Use **Copy selected** or **Copy all**, or save the results as a `.txt`
  file. **Send to Harvest** fills the Harvest input and opens that page;
  it does not parse or start a harvest.

Joining is performed in memory. File access occurs only when browsing,
adding files, or saving results. Link Joiner does not change the local
Harvest processing or output settings.

## Extracting matches from text

Open **Extract** from the title bar to find text that looks like web/file
links, local paths, email addresses, and phone numbers. Enter or paste text
in the input pane, then choose **Extract All** or a category-specific
extraction action. The results pane lets you filter the matches by category.

- Extraction runs locally on the text in the input box. It does not open,
  resolve, or download extracted values and does not send text to a network
  service.
- Links and paths preserve their spelling and case. Clearly recognized
  links and paths are deduplicated case-sensitively; email-like matches are
  deduplicated without regard to case.
- Phone-like matches show the original text and duplicates are retained.
  Matches are not checked for existence, assignment, or validity, and no
  country code is inferred.
- Use **Copy selected**, **Copy active category**, or **Copy all results**.
  **Save results** writes a user-chosen `.txt` file and can include category
  headings and match counts.
- Editing the input marks displayed results as out of date; copy and save
  are disabled until extraction runs again. **Clear input** and **Clear
  results** act independently.
