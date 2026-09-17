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

text

## Cloud-Readiness Decisions

File-Ferry currently targets local files only. Cloud storage
(Google Drive, AWS S3, Azure Blob, Dropbox, OneDrive, GCS, and any
S3-compatible object store) is planned for v1.1. To guarantee that
adding cloud support is a matter of writing new implementations
rather than rewriting the pipeline, three architectural rules are
reserved today.

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

## Cloud Interfaces (v1.1 - Not Yet Implemented)

When cloud support lands, the following implementations will be added
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

## When Cloud Lands - v1.1 Roadmap Sketch

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
