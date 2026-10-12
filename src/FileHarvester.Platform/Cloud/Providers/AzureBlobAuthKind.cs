// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobAuthKind.cs
// PURPOSE:  Enumerates the three authentication shapes Azure Blob Storage
//           supports in File-Ferry v1.1. Entra ID (OAuth) is deferred to
//           C.6 and is not a member of this enum until that phase.
// RELATION: Consumed by AzureBlobProviderFactory to decide which
//           BlobServiceClient constructor to use, and by the profile
//           validator to reject unsupported combinations before the
//           provider is constructed.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The authentication shape of an Azure Blob Storage credential.
/// </summary>
/// <remarks>
/// The credential value itself is stored as an opaque string in
/// <c>ICredentialStore</c>. This enum tells the factory how to interpret
/// that string. Three shapes are supported in C.4.
/// </remarks>
public enum AzureBlobAuthKind
{
    /// <summary>
    /// The credential string is a full Azure Storage connection string,
    /// for example:
    /// <c>DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net</c>.
    /// The factory constructs the client directly from the string.
    /// </summary>
    ConnectionString = 0,

    /// <summary>
    /// The credential string is a base64-encoded account key. The
    /// account name is read from <c>AzureBlobConnectionSettings</c>.
    /// The factory constructs a <c>StorageSharedKeyCredential</c>.
    /// </summary>
    AccountKey = 1,

    /// <summary>
    /// The credential string is a SAS token, with or without a leading
    /// <c>?</c>. The account name is read from
    /// <c>AzureBlobConnectionSettings</c>. The factory constructs the
    /// full URI including the token.
    /// </summary>
    SasToken = 2,
}
