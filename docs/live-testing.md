# File-Ferry - Live Cloud Testing

Status: procedure for user-led live validation of the cloud providers.
Read this before running any step that touches a real cloud endpoint.

## 1. Purpose

The cloud work has two phases:

- CDT (consumer-driven tests): offline, deterministic, green. CDT
  proves FileHarvester behavior against substitutes and in-memory
  stores: the code paths, the mapping, the guards, the fixtures.
- Live testing: a real endpoint is contacted with a real credential.
  Live testing validates real provider consent, real token exchange,
  real token refresh, real revocation, real file operations, and real
  disconnect and reconnect behavior.

CDT does not prove real provider behavior. Live testing does not
replace CDT. The two are not substitutes. A green CDT with a red live
test is a failing provider.

This document assumes all six providers are registered at startup:
aws-s3, azure-blob, gcs, gdrive, onedrive, dropbox. The OAuth providers
resolve their client registration and access token lazily, on first
use.

## 2. Safety rules

The rules in docs/cloud-verification-safety.md apply to every step in
this document. Read that document first. This section restates the
requirements that most directly apply here.

- Use disposable test containers, buckets, folders, and objects only.
- Delete only objects created by this verification run.
- Never delete, overwrite, or enumerate production data.
- The user performs every real-account browser action, consent action,
  credential entry, and destructive verification action.
- Rotate or revoke test credentials after risky verification, per the
  safety document.
- Stop immediately on an unexpected permission prompt, an unexpected
  scope request, or access to an unintended resource.

### 2.1 The no-secrets-in-AI rule

Never paste access keys, secret keys, client secrets, connection
strings, tokens, or refresh tokens into an AI chat.

Use placeholders such as <ACCESS_KEY_ID> and <SECRET_KEY>. Follow the
provider console steps yourself. The AI prompt may help you understand
the console and the fields; it must never be used to transmit
credentials.

## 3. Consent

Every real-account browser or consent action requires the user's
explicit consent at the moment it happens. The user completes the
consent in the browser. The application receives the resulting token
through its loopback listener. No automated step in this document
grants consent on the user's behalf.

## 4. Credential-acquisition prompts

Each prompt below can be pasted into an AI chat to get current,
provider-specific setup instructions. Each prompt excludes secrets.
Each tells the AI to explain every field FileHarvester requests, where
to obtain it, and which values are secret.

### 4.1 Amazon S3

I need to obtain credentials for AWS S3 so I can use it in a desktop
application. Please give me: (1) the exact URL of the IAM console page
where I create an access key; (2) the numbered steps from login to the
credential being displayed; (3) what IAM permissions the access key
needs for read, list, and upload access to one bucket; (4) the exact
shape of what I will receive; (5) which of the values are secret and
must never be shared; (6) links to the official AWS documentation;
(7) warnings about what NOT to do with this credential. Do not ask me
to paste the credential. Be concise.

### 4.2 Azure Blob Storage

I need to obtain a connection string for Azure Blob Storage so I can
use it in a desktop application. Please give me: (1) the exact URL of
the Azure portal page; (2) the numbered steps from login to the
connection string being displayed; (3) which storage account
permissions I need for read, list, and upload access to one container;
(4) the exact shape of the connection string; (5) which values inside
it are secret and must never be shared; (6) links to the official
Azure documentation; (7) warnings about what NOT to do with this
credential. Do not ask me to paste the credential. Be concise.

### 4.3 Google Cloud Storage

I need to obtain a service account JSON key for Google Cloud Storage
so I can use it in a desktop application. Please give me: (1) the
exact URL of the GCP console page; (2) the numbered steps from login
to downloading the JSON key; (3) which IAM role I should grant for
read, list, and upload access to one bucket; (4) the exact shape of
what the JSON file will contain; (5) which fields inside it are secret
and must never be shared; (6) links to the official Google Cloud
documentation; (7) warnings about what NOT to do with this file. Do
not ask me to paste the file contents. Be concise.

### 4.4 Google Drive (OAuth)

I want to connect Google Drive to a desktop application. Please
explain: (1) that Google Drive requires the application owner to
register an OAuth client; (2) which Google Cloud console page is
used; (3) the OAuth scopes needed for read, list, and upload access;
(4) the exact redirect URI shape the desktop application uses; (5)
what the user sees when they authorize; (6) which values (client
secret) are secret and must never be shared; (7) links to the official
Google documentation; (8) any review or verification Google requires.
Do not ask me to paste the client secret. Be concise.

### 4.5 Microsoft OneDrive (OAuth)

I want to connect Microsoft OneDrive to a desktop application. Please
explain: (1) that OneDrive requires the application owner to register
an Azure AD app; (2) which Microsoft Entra admin center page is used;
(3) the Microsoft Graph permissions needed for read, list, and upload
access; (4) the exact redirect URI shape the desktop application
uses; (5) what the user sees when they authorize; (6) which values
(client secret) are secret and must never be shared; (7) links to the
official Microsoft documentation; (8) any admin-consent requirements.
Do not ask me to paste the client secret. Be concise.

### 4.6 Dropbox (OAuth)

I want to connect Dropbox to a desktop application. Please explain:
(1) that Dropbox requires the application owner to register an app;
(2) which Dropbox App Console page is used; (3) the OAuth scopes
needed for read, list, and upload access; (4) the exact redirect URI
shape the desktop application uses; (5) what the user sees when they
authorize; (6) which values (app secret) are secret and must never be
shared; (7) links to the official Dropbox documentation; (8) any
review or verification Dropbox requires. Do not ask me to paste the
app secret. Be concise.

## 5. Field-by-field credential explanations

Each table below lists every field FileHarvester requests for that
provider, what it is used for, where to obtain it, whether it is
secret, an example shape (never a real value), and what a missing or
wrong value causes.

### 5.1 Amazon S3

| Field | Purpose | Where to obtain | Secret | Example shape | Common error |
| ----- | ------- | --------------- | ------ | ------------- | ------------ |
| Access Key ID | Identifies the AWS principal FileHarvester uses. | AWS IAM console: Users, Security credentials, Create access key. | Non-secret identifier. | AKIA followed by 16 uppercase alphanumerics. | A malformed value fails the pattern check at Save; a wrong value gives an InvalidAccessKeyId error on the first request. |
| Secret Access Key | The secret half of the access key pair. | Shown once at access-key creation; never retrievable again. | Secret. | A 40-character base64-like string. | A wrong value gives a SignatureDoesNotMatch error on the first request. |
| Region | The AWS region the bucket lives in. | AWS console, top-right region selector, or the bucket's Properties tab. | Non-secret. | us-east-1 | Empty means auto-detect; a wrong value gives a region-mismatch error on the first request. |
| Session Token | Needed only for temporary credentials (STS). | Shown with the temporary credentials when they are issued. | Secret. | A long base64-like string. | Omit for long-lived keys; a wrong value gives an InvalidToken error. |

### 5.2 Azure Blob Storage

| Field | Purpose | Where to obtain | Secret | Example shape | Common error |
| ----- | ------- | --------------- | ------ | ------------- | ------------ |
| Connection String | Carries the account name and the account key for the storage account. | Azure portal: Storage account, Access keys, Connection string. | Secret (it embeds the account key). | DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net | A truncated or malformed string gives a construction failure on first use. |

### 5.3 Google Cloud Storage

| Field | Purpose | Where to obtain | Secret | Example shape | Common error |
| ----- | ------- | --------------- | ------ | ------------- | ------------ |
| Service Account JSON | The full JSON key file for the service account FileHarvester uses. | GCP console: IAM and Admin, Service Accounts, Keys, Add key, Create new key, JSON. | Secret. | A JSON object with type, project_id, private_key, client_email, and related fields. | A missing or malformed JSON gives a construction failure on first use. |

### 5.4 OAuth providers

Google Drive, OneDrive, and Dropbox take no credential field in the
profile form. Their connection is an OAuth client registration, stored
in %APPDATA%\FileHarvester\oauth-clients.json, not a credential value.
The client ID and endpoints are non-secret configuration. The client
secret, where the provider uses one, is secret and is stored through
the credential store, not the profile file.

## 6. Per-provider feature matrix

| Feature | Amazon S3 | Azure Blob | GCS | Google Drive | OneDrive | Dropbox |
| ------- | --------- | ---------- | --- | ------------ | -------- | ------- |
| Connect | Static credentials | Static credentials | Static credentials | OAuth consent | OAuth consent | OAuth consent |
| Read | | | | | | |
| Write | | | | | | |
| List | | | | | | |
| Metadata | | | | | | |
| Delete test object | | | | | | |
| Disconnect | Remove credential | Remove credential | Remove credential | Revoke and delete | Delete (no provider revoke) | Revoke and delete |
| Access blocked after disconnect | | | | | | |
| Reconnect | | | | | | |

Fill each cell after the provider's run with Yes, No, or Skipped, and
record the observed result in section 13.

## 7. Per-provider steps and expected results

Run the checklist once per provider, one provider at a time.

1. Connect.
   - Static provider: enter the credential fields from section 5,
     then save. Expected: the provider appears as connected.
   - OAuth provider: confirm or enter the OAuth client registration,
     click Connect, and complete the browser consent. Expected: the
     provider appears as connected.
2. Read. Read a test file from the provider. Expected: the file
   contents appear and match the source.
3. Write. Write a test file to the provider. Expected: the file exists
   at the provider with the expected content.
4. List. List a test folder or container. Expected: the entries
   include the test file.
5. Metadata. Read metadata for a test object. Expected: size and
   modified time match.
6. Delete. Delete only the test object this run created. Expected: the
   object no longer exists at the provider.
7. Disconnect.
   - Static provider: remove the stored credential.
   - OAuth provider: disconnect. This revokes the token at the
     provider where the provider supports revocation and deletes it
     from the local credential store.
   Expected: the provider shows as disconnected.
8. Confirm blocked. Attempt an operation. Expected: it fails cleanly,
   not silently, not with a wrong result.
9. Reconnect. Reconnect and attempt an operation. Expected: it
   succeeds again.

Use a resource name of the form file-ferry-verify-<guid> for buckets
and containers, and File-Ferry Verify <guid> for folders.

## 8. OneDrive multi-page pagination validation (F-132)

OneDrive listing must follow the Graph @odata.nextLink continuation.
Validate this explicitly:

1. Create or choose a test folder that contains more items than fit
   on a single Graph page. Create enough test files to force at least
   two pages.
2. List the folder. Confirm the first page returns a continuation.
3. Request the next page using that continuation.
4. Confirm that no item is skipped and no item appears twice across
   the two pages.
5. Clean up every test file created for this step.

If the first page returns no continuation, the folder did not exceed
one page; add more test files and repeat.

## 9. Controlled token-refresh validation (OAuth providers)

For Google Drive, OneDrive, and Dropbox, confirm a later operation
succeeds after the access token is refreshed.

- Perform an operation after the access token's natural expiry, or
  after a refresh is triggered, and confirm it succeeds.
- Use the provider's normal refresh path. Do not delete or invalidate
  a real refresh token to force a failure.
- If a refresh cannot be triggered safely, record that as a limitation
  in section 13 and skip the check for that provider.

## 10. Dropbox overwrite and create procedure (F-142)

This is the live verification of the Dropbox write semantics. It
proves that an overwrite replaces the file and that a second write
creates a new file.

1. Create a test folder named File-Ferry Verify <guid> in Dropbox.
2. Create a file test.txt with the single character A. Confirm it
   exists and its size is 1.
3. Overwrite test.txt with the single character B. Confirm its size is
   still 1 and its content is B, not AB.
4. Create a second file test2.txt with the single character C.
   Confirm it exists.
5. Clean up: delete test.txt, delete test2.txt, then delete the test
   folder.
6. Confirm the test folder no longer exists.

## 11. Troubleshooting-prompt template

Paste this template into an AI chat when a step fails. Fill in only
the non-secret fields. Do not paste any credential value.

    Provider: <provider display name>
    URI scheme: <s3:// | azure:// | gs:// | gdrive:// | onedrive:// | dropbox://>
    Operation: <connect | read | write | list | metadata | delete | disconnect | reconnect>
    Error category: <authentication | authorization | not found | rate limit | network | other>
    Occurred: <before consent | after consent>
    Exact error text (with any credential value redacted): <text>
    Question: what are the most likely causes and the provider-console checks I should make?

Never include an access key, secret key, client secret, connection
string, token, or refresh token in the prompt.

## 12. Deferred items

Every deferred item below is classified non-blocking for user testing.
None prevents a provider from being validated live.

| Item | Summary | Status |
| ---- | ------- | ------ |
| F-140 | MicrosoftGraphOneDriveClient has no direct unit or CDT seam. | Open, non-blocking. |
| F-109 follow-up | The credential-resolution reason reaches the user; a further refinement is scheduled. | Open, non-blocking. |
| F-110, F-113 | The disconnect lifecycle is validated by live testing, not by CDT. | Open, non-blocking. |
| F-088 through F-091 | Remote provider search has no caller in the running app; it is opt-in and not wired. | Open, non-blocking. |

## 13. Per-provider result record template

After each provider's run, record the outcome. One block per provider.

    Provider: <name>
    Date: <date>
    Connected: yes / no
    Read: yes / no - <file>
    Write: yes / no - <file>
    List: yes / no - <folder>
    Metadata: yes / no
    Delete test object: yes / no
    Disconnect: yes / no
    Access blocked after disconnect: yes / no
    Reconnect: yes / no
    Token refresh validated: yes / no / skipped - <reason>
    OneDrive pagination validated: yes / no / not applicable
    Findings: <any divergence, with the exact observed behavior>
    Follow-up needed: <yes / no - what>

Do not include credentials, tokens, account identifiers, or secret
values in the record.

## 14. What live testing does not prove

- It does not prove the CDT still passes; run the suite separately.
- It does not replace the offline tests; a live pass with a red suite
  is not a pass.
- It does not validate a provider whose OAuth client registration is
  absent; a missing registration throws on first use by design.
- It does not prove production readiness of the hosting environment;
  it proves the provider integration.

End of the live cloud testing procedure.
