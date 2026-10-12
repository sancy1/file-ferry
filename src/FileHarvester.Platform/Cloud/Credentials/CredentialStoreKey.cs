// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/CredentialStoreKey.cs
// PURPOSE:  The typed key under which a credential is stored. Carries the
//           credential kind, the provider profile id, and the user-chosen
//           instance name. A static key and an OAuth key for the same
//           profile and instance are distinct; their composed store strings
//           are structurally different by segment count.
// RELATION: Consumed by ITypedCredentialStore. CredentialKey is unchanged;
//           FromCredentialKey bridges the two. The static store-key format
//           is byte-for-byte identical to CredentialKey.ToStoreKey().
// ============================================================================

using System;

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// The typed key under which one credential is stored.
/// </summary>
/// <remarks>
/// <para>
/// Static keys use file-ferry/{ProfileId}/{InstanceName} - the same
/// format CredentialKey produces. OAuth keys use
/// file-ferry/oauth/{ProfileId}/{InstanceName}. The two formats are
/// structurally disjoint: three segments for static, four for OAuth.
/// A profile id that happens to be the literal string oauth is a valid
/// static profile id; its static key is three segments and does not
/// collide with the four-segment OAuth format.
/// </para>
/// <para>
/// Both parts are required and neither may contain the separator
/// character /. The rules match CredentialKey.Validate exactly; no
/// rule is added or removed by this type.
/// </para>
/// </remarks>
public readonly record struct CredentialStoreKey(
    CredentialKind Kind,
    string ProfileId,
    string InstanceName)
{
    private const string Prefix = "file-ferry";
    private const string OAuthNamespace = "oauth";
    private const char Separator = (char)47;

    /// <summary>
    /// Builds a static key from an existing CredentialKey. The composed
    /// store key is byte-for-byte identical to CredentialKey.ToStoreKey().
    /// </summary>
    /// <param name="key">The two-part key. Not null.</param>
    /// <returns>The typed key with Kind = Static.</returns>
    public static CredentialStoreKey FromCredentialKey(CredentialKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new CredentialStoreKey(
            CredentialKind.Static, key.ProfileId, key.InstanceName);
    }

    /// <summary>
    /// Builds a static key for a profile id and instance name.
    /// </summary>
    /// <param name="profileId">The provider profile id. Not null.</param>
    /// <param name="instanceName">The instance name. Not null.</param>
    /// <returns>The typed key with Kind = Static.</returns>
    public static CredentialStoreKey Static(string profileId, string instanceName)
        => new(CredentialKind.Static, profileId, instanceName);

    /// <summary>
    /// Builds an OAuth key for a profile id and instance name. The composed
    /// store key uses the reserved OAuth namespace.
    /// </summary>
    /// <param name="profileId">The provider profile id. Not null.</param>
    /// <param name="instanceName">The instance name. Not null.</param>
    /// <returns>The typed key with Kind = OAuth.</returns>
    public static CredentialStoreKey OAuth(string profileId, string instanceName)
        => new(CredentialKind.OAuth, profileId, instanceName);

    /// <summary>
    /// The composed store key.
    /// </summary>
    /// <returns>The store key string.</returns>
    /// <exception cref="InvalidOperationException">
    /// When ProfileId or InstanceName is null, empty, or contains the
    /// separator character.
    /// </exception>
    public string ToStoreKey()
    {
        Validate();
        return Kind == CredentialKind.OAuth
            ? Prefix + Separator + OAuthNamespace + Separator + ProfileId + Separator + InstanceName
            : Prefix + Separator + ProfileId + Separator + InstanceName;
    }

    /// <summary>
    /// Parses a composed store key back into a typed key. Three segments
    /// with the prefix produce a static key; four segments with the prefix
    /// and the reserved OAuth namespace produce an OAuth key.
    /// </summary>
    /// <param name="storeKey">The composed store key.</param>
    /// <returns>The parsed typed key.</returns>
    /// <exception cref="FormatException">
    /// When the key is not in a recognized format.
    /// </exception>
    public static CredentialStoreKey Parse(string storeKey)
    {
        if (string.IsNullOrEmpty(storeKey))
        {
            throw new FormatException("CredentialStoreKey cannot be null or empty.");
        }

        var parts = storeKey.Split(Separator);

        if (parts.Length == 3 && parts[0] == Prefix)
        {
            return new CredentialStoreKey(CredentialKind.Static, parts[1], parts[2]);
        }

        if (parts.Length == 4 && parts[0] == Prefix && parts[1] == OAuthNamespace)
        {
            return new CredentialStoreKey(CredentialKind.OAuth, parts[2], parts[3]);
        }

        throw new FormatException(
            "CredentialStoreKey must be in file-ferry/{ProfileId}/{InstanceName} " +
            "or file-ferry/oauth/{ProfileId}/{InstanceName} format.");
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId))
        {
            throw new InvalidOperationException("CredentialStoreKey.ProfileId is required.");
        }
        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            throw new InvalidOperationException("CredentialStoreKey.InstanceName is required.");
        }
        if (ProfileId.Contains(Separator))
        {
            throw new InvalidOperationException(
                "CredentialStoreKey.ProfileId must not contain the separator character.");
        }
        if (InstanceName.Contains(Separator))
        {
            throw new InvalidOperationException(
                "CredentialStoreKey.InstanceName must not contain the separator character.");
        }
    }
}
