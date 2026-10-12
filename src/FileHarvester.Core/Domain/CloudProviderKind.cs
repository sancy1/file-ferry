// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/CloudProviderKind.cs
// PURPOSE:  The kind of cloud storage a provider profile represents.
//           Distinguishes S3-compatible endpoints from Azure Blob and
//           GCS, and the consumer file-sync providers.
// RELATION: Part of ProviderProfile. Consumed by the profile store,
//           the dynamic form engine, and the C.3 provider dispatcher.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The kind of cloud storage a provider profile represents.
/// </summary>
/// <remarks>
/// <para>
/// A single Kind can serve many named providers. S3Compatible covers
/// AWS S3 itself plus MinIO, Cloudflare R2, Backblaze B2, Wasabi,
/// DigitalOcean Spaces, Linode Object Storage, and any other
/// S3-compatible endpoint.
/// </para>
/// <para>
/// The enum is deliberately closed. Adding a new storage kind is a
/// code change, not a configuration.
/// </para>
/// </remarks>
public enum CloudProviderKind
{
    /// <summary>AWS S3 and any S3-compatible endpoint.</summary>
    S3Compatible = 0,

    /// <summary>Azure Blob Storage.</summary>
    AzureBlob = 1,

    /// <summary>Google Cloud Storage.</summary>
    GcsObject = 2,

    /// <summary>Google Drive.</summary>
    GoogleDrive = 3,

    /// <summary>Microsoft OneDrive or SharePoint.</summary>
    OneDrive = 4,

    /// <summary>Dropbox.</summary>
    Dropbox = 5,
}
