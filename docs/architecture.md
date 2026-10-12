# File-Ferry - Architecture

Status: Living document. Sections are added as phases complete.

## Purpose

This document describes the layered architecture of File-Ferry, the
responsibility of each project, and the data flow from user input to
final output file.

## Layering
FileHarvester.App (Avalonia UI - presentation only)
FileHarvester.Core (domain logic - OS-agnostic, UI-agnostic)
FileHarvester.Platform (OS-specific adapters behind interfaces)
FileHarvester.Tests (xUnit - all layers)

text

## Data Flow
User input -> FileRequest[]
-> IFileResolver -> ResolvedFile[]
-> IContentReader -> FileContent[]
-> IBundleComposer -> ComposedDocument
-> IFormatWriter -> bytes
-> IOutputSink -> files on disk

Google Drive's Cloud-page download is a separate flow: Cloud connection
instance -> Platform Picker OAuth flow -> token-bound Drive metadata and
download/export service -> user-selected local file. It does not route
through the Harvest resolver, reader, composer, or output sink.

text

## Cloud-Readiness Decisions

Harvest currently targets local files only. Google Drive's separate
Cloud-page selection/download action is implemented without adding Drive
sources to Harvest. Other cloud storage providers and cloud-backed Harvest
inputs/outputs remain future work. To keep the Harvest pipeline extensible,
three architectural rules are reserved today.

### Rule 1 - IFormatWriter takes a Stream, never a FileStream

Every IFormatWriter implementation writes to a System.IO.Stream.
It must never accept or assume a FileStream, and must never accept
a filesystem path. This keeps format writers usable for local files,
in-memory buffers, network streams, and cloud upload streams without
change.

Enforcement: Code review. Any future PR that changes the
IFormatWriter signature to take a FileStream or string path
must be rejected.

### Rule 2 - ResolvedFile.AbsolutePath reserves a URI scheme convention

ResolvedFile.AbsolutePath is currently a plain string containing a
local filesystem path (for example, C:\Users\HP\docs\a.txt). In v1.1
it becomes the canonical identifier for a file that may live anywhere,
using standard URI schemes:

    (none)      Local filesystem path (today)     C:\Users\HP\docs\a.txt
    file://     Explicit local filesystem         file:///C:/Users/HP/docs/a.txt
    s3://       AWS S3 or S3-compatible           s3://my-bucket/docs/a.txt
    gs://       Google Cloud Storage              gs://my-bucket/docs/a.txt
    gdrive://   Google Drive (file ID)            gdrive://file/1a2b3c4d
    azure://    Azure Blob Storage                azure://container/blob
    dropbox://  Dropbox (file path)               dropbox:///docs/a.txt

Code that consumes ResolvedFile.AbsolutePath today treats it as a
plain string. In v1.1 a single helper, PathScheme.Parse(string), will
inspect the scheme and dispatch to the correct provider. Adding this
helper does not break existing code: strings without a recognized
scheme are local paths.

Enforcement: No new code should assume AbsolutePath is always a
Windows or POSIX path. Any new code that parses paths must go through
a future IPathScheme helper rather than string-matching separators.

### Rule 3 - IOutputSink returns a URI-shaped destination

SaveResult (returned by IOutputSink.SaveAsync) currently carries a
local filesystem path. In v1.1 it carries a URI using the same scheme
convention as Rule 2. Callers must treat the destination as an opaque
string, never constructing it themselves.

Enforcement: No consumer of SaveResult should manipulate the path
with Path.Combine, Path.GetFullPath, or string concatenation. Treat
it as a black box.

## Cloud-backed Harvest Interfaces (planned)

When cloud-backed Harvest input/output support lands, the following implementations will be added
behind the existing interfaces. No existing interface changes.

    Future Implementation        Implements                Provider
    ---------------------------  ------------------------  --------------------------
    CloudFileResolver            IFileResolver             All providers (by scheme)
    CloudContentReader           IContentReader            All providers
    CloudOutputSink              IOutputSink               All providers
    CloudPathSearchService       IPathSearchService        Provider-native search APIs
    CloudDefaultPathsProvider    IDefaultPathsProvider     Provider-default folders
    CloudAuthManager             (new interface)           OAuth 2.0 + IAM credentials

Six new classes, zero changes to the six existing interfaces. The
resilience pipeline, bundle composer, format writers, and UI binding
all work unchanged.

## Cloud-backed Harvest Roadmap Sketch

1. Add ICloudStorageProvider and CloudAuthManager.
2. Implement the six classes in the table above, one provider at a
   time (S3 first, then Google Drive, then Azure Blob, then others).
3. Add a "Source" dropdown to the input intake panel (Phase 3.1) that
   switches between Local, S3, Google Drive, and so on.
4. Add a Settings -> Credentials panel for OAuth flows and access keys.
5. Store credentials in the OS keychain (Windows Credential Manager,
   macOS Keychain, Linux libsecret) - never in plain files.
6. Add per-provider cost guardrails to the resilience pipeline (cloud
   operations cost money; silent retries must be budgeted).

Estimated effort: 11-15 weeks for the first three providers. The
architecture supports the work; the work itself is new implementations,
not a rewrite.

---

## v1.1 Cloud Delivery (in progress)

Status: the foundations of the v1.1 cloud work are delivered and
verified. The remaining provider-specific and UI phases are scheduled.

### Google Drive Cloud-page download

The Google Drive download action is isolated from Harvest. Its Picker
authorization uses a one-use PKCE state, the existing Google client
registration and securely stored Client Secret, the configured loopback
redirect, and `drive.file` only. The desktop Picker request includes
`trigger_onepick=true` and `allow_multiple=true`; an existing authorization
`state` parameter is replaced with the one-use state issued for this
attempt. The existing normal Connect flow and its callback parser remain
separate and unchanged.

The Platform layer exchanges the Picker authorization code and keeps the
resulting access token only in memory for the selected-file operation.
Before file metadata or content requests, it compares non-empty
`about.get(user(permissionId))` results from the connected account's token
and the Picker token; missing identity or mismatch is a hard failure.
Every selected-file metadata, binary-download, and Workspace-export
request uses the Picker token. `capabilities.canDownload` is checked
before download and again before transfer.

Binary files use Drive media download. Google Docs export as PDF,
Sheets as XLSX, and Slides as PPTX with matching MIME types and filename
extensions. The Cloud page confirms the returned selection before
downloading. Files download sequentially; users may save separately or
save multiple selected files as a ZIP. Outputs use a Save dialog with
Downloads suggested and a temporary sibling file before completion.
Neither OAuth tokens nor selected-file content enter Harvest.

The exact configured `127.0.0.1` Picker callback is a release acceptance
gate: Google documents that Picker adds no additional redirect
restrictions, but the configured callback must still be verified in a
manual disposable-account run. The second gate is successful
non-empty `permissionId` matching from both `about.get` calls under
`drive.file`; any missing result, API failure, or mismatch blocks file
access.

### What is delivered as of this section

- **Credential store.** `ICredentialStore` in `FileHarvester.Platform`,
  with four implementations: `WindowsCredentialStore` (real, Windows
  Credential Manager), `MacOsKeychainStore` (throws a named
  `CloudCredentialUnavailableException`), `LinuxSecretServiceStore`
  (same), and `NotSupportedCredentialStore` (defensive fallback).
- **Registration.** The credential store is registered by a
  Platform-owned extension method `AddFileHarvesterCloudCredentials`,
  called from `App.axaml.cs` immediately after `AddFileHarvester`. It
  is not registered in Core, because Core does not reference Platform.
  This preserves the dependency direction: App -> Core, App ->
  Platform, Platform -> Core.
- **Path primitives.** `PathScheme` (URI scheme detection) and
  `CloudPath` (lexical, host-OS-independent path helpers) in
  `FileHarvester.Core/Domain`.
- **Cloud contracts.** `ICloudProvider`, `CloudEntry`,
  `CloudEntryPage`, `CloudWriteHandle`, `ProviderMetadata`,
  `ProviderProfile`, `FieldSpec`, `CloudProviderKind`, `AuthKind` in
  `FileHarvester.Core/Domain`.
- **Cloud resilience policies.** `cloud-read`, `cloud-write`,
  `cloud-search`, `cloud-auth` registered in `PolicyNames` and in
  `ServiceCollectionExtensions`. See `docs/resilience-usage.md` for
  the full configuration.

### What is deferred

- **macOS Keychain** and **Linux Secret Service**. The interfaces exist;
  the implementations throw `CloudCredentialUnavailableException`.
  Scheduled for a future **Platform Enablement** phase.
- **`AvaloniaOverwriteConfirmer`** (finding F-024), the App-side
  overwrite dialog, is delivered with the cloud UI work in C.8. Until
  then, the App fails closed on collision without prompting.

### Coverage baseline

The test suite currently contains **150 tests** (as of the C.0.5
completion). All pass. The full test list is in
`src/FileHarvester.Tests/`. This baseline is recorded here so that
`README.md`'s pointer to this document is accurate.

## SchemaLence database workspace

SchemaLence is a separate, read-focused database workspace. Its provider-
neutral contracts live in `SchemaLence.Core`; provider implementations,
secure credential adaptation, and local persistence live in
`SchemaLence.Platform`; the Avalonia page and view model live in
`FileHarvester.App`. It is not part of the file Harvest resolver, content
reader, composer, or output sink.

The first provider is PostgreSQL. Metadata access uses catalog queries and
bound parameters; object identifiers used for table reads are quoted
separately from parameterized values. Data pages are limited to 500 rows,
and each provider connection is set to read-only by default. The workspace
has no arbitrary SQL editor or database mutation commands. Its local
SQLite store persists non-secret connection details and backup-history
metadata; passwords are routed only through the existing credential-store
abstraction.

PostgreSQL structure metadata includes pgvector type and dimension
recognition where available. This is inspection only: SchemaLence does not
create or modify vector extensions, columns, or indexes. The provider
contracts are intended to support more database families. MongoDB is the
planned NoSQL milestone; it is not represented as implemented until its
provider, UI behavior, and tests are delivered. MySQL and a dedicated
vector database provider remain later milestones.

Table/view Excel export streams role-visible rows through bounded batches
into an Open XML workbook, without buffering the complete result set.
PostgreSQL custom-archive backups use `pg_dump` and the existing file-write
resilience policy; the password is passed in the child process environment
and is not placed in its arguments. Backup history is written locally only
after the archive has been moved into its final destination. These exports
and backups are separate from Harvest and do not grant mutation access.


---

## v1.1 Cloud Delivery (in progress)

Status: the foundations of the v1.1 cloud work are delivered and
verified. The remaining provider-specific and UI phases are scheduled.

### What is delivered as of this section

- **Credential store.** `ICredentialStore` in `FileHarvester.Platform`,
  with four implementations: `WindowsCredentialStore` (real, Windows
  Credential Manager), `MacOsKeychainStore` (throws a named
  `CloudCredentialUnavailableException`), `LinuxSecretServiceStore`
  (same), and `NotSupportedCredentialStore` (defensive fallback).
- **Registration.** The credential store is registered by a
  Platform-owned extension method `AddFileHarvesterCloudCredentials`,
  called from `App.axaml.cs` immediately after `AddFileHarvester`. It
  is not registered in Core, because Core does not reference Platform.
  This preserves the dependency direction: App -> Core, App ->
  Platform, Platform -> Core.
- **Path primitives.** `PathScheme` (URI scheme detection) and
  `CloudPath` (lexical, host-OS-independent path helpers) in
  `FileHarvester.Core/Domain`.
- **Cloud contracts.** `ICloudProvider`, `CloudEntry`,
  `CloudEntryPage`, `CloudWriteHandle`, `ProviderMetadata`,
  `ProviderProfile`, `FieldSpec`, `CloudProviderKind`, `AuthKind` in
  `FileHarvester.Core/Domain`.
- **Cloud resilience policies.** `cloud-read`, `cloud-write`,
  `cloud-search`, `cloud-auth` registered in `PolicyNames` and in
  `ServiceCollectionExtensions`. See `docs/resilience-usage.md` for
  the full configuration.

### What is deferred

- **macOS Keychain** and **Linux Secret Service**. The interfaces exist;
  the implementations throw `CloudCredentialUnavailableException`.
  Scheduled for a future **Platform Enablement** phase.
- **`AvaloniaOverwriteConfirmer`** (finding F-024), the App-side
  overwrite dialog, is delivered with the cloud UI work in C.8. Until
  then, the App fails closed on collision without prompting.

### Coverage baseline

The test suite currently contains **150 tests** (as of the C.0.5
completion). All pass. The full test list is in
`src/FileHarvester.Tests/`. This baseline is recorded here so that
`README.md`'s pointer to this document is accurate.
