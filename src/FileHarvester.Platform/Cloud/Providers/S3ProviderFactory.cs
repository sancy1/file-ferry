// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/S3ProviderFactory.cs
// PURPOSE:  Builds an IAmazonS3 client from a connection settings record.
//           Handles AWS S3, Cloudflare R2, Backblaze B2, Wasabi,
//           DigitalOcean Spaces, MinIO, and any other S3-compatible
//           endpoint.
// RELATION: Consumed by the C.8 credential-to-provider wiring. Pure
//           construction: no I/O, no network, no resilience.
// ============================================================================

using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The connection settings for an S3-compatible endpoint.
/// </summary>
/// <param name="AccessKeyId">The access key ID. Required.</param>
/// <param name="SecretAccessKey">The secret access key. Required.</param>
/// <param name="SessionToken">
/// Optional session token. Required only for temporary credentials
/// (STS-issued, access key IDs starting with ASIA).
/// </param>
/// <param name="Region">
/// The AWS region. Optional; defaults to us-east-1. Ignored when
/// CustomEndpointUrl is provided.
/// </param>
/// <param name="CustomEndpointUrl">
/// The endpoint URL for a non-AWS S3-compatible service. Null for AWS S3.
/// </param>
/// <param name="ForcePathStyle">
/// True to use path-style addressing (bucket in the path, not the host).
/// Required for most S3-compatible services. Always true when
/// CustomEndpointUrl is provided.
/// </param>
public sealed record S3ConnectionSettings(
    string AccessKeyId,
    string SecretAccessKey,
    string? SessionToken,
    string? Region,
    string? CustomEndpointUrl,
    bool ForcePathStyle);

/// <summary>
/// Builds S3-compatible clients.
/// </summary>
public static class S3ProviderFactory
{
    private const string DefaultRegion = "us-east-1";

    /// <summary>
    /// Creates an <see cref="IAmazonS3"/> client from the given settings.
    /// </summary>
    /// <param name="settings">The connection settings. Never null.</param>
    /// <returns>A configured client. The caller owns it.</returns>
    public static IAmazonS3 CreateClient(S3ConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.AccessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.SecretAccessKey);

        AWSCredentials credentials;

        if (!string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            credentials = new SessionAWSCredentials(
                settings.AccessKeyId,
                settings.SecretAccessKey,
                settings.SessionToken);
        }
        else
        {
            credentials = new BasicAWSCredentials(
                settings.AccessKeyId,
                settings.SecretAccessKey);
        }

        var config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(settings.CustomEndpointUrl))
        {
            config.ServiceURL = settings.CustomEndpointUrl;
            config.ForcePathStyle = true;
        }
        else
        {
            var regionName = string.IsNullOrWhiteSpace(settings.Region)
                ? DefaultRegion
                : settings.Region;

            config.RegionEndpoint = RegionEndpoint.GetBySystemName(regionName);
            config.ForcePathStyle = settings.ForcePathStyle;
        }

        return new AmazonS3Client(credentials, config);
    }
}
