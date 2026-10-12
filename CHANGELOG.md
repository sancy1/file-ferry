# Changelog

All notable changes to File-Ferry are documented in this file.

## [1.1.0] - 2026-10-12

### Added
- Cloud workspace with built-in profiles for AWS S3, S3-compatible services,
  Azure Blob Storage, Google Cloud Storage, Google Drive, OneDrive, and Dropbox.
- Google Drive Picker downloads for one or multiple selected files, with
  Google Workspace files exported to PDF, XLSX, and PPTX.
- PostgreSQL SchemaLence workspace for schema and table inspection, paged
  role-visible data, Excel export, and custom-format database backups.
- Link Joiner for combining base locations with lists of paths.
- Extract workspace for finding links, paths, email addresses, and phone-like
  text in user-provided content.
- Confirmation before replacing existing Harvest output files.
- Secure credential-store integration and provider-specific cloud setup.

### Changed
- Expanded the application navigation, responsive workspaces, diagnostics,
  resilience policies, and release documentation.
- Database backups run without the short timeout intended for ordinary local
  file writes and remain cancellable.
- Reformatted SchemaLence Structure / Definition reports with a readable,
  aligned columns table and clearly separated, icon-led metadata sections.

### Fixed - Overwrite Confirmation
- New `IOverwriteConfirmer` interface and `AlwaysCancelOverwriteConfirmer`
  default (fail-closed). `OutputSink` now asks before replacing an existing
  file, in both merged and separate output modes.
- New `OverwritePromptResult` record and `OverwriteDecision` enum.
- "Apply to all remaining files" for separate-files mode, scoped to the
  current harvest operation only.
- Existence check runs through the `file-read` policy with a fail-closed
  fallback (treats an unavailable check as "file exists").
- Caller cancellation while the prompt is open returns Cancel and does not
  write the file.

- The `----- {path} -----` default delimiter was corrected to
  `===== {path} =====` to match the documented and tested default.
- `ComposerOptions.SingleMerged()` now sets `IncludeHeaders: true`, matching
  `ComposedDocument.ForSingleMerged()`.

### Maintenance
- Version reconciled to `1.1.0` across shared build settings, the app project,
  and the in-app version display.
- Deleted the leftover `dotnet new xunit` template file `UnitTest1.cs`.
