// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobConnectionSettings.cs
// PURPOSE:  Non-secret connection configuration for Azure Blob Storage.
//           Carries the account name, an optional custom endpoint suffix
//           (for sovereign clouds and Azurite), and an optional container
//           name. It never carries a credential value.
// RELATION: Consumed by AzureBlobProviderFactory to construct the
//           BlobServiceClient. The credential value is retrieved separately
//           from ICredentialStore and passed to the factory as a string.
//           The split between settings (non-secret) and credential (secret)
//           is deliberate: settings may be inspected, logged, and stored
//           in user configuration; the credential must never be.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Non-secret configuration for an Azure Blob Storage connection.
/// </summary>
/// <param name="AccountName">
/// The Azure Storage account name (the part before
/// <c>.blob.core.windows.net</c>). Non-null, non-empty. Example:
/// <c>mystorageaccount</c>.
/// </param>
/// <param name="CustomEndpointSuffix">
/// Optional custom endpoint suffix. Null means the default public Azure
/// endpoint suffix (<c>core.windows.net</c>). Set this for sovereign
/// clouds (for example <c>core.chinacloudapi.cn</c>,
/// <c>core.usgovcloudapi.net</c>) or for the Azurite emulator
/// (<c>127.0.0.1:10000</c>). Never includes a scheme or a leading dot.
/// </param>
/// <param name="ContainerName">
/// Optional default container name. When set, the factory constructs a
/// <c>BlobContainerClient</c>-scoped URI; when null, the factory
/// constructs a service-scoped URI suitable for container enumeration.
/// </param>
public sealed record AzureBlobConnectionSettings(
    string AccountName,
    string? CustomEndpointSuffix = null,
    string? ContainerName = null);
