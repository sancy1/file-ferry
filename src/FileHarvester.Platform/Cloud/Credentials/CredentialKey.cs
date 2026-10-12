// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/CredentialKey.cs
// PURPOSE:  The key under which a credential is stored in the
//           platform credential store. Composed from a provider
//           profile ID and a user-chosen instance name.
// RELATION: Consumed by ICredentialStore and every implementation.
//           The composed store key uses the format
//           "file-ferry/{ProfileId}/{InstanceName}".
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// The key under which one credential is stored.
/// </summary>
/// <remarks>
/// <para>
/// The store key format is <c>file-ferry/{ProfileId}/{InstanceName}</c>.
/// For example, <c>file-ferry/aws-s3/work-account</c>. The instance
/// name distinguishes multiple credentials for the same provider.
/// </para>
/// <para>
/// Both parts are required and neither may contain the separator
/// character <c>/</c>.
/// </para>
/// </remarks>
/// <param name="ProfileId">
/// The provider profile ID, such as <c>aws-s3</c> or <c>cloudflare-r2</c>.
/// </param>
/// <param name="InstanceName">
/// The user-chosen instance name, such as <c>work-account</c>.
/// </param>
public sealed record CredentialKey(string ProfileId, string InstanceName)
{
    private const string Prefix = "file-ferry";
    private const char Separator = (char)47;

    /// <summary>
    /// The full store key: <c>file-ferry/{ProfileId}/{InstanceName}</c>.
    /// </summary>
    /// <returns>The composed key.</returns>
    /// <exception cref="InvalidOperationException">
    /// When <see cref="ProfileId"/> or <see cref="InstanceName"/> is
    /// null, empty, or contains the separator character.
    /// </exception>
    public string ToStoreKey()
    {
        Validate();
        return Prefix + Separator + ProfileId + Separator + InstanceName;
    }

    /// <summary>
    /// Parses a store key back into a <see cref="CredentialKey"/>.
    /// </summary>
    /// <param name="storeKey">
    /// The store key in <c>file-ferry/{ProfileId}/{InstanceName}</c> form.
    /// </param>
    /// <returns>The parsed key.</returns>
    /// <exception cref="FormatException">
    /// When the key is not in the expected format.
    /// </exception>
    public static CredentialKey Parse(string storeKey)
    {
        if (string.IsNullOrEmpty(storeKey))
        {
            throw new FormatException("CredentialKey cannot be null or empty.");
        }

        var parts = storeKey.Split(Separator);
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            throw new FormatException(
                "CredentialKey must be in file-ferry/{ProfileId}/{InstanceName} format.");
        }

        return new CredentialKey(parts[1], parts[2]);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId))
        {
            throw new InvalidOperationException("CredentialKey.ProfileId is required.");
        }
        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            throw new InvalidOperationException("CredentialKey.InstanceName is required.");
        }
        if (ProfileId.Contains(Separator))
        {
            throw new InvalidOperationException(
                "CredentialKey.ProfileId must not contain the separator character.");
        }
        if (InstanceName.Contains(Separator))
        {
            throw new InvalidOperationException(
                "CredentialKey.InstanceName must not contain the separator character.");
        }
    }
}
