Dynamic Cloud Provider Integration — Design and Implementation Plan
Scope: the dynamic-profile system that lets users connect any cloud endpoint that speaks a protocol File-Ferry already supports, without rebuilding the app. This plan is a standalone document, ready to paste into the handover note. It does not modify the six shipped adapters; it wraps them in a registry that makes them reachable from user-created profiles.

Status: design. No code written. Every decision here is subject to your approval before the first line is written.

Part 1 — What "dynamic" means, precisely
The word "dynamic" is doing a lot of work in this discussion. Let me pin it down so the plan is unambiguous.

A profile is dynamic. A user can create, edit, and delete a profile at runtime. The profile is metadata: a display name, a choice of adapter kind, an endpoint URL, a region, a bucket/container name, an optional path prefix, and a credential-field schema. Nothing about the profile requires a rebuild.

A protocol implementation is fixed. The six adapter classes — S3CompatibleProvider, AzureBlobProvider, GcsObjectProvider, GoogleDriveProvider, OneDriveProvider, DropboxProvider — are compiled into the app. A profile says which adapter to use, not how to talk to the API. A genuinely new API shape needs a code change, and this plan does not pretend otherwise.

A credential is neither. Credentials are secrets. They live in ICredentialStore (Windows Credential Manager on Windows). The profile metadata never contains a credential value. The profile names the credential; the store holds it.

The three-part split is the whole design. Profile = user data. Adapter = code. Credential = secret. Three concerns, three storage mechanisms, three lifecycles.

Part 2 — The capability this unlocks
After C.8, a user can connect:

Cloud How
AWS S3 shipped profile, or custom
Cloudflare R2 shipped profile, or custom (endpoint is https://<account-id>.r2.cloudflarestorage.com)
Backblaze B2 (S3-compatible) shipped profile, or custom
Wasabi shipped profile, or custom
DigitalOcean Spaces shipped profile, or custom
MinIO (self-hosted) shipped profile, or custom
Any S3-compatible endpoint custom
Azure Blob Storage shipped profile, or custom
Google Cloud Storage shipped profile
Any Azure Blob-compatible endpoint (Azurite, MinIO gateway for Azure, etc.) custom
Any GCS-compatible endpoint (fake-gcs-server, self-hosted gateway) custom
Google Drive shipped OAuth profile
Microsoft OneDrive / SharePoint shipped OAuth profile
Dropbox shipped OAuth profile
That is the whole long tail of "clouds that accept an API key" plus the three OAuth providers File-Ferry ships.

What it does not unlock: Box, pCloud, Mega, Nextcloud, Notion, Airtable, Slack, or any other provider whose API shape is not one of the six supported protocols. Adding one of those is a code change — a new adapter — and that is a future release, not a configuration.

Part 3 — The four storage mechanisms
The system stores four different kinds of data in four different places. Keeping them separate is the single most important rule.

Data Where Why
Built-in profiles FileHarvester.Core/Domain/BuiltInProfiles.cs — compiled into the app Curated by the app author. Shipped. Trusted.
Custom profiles %APPDATA%\FileHarvester\cloud-profiles.json — user-writable JSON User-created metadata. Nonauthoritative. Never contains a secret.
Credential values Windows Credential Manager (via ICredentialStore) OS-protected. Never in a file the user can read. Never in a repo. Never logged.
OAuth client configuration %APPDATA%\FileHarvester\oauth-clients.json — app-owner-writable JSON Client IDs, authorization URLs, token URLs, scopes, redirect settings. Not client secrets — desktop OAuth uses PKCE, no secret.
The last row is the one the reviewer's note highlighted and I am making explicit: OAuth client IDs are configuration, not credentials, and not built into the binary. Putting them in a user-writable file lets the app owner rotate a client ID without rebuilding the app. If the file is missing or a required client ID is absent, the corresponding OAuth option is shown as unavailable in the UI, with a clear explanation — not a crash, not a silent failure.

Part 4 — The registry
The registry is the seam between "what the user configured" and "what the adapters can do." It has one public interface.

csharp
public interface ICloudProviderRegistry
{
// Merges built-in and custom profiles, deduplicates by ID, and returns
// them in a stable order: built-ins first (in BuiltInProfiles order),
// then customs (in file order).
Task<IReadOnlyList<ProviderProfile>> GetProfilesAsync(
CancellationToken cancellationToken = default);

    // Constructs the provider for a profile. Returns null when the
    // adapter kind is unsupported or the credential is missing; the
    // caller distinguishes "unsupported" from "not configured" via
    // RegistryResult.Status.
    Task<ICloudProvider?> CreateProviderAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken = default);

}

public sealed record RegistryResult(
ICloudProvider? Provider,
RegistryStatus Status,
string? Reason);

public enum RegistryStatus
{
Available = 0,
MissingCredential = 1,
UnsupportedAdapter = 2,
AdapterConstructionFailed = 3,
ProfileInvalid = 4,
}
Design decisions, each with a reason.

D1 — GetProfilesAsync is async
Reason: it reads the custom-profiles JSON from disk. That read runs through IResilienceExecutor (the file-read policy), which is async. A synchronous signature would force the read to block. Async, through the policy, is the rule.

D2 — CreateProviderAsync returns null with a status, not an exception
Reason: null + status is a normal outcome — a profile whose credential has not been entered yet, a profile whose adapter kind is unsupported. An exception would force the caller to catch a type that is not truly exceptional. The result carries the reason, so the UI can show a specific message.

D3 — The result is a record with a status enum
Reason: three distinct states — available, credential missing, adapter unsupported — need to reach the UI as three different messages. A null alone would conflate them. The record carries all three.

D4 — Built-in profiles are never overridable by a custom profile with the same ID
Reason: if a user creates a custom profile with the ID aws-s3, the built-in aws-s3 is authoritative. A duplicate custom ID is rejected during loading. Deterministic, no shadowing.

D5 — Custom profiles with duplicate IDs within the custom file: last-write-wins, with a warning logged
Reason: the store from C.2.3 already uses last-write-wins for SaveCustomAsync. Reading follows the same rule. The registry logs a warning so a user who accidentally has two entries with the same ID can see it. Not an error — the app continues.

D6 — Credentials are resolved lazily
Reason: GetProfilesAsync returns profile metadata. It does not touch the credential store. The credential is read only when CreateProviderAsync is called, only for the profile being constructed. Listing profiles does not require credentials for every profile.

D7 — The registry is a singleton, but its CreateProviderAsync is stateless
Reason: the registry holds no per-call state. Two calls to CreateProviderAsync with the same profile construct two independent providers. The callers own the providers they receive. This matches the DI model where ICloudProvider implementations are stateless factories.

Part 5 — The adapter factories
The registry dispatches to a per-kind factory. Each factory knows how to build one adapter from one profile and one credential.

csharp
public interface ICloudProviderFactory
{
CloudProviderKind Kind { get; }
Task<ICloudProvider?> CreateAsync(
ProviderProfile profile,
string credential,
CancellationToken cancellationToken);
}
Six implementations, one per adapter kind:

Factory Builds Reads from the credential
S3CompatibleProviderFactory S3CompatibleProvider Access key ID + secret + optional session token
AzureBlobProviderFactory AzureBlobProvider Connection string OR account name + account key OR SAS
GcsObjectProviderFactory GcsObjectProvider Service account JSON (the whole file content)
GoogleDriveProviderFactory GoogleDriveProvider OAuth refresh token (exchange for access token via cloud-auth)
OneDriveProviderFactory OneDriveProvider OAuth refresh token
DropboxProviderFactory DropboxProvider OAuth refresh token
The factories are registered in DI, keyed by CloudProviderKind. The registry resolves them via IEnumerable<ICloudProviderFactory> and indexes by Kind.

Where do the six adapters come from?

S3CompatibleProvider — delivered (C.3.1).

AzureBlobProvider — C.4.

GcsObjectProvider — C.5.

GoogleDriveProvider — C.6.

OneDriveProvider, DropboxProvider — C.7.

C.8 supplies the registry and the factories; the six adapters are the phases before it.

Part 6 — The credential schema per kind
When a user creates a custom profile, the app knows what fields to ask for because the adapter kind declares them. The user does not invent the schema; the schema comes from the kind.

The pre-filled schema per kind (from BuiltInProfiles.cs's templates):

Kind Credential fields
S3Compatible accessKeyId (Text, required), secretAccessKey (Password, required), sessionToken (Password, optional), endpointUrl (Text, required for custom — pre-filled for named profiles), region (Text, optional), bucket (Text, required)
AzureBlob authMode (Choice: ConnectionString / AccountKey / SAS), then the fields for that mode: connectionString (Multiline), or accountName + accountKey, or sasToken
GcsObject serviceAccountJson (File, required), bucket (Text, required)
OAuth kinds No credential fields. The OAuth flow supplies the token.
The Add Custom Cloud dialog renders these fields dynamically. The user sees exactly the fields their chosen kind requires. No field the user does not need, no field the user has to invent.

What the user supplies that is not in the schema:

Display name (free text, required).

Adapter kind (dropdown, required).

Endpoint URL (pre-filled from the kind's default; editable for custom).

Region, bucket, path prefix (from the kind's fields).

What the user never sees: the endpoint of a shipped profile (they use the shipped one), the field names (they are provided), the credential value (it goes to the credential store, not to the profile file).

Part 7 — The lifecycle of a custom profile
Creation.

User opens Add Custom Cloud.

Picks a display name and an adapter kind.

The dialog renders the kind's schema.

The user fills in the endpoint and the credential fields.

The user clicks Test Connection.

ICloudProviderRegistry.CreateProviderAsync(profile, credential) builds the provider.

The provider performs a cheap existence check against the user's bucket.

On success, the profile is saved to cloud-profiles.json, the credential is written to ICredentialStore, and the profile appears in the Source and Destination dropdowns.

Use.

User selects the profile from the Source dropdown.

The registry resolves the profile by ID.

The registry reads the credential from the store.

The registry constructs the provider.

The provider is registered for the profile's scheme via Func<PathSchemeKind, ICloudProvider?>.

Resolution, reading, writing, and cross-provider transfer all flow through the provider.

Deletion.

User deletes a custom profile.

The profile is removed from cloud-profiles.json.

The credential is removed from ICredentialStore.

Any in-flight harvest using that profile completes or is cancelled by the user; the next harvest sees the profile gone.

Editing.

User edits a custom profile.

The dialog opens with the current field values.

Endpoint and non-secret fields are pre-filled from the profile.

Secret fields are left blank — the user re-enters the credential, or leaves them blank to keep the existing one.

Save overwrites the profile and, if a new credential was entered, the credential store.

Every step above is a stable marker for a consumer-driven test. Part 9 names them.

Part 8 — The dynamic parts, named precisely
To answer your question directly: what is dynamic, and what is not.

Dynamic — user can change at runtime, no rebuild:

Display name of a custom profile.

Adapter kind of a custom profile (from the fixed set of six).

Endpoint URL.

Region, tenant, account name.

Bucket/container/root path.

Optional path prefix.

Credential fields within the kind's schema.

Which profiles appear in the Source and Destination dropdowns.

Which profiles are connected.

Not dynamic — requires a code change:

A new adapter kind. Adding a BoxProvider is a new class, a new factory, a new CloudProviderKind value. It is not a configuration.

A new credential field type. FieldKind (Text / Password / Multiline / File / Choice) is a closed enum.

A new scheme prefix. PathSchemeKind (Local / File / S3 / Gcs / GoogleDrive / AzureBlob / Dropbox) is a closed enum. A new provider with a new scheme needs a code change.

This is the correct split. The user can adapt the app to any cloud that speaks a supported protocol. The app author can add a new protocol with a code change. No user-invented code execution. No plugin DLLs. No configuration-driven bytecode.

Part 9 — Consumer-driven tests for the dynamic registry
This is the part you asked for specifically. The tests are consumer-driven: they act as the App, using only what the profile system exposes to the App. They do not test the registry's internals. They test the outcome a downstream consumer sees.

The consumer and what it does
The consumer is the App layer — specifically, the Source dropdown, the Destination dropdown, and the harvest pipeline. The tests are written from that consumer's point of view:

A consumer adds a custom profile through the registry's public API, with a specific kind, endpoint, and credential.

A consumer lists profiles and expects to see built-ins plus the custom one.

A consumer creates a provider for a profile and expects a working provider.

A consumer attempts to create a provider for a profile with a missing credential and expects a specific status.

A consumer attempts to create a provider for an unsupported adapter kind and expects a specific status.

A consumer uses the provider through the full pipeline — resolve, read, compose, write — and expects a working file.

Every test asserts what the consumer sees, not what the registry does internally.

The test files and what each does
DynamicProfileRegistryTests.cs — 12 tests.

# Test What the consumer does What it asserts

1 ConsumerListsBuiltInProfiles Calls GetProfilesAsync on a fresh registry. The result contains the eleven built-ins by ID, in BuiltInProfiles order.
2 ConsumerAddsCustomProfileAndListsIt Saves a custom profile via ProviderProfileStore. Calls GetProfilesAsync. The result contains the custom profile by ID, after the built-ins.
3 ConsumerCannotShadowBuiltInId Saves a custom profile with ID aws-s3. Calls GetProfilesAsync. The built-in aws-s3 is still the one returned; the custom is rejected and logged.
4 ConsumerSeesDuplicateCustomIdRejected Saves two customs with ID dup. Calls GetProfilesAsync. Only one custom appears. The last-write-wins. A warning was logged.
5 ConsumerCreatesS3CompatibleProvider Registers a custom S3-compatible profile. Stores the credential. Calls CreateProviderAsync. RegistryResult.Status == Available. Provider is an S3CompatibleProvider.
6 ConsumerCreatesProviderWithoutCredential Registers a profile. Does not store a credential. Calls CreateProviderAsync. Status == MissingCredential. Provider == null. Reason names the profile and credential key.
7 ConsumerCreatesProviderForUnsupportedKind Registers a profile with a kind that no factory handles (test double). Calls CreateProviderAsync. Status == UnsupportedAdapter. Provider == null. Reason names the kind.
8 ConsumerListsProfilesWithoutTouchingCredentials Stores no credentials at all. Calls GetProfilesAsync. The call succeeds. The credential store's read method was never called.
9 ConsumerCreatesProviderConstructsTheCorrectAdapter Registers two profiles with different kinds. Calls CreateProviderAsync on each. Each returns the correct concrete type for its kind.
10 ConsumerPersistsAndReloadsProfiles Saves a custom profile. Builds a fresh registry over the same store directory. Calls GetProfilesAsync. The custom profile is present in the fresh registry.
11 ConsumerDeletesCustomProfile Saves a profile. Deletes it. Calls GetProfilesAsync. The custom profile is gone. Built-ins are unaffected.
12 ConsumerListsProfilesWithMalformedCustomFile Writes garbage to cloud-profiles.json. Calls GetProfilesAsync. Built-ins are returned. No exception. A warning was logged.
DynamicProfileEndToEndTests.cs — 8 tests. These are the tests that prove the whole pipeline works from a consumer's point of view.

# Test What the consumer does What it asserts

13 ConsumerHarvestsFromACustomProfile Registers a custom S3-compatible profile pointing at a stub provider. Builds the whole DI graph. Runs a harvest. A SaveResult with Success == true. The stub provider's OpenReadAsync was called with the correct path.
14 ConsumerWritesToACustomProfile Same setup, but the harvest writes to the custom profile's destination. The stub provider's OpenWriteAsync and CompleteAsync were called.
15 ConsumerTransfersBetweenTwoCustomProfiles Two custom profiles with different schemes. Harvest reads from one, writes to the other. Both providers were called. The bytes flow through.
16 ConsumerSeesUnavailableProfileInTheList A custom profile with no stored credential. CreateProviderAsync is called by the app to prepare the dropdown entry. Status == MissingCredential. The app can render "Credential required" next to the profile.
17 ConsumerReusesTheSameProviderAcrossRequests The app calls CreateProviderAsync once and reuses the provider for multiple ResolveAsync calls. The provider's state is not reset. Each call succeeds.
18 ConsumerAddsProfileWithAnEndpointOverride A custom S3-compatible profile with an endpointUrl different from the built-in. Harvest runs. The stub provider was built with the custom endpoint. The built-in's default was not used.
19 ConsumerAddsProfileWithARegionOverride Custom profile with a region different from the default. The stub provider was built with the region.
20 ConsumerCannotCreateProviderForProfileWithMalformedEndpoint Custom profile with an endpoint URL that fails to parse. CreateProviderAsync. Status == AdapterConstructionFailed. Provider == null. Reason names the endpoint.
DynamicProfileDocumentationTests.cs — 4 documentation conformance tests.

# Test Assertion

21 DocumentationListsAllSixAdapterKinds The new dynamic section of the handover doc names the six kinds. The CloudProviderKind enum has exactly those six.
22 DocumentationClaimsCustomProfilesAreMetadataOnly ProviderProfile has no property of type string whose name contains "secret" or "key" or "password". A structural assertion on the type, not a prose search.
23 DocumentationClaimsCredentialValuesLiveInTheStore CloudOutputSink, CloudFileResolver, CloudContentReader read credentials only through ICredentialStore. A reflection assertion on constructor parameters.
24 DocumentationClaimsOAuthClientConfigIsExternal oauth-clients.json is not embedded in the binary. A reflection assertion on App — it does not reference the file at build time.
Total: 24 tests.

How the tests are achieved — the mechanics
Fixtures. No TestHarness — the dynamic registry needs a per-test temp directory and its own DI graph. Each test class uses a per-test constructor and Dispose, same pattern as CloudOutputSinkTests and DispatchRegistrationTests.

Stub adapters. The registry is tested with stub factories for the six kinds. A StubCloudProviderFactory returns a StubCloudProvider configured by the test. No real adapter is exercised in the registry tests. The adapters have their own tests (C.3.1, C.4, C.5, C.6, C.7).

Stub credential store. The registry resolves ICredentialStore. In tests, an InMemoryCredentialStore is used — implemented in the test assembly, not the app.

Temp storage. Each test writes cloud-profiles.json to a per-test directory, same as ProviderProfileStoreTests and CloudProfilesTests.

The DI graph for the end-to-end tests. Real AddFileHarvester, real AddFileHarvesterCloudCredentials, real AddFileHarvesterCloudDispatch, then a test-only AddFileHarvesterCloudRegistryForTesting(...) that registers the stub factories. The rest is the real composition. This is what makes them consumer-driven: they exercise the same DI graph the App builds, with only the leaf adapters stubbed.

Fault injection. Tests 6 and 16 use ICredentialStore configured to return null for a specific key. Test 12 writes garbage to cloud-profiles.json. Test 20 uses a malformed endpoint URL. None uses an integration endpoint.

Documentation tests use structural markers, not prose. Tests 21–24 reflect on the code — enum values, property names, constructor parameters. A test fails because a contract is wrong, not because the wording was rephrased.

Part 10 — What must be in the UI, exactly
The UI is C.8. It is not part of this plan's code, but the tests in Part 9 assume the App exposes specific capabilities. The plan requires:

The Source selector (Input panel). A dropdown above the paste box with:

Local (default).

One entry per available profile (built-in + custom), grouped by kind.

An "Add Custom Cloud…" entry.

An "Unavailable" state for a profile whose credential is missing, with a tooltip.

The Destination selector (Output panel). Same shape, same entries.

The Add Custom Cloud dialog. Fields:

Display name (Text, required).

Adapter kind (Dropdown, six options).

Endpoint URL (pre-filled from kind, editable).

Region / tenant / bucket / path prefix (per kind).

Credential fields (rendered from kind's schema).

A "Test Connection" button — runs CreateProviderAsync and a cheap check.

An "I don't know my cloud" button — opens the LlmPromptGenerator panel (from C.2.2) with a prompt the user can copy into any LLM.

Save / Cancel.

The Cloud Connections dialog. A list of profiles:

Each row shows the display name, the kind, and a status indicator (Connected / Credential required / Unsupported).

Each row has an Edit and a Delete.

A "Refresh" button.

The OAuth Client Configuration path. %APPDATA%\FileHarvester\oauth-clients.json. The app reads it at startup and caches it. The path is documented in the user guide. The file is not shipped empty — if it is missing, the app shows the OAuth options as "OAuth not configured" and links to the docs.

Part 11 — Resilience, storage, and the rules
Every file read and write in the dynamic system routes through IResilienceExecutor:

Operation Policy
ProviderProfileStore.ListCustomAsync — read cloud-profiles.json file-read
ProviderProfileStore.SaveCustomAsync — write cloud-profiles.json bundle-write
ProviderProfileStore.DeleteCustomAsync — write bundle-write
OAuthClientConfig.LoadAsync — read oauth-clients.json file-read
ICloudProviderRegistry.CreateProviderAsync — resolve credentials file-read (via ICredentialStore)
No new policy names. The dynamic system uses the three existing local policies. Only the cloud operations themselves use the four cloud policies.

The rules from the credential-safety doc apply unchanged:

No credential values in cloud-profiles.json. Ever.

No credential values in oauth-clients.json. Client IDs, no client secrets.

No credential values in logs, exceptions, or test output.

The registry is a singleton with no per-call state.

Part 12 — Implementation order and test placement
The dynamic work is C.8.1 through C.8.6, a sub-phase of C.8, executed in this order:

Sub-phase Delivers Tests Where
C.8.1 ICloudProviderFactory interface and six stub factories 0 (interfaces) —
C.8.2 ICloudProviderRegistry and CloudProviderRegistry 4 Contracts/DynamicProfileRegistryContractTests.cs
C.8.3 ProviderProfileStore integration (already exists, wires the registry) 3 Contracts/DynamicProfileStoreContractTests.cs
C.8.4 The registry's CreateProviderAsync dispatch 5 Contracts/DynamicProfileFactoryContractTests.cs
C.8.5 The Add Custom Cloud dialog and the Source/Destination selectors 8 ConsumerDriven/DynamicProfileEndToEndTests.cs (manual verification)
C.8.6 The documentation tests 4 Documentation/DynamicProfileDocumentationTests.cs
Total: 24 test methods in three tiers. This is the plan you asked for — the tests are consumer-driven in the sense that they act as the App using only the registry's public interface, and the end-to-end tests exercise the real DI graph with only the leaf adapters stubbed.

After C.8: the six providers are reachable from the App, and a user can add any S3/Azure/GCS-compatible cloud without a rebuild.

Part 13 — What remains hardcoded, and why
To be explicit, one final time:

Hardcoded Reason
The six adapter kinds (CloudProviderKind enum) A new protocol needs a new adapter class. Code.
The six scheme prefixes (PathSchemeKind enum) A new protocol needs a new prefix. Code.
The credential field kinds (FieldKind enum) A new field type needs a new UI control. Code.
The OAuth provider list (Google Drive, OneDrive, Dropbox) Each provider's OAuth flow is provider-specific and the client must be registered with the provider. Code.
Everything else is dynamic. The list of clouds the user can connect is not hardcoded — it is BuiltInProfiles ∪ ProviderProfileStore.ListCustomAsync(), evaluated at runtime. The user's list of clouds can grow without a rebuild.

End of plan.

This document is ready to paste into the handover note. It is self-contained, it names every file, every interface, every test, and every storage mechanism. It does not require any change to C.3–C.7 (the six adapters). It supersedes the "hardcoded list" framing entirely.

What I need from you before this plan becomes executable:

Confirm the four storage mechanisms (built-in profiles, custom profiles, credential store, OAuth client config).

Confirm the registry interface (ICloudProviderRegistry with GetProfilesAsync and CreateProviderAsync returning a RegistryResult).

Confirm the test plan — 24 tests across three tiers, with the consumer-driven tier as the proof.

Confirm the scope of "dynamic" — profiles are dynamic; protocols are not.

Reply "confirmed" and I close this design as approved. Then I proceed with C.4 — the Azure Blob provider — as the next phase in the existing plan.

My recommendation: start with C.4 — Azure Blob provider
Not with the dynamic registry (C.8). Not with a return to S3 (C.3.5). Start with C.4.

Why C.4 first

1. The dynamic registry needs six adapters to be worth anything. A registry with one adapter (S3) is a registry that lists one cloud. The value of the dynamic design is that any S3-compatible cloud works. That value only materializes when the other five adapters exist. Building the registry before the adapters is building a dispatch table with one entry.

2. C.4 exercises the patterns C.3 established. The S3 provider is the reference. The Azure provider is the second implementation of ICloudProvider. The second implementation is where the abstraction either holds or breaks. If ICloudProvider is shaped wrong, C.4 is where we find out — on one provider, cheaply — instead of after six.

3. C.4 has no dependency on the UI, on OAuth, or on the dynamic registry. It is a standalone phase: one provider, one factory, one set of tests. It can be delivered, verified, and closed without touching anything else.

4. The Azure SDK is a genuinely different shape from the AWS SDK. Where S3 uses bucket + key, Azure uses container + blob. Where S3 uses IAmazonS3, Azure uses BlobServiceClient / BlobContainerClient / BlobClient. Where S3 has access keys, Azure has connection strings, account keys, SAS tokens. Getting a second storage backend to fit ICloudProvider proves the interface is not S3-shaped by accident.

5. It is the shortest path to the second provider being real. C.4 is one phase, roughly 20 tests, roughly the same size as C.3.1. It can be done in one sequence without interleaving.

Why not the other candidates
Why not the dynamic registry first? Reason: with one adapter, the registry has one factory. The registry's merge logic, dedup, status codes, and end-to-end tests all depend on having multiple kinds to dispatch to. Building it now means building it against S3 only and re-verifying the whole thing when C.4, C.5, C.6, C.7 land. That is rework, not progress.

Why not return to C.3.5 (S3 real verification) first? Reason: S3 real verification needs the App to have a cloud UI, and the App's cloud UI is C.8. C.3.5 cannot be completed today. It has been correctly deferred. Running it now would need a throwaway console tool that proves less than the App will.

Why not start OAuth (C.6)? Reason: OAuth is the largest single piece — loopback listener, token store, three provider flows, one client-registration pipeline. It is also independent of the storage providers. It can come after Azure, GCS, and the storage-side architecture is proven. Sequencing it later is safer.

Why not the dynamic registry and C.4 together? Reason: the manual's discipline says one phase at a time. C.4 is a phase; the registry is a sub-phase of C.8. They are not peers. Combining them means two unverified changes in one build.

The order I recommend, in one line
C.4 → C.5 → C.6 → C.7 → C.8 (including the dynamic registry) → C.9 → the six manual verifications.

C.4 — Azure Blob provider. Second ICloudProvider. Proves the abstraction.

C.5 — Google Cloud Storage provider. Third storage backend, service-account JSON.

C.6 — OAuth infrastructure + Google Drive. The largest single phase.

C.7 — OneDrive + Dropbox. Reuses the OAuth infrastructure.

C.8 — the App UI and the dynamic registry. This is where the six adapters become reachable and the user can add custom profiles.

C.9 — cross-provider transfer.

Then the six manual verifications — one per provider, against real endpoints, with real credentials.

What C.4 delivers, in one paragraph
AzureBlobProvider implementing ICloudProvider, AzureBlobProviderFactory building it from a connection string or account key or SAS token, and roughly twenty tests — mocked via the Azure SDK's test doubles, plus the same behavioral contract tests the S3 provider has. It compiles, it passes, it exposes the same ICloudProvider surface. It is the second backend, and it is where the interface earns its keep.

What I need from you
Confirm C.4 as the next phase. Or tell me a different starting point and I will follow it.

If confirmed, I send the C.4 design pre-write. It will follow the same shape as C.3.1's design: the exact AzureBlobProvider surface, the AzureBlobProviderFactory surface, the credential shapes Azure accepts (connection string / account key / SAS), the resilience wrapping for each ICloudProvider method, the test inventory, and the two new files C.4 will create. No code until you approve the design.

No file will be written until you confirm the starting point and approve the C.4 design.
