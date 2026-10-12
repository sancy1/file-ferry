// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobProviderFactory.cs
// PURPOSE:  Constructs a BlobServiceClient from three inputs: a non-secret
//           settings record, an auth-kind discriminator, and a credential
//           string retrieved from ICredentialStore by the caller. The
//           factory is static and pure — no I/O, no logging, no resilience,
//           no DI. It exists to make the Azure provider testable without
//           live Azure credentials.
// RELATION: Called by the profile-to-settings adapter in C.8. Consumed by
//           AzureBlobProviderTests. Not called by AzureBlobProvider itself —
//           the provider receives an already-constructed client.
// ============================================================================

using System.Globalization;
using Azure.Storage.Blobs;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Builds Azure Blob Storage clients from configuration and credentials.
/// </summary>
public static class AzureBlobProviderFactory
{
    /// <summary>
    /// The default public Azure endpoint suffix, used when the settings
    /// do not specify a custom one.
    /// </summary>
    public const string DefaultEndpointSuffix = "core.windows.net";

    /// <summary>
    /// Constructs a <see cref="BlobServiceClient"/> for the given account,
    /// auth shape, and credential.
    /// </summary>
    /// <param name="settings">
    /// Non-secret connection settings. Must not be null.
    /// </param>
    /// <param name="authKind">
    /// How to interpret <paramref name="credential"/>. Must be a defined
    /// <see cref="AzureBlobAuthKind"/> value.
    /// </param>
    /// <param name="credential">
    /// The credential string. Its shape depends on
    /// <paramref name="authKind"/>:
    /// <list type="bullet">
    ///   <item><see cref="AzureBlobAuthKind.ConnectionString"/>: the full connection string.</item>
    ///   <item><see cref="AzureBlobAuthKind.AccountKey"/>: a base64 account key.</item>
    ///   <item><see cref="AzureBlobAuthKind.SasToken"/>: a SAS token, with or without a leading <c>?</c>.</item>
    /// </list>
    /// Must not be null, empty, or whitespace-only.
    /// </param>
    /// <returns>A new <see cref="BlobServiceClient"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="settings"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="credential"/> is null, empty, or whitespace-only; or
    /// the SAS token contains internal whitespace or a URI fragment.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="authKind"/> is not a defined enum value.
    /// </exception>
    public static BlobServiceClient CreateClient(
        AzureBlobConnectionSettings settings,
        AzureBlobAuthKind authKind,
        string credential)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(credential))
        {
            throw new ArgumentException(
                "Credential must not be null, empty, or whitespace-only.",
                nameof(credential));
        }

        return authKind switch
        {
            AzureBlobAuthKind.ConnectionString =>
                CreateFromConnectionString(credential),

            AzureBlobAuthKind.AccountKey =>
                CreateFromAccountKey(settings, credential),

            AzureBlobAuthKind.SasToken =>
                CreateFromSasToken(settings, credential),

            _ => throw new ArgumentOutOfRangeException(
                nameof(authKind),
                authKind,
                "Unknown Azure Blob auth kind."),
        };
    }

    // ------------------------------------------------------------------
    // Auth-shape builders.
    // ------------------------------------------------------------------

    private static BlobServiceClient CreateFromConnectionString(string connectionString)
    {
        // BlobServiceClient validates the connection string and throws
        // FormatException if it is malformed. We let that propagate — the
        // caller supplied a bad credential, and the caller needs to know.
        return new BlobServiceClient(connectionString);
    }

    private static BlobServiceClient CreateFromAccountKey(
        AzureBlobConnectionSettings settings,
        string accountKey)
    {
        var uri = BuildServiceUri(settings, containerName: null);
        var sharedKey = new Azure.Storage.StorageSharedKeyCredential(
            settings.AccountName,
            accountKey);

        return new BlobServiceClient(uri, sharedKey);
    }

    private static BlobServiceClient CreateFromSasToken(
        AzureBlobConnectionSettings settings,
        string rawSasToken)
    {
        var token = NormalizeSasToken(rawSasToken);
        var uri = BuildServiceUri(settings, settings.ContainerName, appendSas: token);

        return new BlobServiceClient(uri);
    }

    // ------------------------------------------------------------------
    // URI construction.
    // ------------------------------------------------------------------

    private static Uri BuildServiceUri(
        AzureBlobConnectionSettings settings,
        string? containerName,
        string? appendSas = null)
    {
        var suffix = string.IsNullOrWhiteSpace(settings.CustomEndpointSuffix)
            ? DefaultEndpointSuffix
            : settings.CustomEndpointSuffix!.TrimStart('.');

        var builder = new System.Text.StringBuilder();
        builder.Append("https://");
        builder.Append(settings.AccountName);
        builder.Append(".blob.");
        builder.Append(suffix);

        if (!string.IsNullOrWhiteSpace(containerName))
        {
            builder.Append('/');
            builder.Append(containerName.Trim('/'));
        }

        if (!string.IsNullOrEmpty(appendSas))
        {
            builder.Append('/');
            builder.Append('?');
            builder.Append(appendSas);
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    // ------------------------------------------------------------------
    // SAS token normalization — applied exactly once.
    // ------------------------------------------------------------------

    /// <summary>
    /// Normalizes a raw SAS token. Trims leading whitespace, removes a
    /// single leading <c>?</c> if present, and validates that no internal
    /// whitespace and no fragment character remains.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The token is empty after normalization, contains internal
    /// whitespace, or contains a <c>#</c> fragment character.
    /// </exception>
    private static string NormalizeSasToken(string rawSasToken)
    {
        var token = rawSasToken.TrimStart();

        if (token.Length > 0 && token[0] == '?')
        {
            token = token[1..];
        }

        if (token.Length == 0)
        {
            throw new ArgumentException(
                "SAS token is empty after normalization.",
                nameof(rawSasToken));
        }

        foreach (var c in token)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException(
                    "SAS token contains internal whitespace.",
                    nameof(rawSasToken));
            }

            if (c == '#')
            {
                throw new ArgumentException(
                    "SAS token contains a fragment character ('#').",
                    nameof(rawSasToken));
            }
        }

        return token;
    }
}
