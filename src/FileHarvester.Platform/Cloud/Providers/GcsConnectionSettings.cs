// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GcsConnectionSettings.cs
// PURPOSE:  Non-secret connection configuration for Google Cloud Storage.
//           Carries the project id (for billing-scope on some operations)
//           and an optional custom endpoint host (for emulators and
//           sovereign endpoints). It never carries a credential value.
// RELATION: Consumed by GcsProviderFactory to construct the StorageClient.
//           The credential (service-account JSON) is retrieved separately
//           from ICredentialStore and passed to the factory as a string.
//           The split between settings (non-secret) and credential (secret)
//           is deliberate: settings may be inspected, logged, and stored
//           in user configuration; the credential must never be.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Non-secret configuration for a Google Cloud Storage connection.
/// </summary>
/// <param name="ProjectId">
/// The Google Cloud project id used for billing scope on operations that
/// require it. Non-null, non-empty. Not a secret — it is visible in the
/// service-account JSON and in every GCS request URL. Example:
/// <c>my-project-123456</c>.
/// </param>
/// <param name="CustomEndpointHost">
/// Optional custom endpoint host. Null means the default public endpoint
/// (<c>storage.googleapis.com</c>). Set this for emulators (for example
/// <c>localhost:4443</c>) or for sovereign endpoints. Never includes a
/// scheme.
/// </param>
public sealed record GcsConnectionSettings(
    string ProjectId,
    string? CustomEndpointHost = null);
