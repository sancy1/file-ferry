# File-Ferry - Cloud Verification Safety

Status: Living document. Applies to every [VERIFY-REAL] sub-phase of
Group C. Read this before running any step that touches a real cloud
endpoint.

## Purpose

The cloud work has two phases: [BUILD] (code written, mocked tests
green) and [VERIFY-REAL] (a real endpoint is contacted with a real
credential). This document states the rules that apply to every
[VERIFY-REAL] step.

## The rules

### 1. Disposable resources only

Every verification uses a resource created specifically for it. No
production or shared resource is ever written to.

- AWS S3: a bucket named `file-ferry-verify-<guid>` in the test account.
- Azure Blob: a container named `file-ferry-verify-<guid>`.
- Google Cloud Storage: a bucket named `file-ferry-verify-<guid>`.
- Google Drive: a folder named `File-Ferry Verify <guid>`.
- OneDrive: a folder named `File-Ferry Verify <guid>`.
- Dropbox: a folder named `File-Ferry Verify <guid>`.

### 2. Explicit cleanup

Every [VERIFY-REAL] exit criterion includes "verify the test objects
are deleted." Cleanup commands are provided as PowerShell. The
verification is not complete until the cleanup is confirmed.

### 3. No credentials in logs, files, or test output

The credential value is read from Windows Credential Manager, used,
and never logged. The store key is logged; the value is not. No
exception message contains the credential.

### 4. No credential values in the repository

`%APPDATA%\FileHarvester\cloud-profiles.json` contains only profile
metadata (provider ID, display name, field names). Credential values
live only in Windows Credential Manager, keyed by
`file-ferry/<profile-id>/<instance-name>`.

### 5. Rotation after risky verification

If a verification step exposes the credential to any risk (a failed
signing attempt that might have logged it, a debugger capture, a
crash dump), the user rotates the credential afterward. Each
[VERIFY-REAL] sub-phase states in its exit criteria whether rotation
is recommended.

### 6. The user performs the verification

The user runs the app on their PC with their credential entered through
the app's credential form. The credential is never sent to the
assistant, never written to a file in the repository, never included
in a chat message.

## Per-provider notes

### AWS S3

- Access key IDs start with `AKIA` (long-lived) or `ASIA` (temporary).
- If using temporary credentials, a session token is also required.
- After a failed signing attempt, rotate the access key as a precaution.

### Azure Blob

- Connection strings are account-scoped. A leaked connection string
  exposes the entire account.
- After verification, consider regenerating the account key in the
  Azure portal if the string was ever typed into a terminal.

### Google Cloud Storage

- Service account JSON keys are long-lived.
- After verification, consider disabling the service account key in
  the GCP console and creating a new one.

### OAuth providers (Google Drive, OneDrive, Dropbox)

- The user consents in a browser. The app receives a refresh token.
- The refresh token is stored in Windows Credential Manager.
- Revoking access in the provider's account settings invalidates the
  token and is the standard cleanup if the account is no longer used.

### Google Drive Picker download

- The Picker flow requests only `drive.file`. It uses the same registered
  Google client ID, existing Client Secret credential-store entry, and
  configured loopback redirect as normal Google Connect; normal Connect's
  flow and callback parser are not modified.
- Picker authorization codes, PKCE verifiers, access tokens, callback
  query values, and file contents must never be logged or displayed.
- Picker tokens are held only in Platform memory for the active selection.
  They are never persisted, returned to the App, or substituted for the
  normal connected-account token.
- Before selected-file requests, compare the Drive `permissionId` obtained
  using the saved connection token with the one obtained using the Picker
  token. Both must be present and equal. Failure to retrieve either ID or
  a mismatch stops the download.
- Check `capabilities.canDownload` before each binary download or export.
  Save only to a path selected by the user. Download into a temporary
  sibling file, then move into place only after success. Remove partial
  files on cancellation/failure; surface a cleanup failure instead of
  treating the operation as successful.
- When the Picker response contains multiple selected IDs, show the
  resolved file list for confirmation. Download sequentially. Users can
  save files individually or combine multiple downloadable files into a
  ZIP. Continue past an individual file failure, remove its incomplete ZIP
  entry, and report aggregate success/failure. Whole-Drive listing and
  backup are not part of this flow.
- Manual release verification must prove the configured
  `http://127.0.0.1:53682/callback/` works for Picker specifically; a
  successful normal Connect is not evidence for this separate callback
  contract. Use a disposable Drive binary file plus disposable Doc, Sheet,
  and Slide test files. Confirm account `permissionId` values are
  non-empty and equal with `drive.file` before considering account binding
  verified.
