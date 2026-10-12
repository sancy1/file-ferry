// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/CloudFileResolver.cs
// PURPOSE:  ICloudFileResolver implementation. Turns a cloud-shaped
//           FileRequest into a ResolvedFile by asking the correct
//           ICloudProvider whether the object exists and, if it does,
//           what its metadata is. A resolution failure is mapped to a
//           safe, actionable user note.
// RELATION: Implements ICloudFileResolver. Consumed by
//           DispatchingFileResolver. Provider selection is by scheme and
//           optional instance, via an async factory injected at
//           construction.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// Resolves cloud-shaped requests by dispatching to the correct
/// <see cref="ICloudProvider"/> for the request scheme and optional
/// instance.
/// </summary>
public sealed class CloudFileResolver : ICloudFileResolver
{
    private readonly Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>> _providerFactory;

    /// <summary>Creates a resolver.</summary>
    /// <param name="providerFactory">
    /// Resolves the provider for a scheme and optional instance. Never null.
    /// </param>
    public CloudFileResolver(
        Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>> providerFactory)
    {
        _providerFactory = providerFactory ??
            throw new ArgumentNullException(nameof(providerFactory));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return Array.Empty<ResolvedFile>();
        }

        var resolved = new List<ResolvedFile>();
        var processed = 0;

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await ResolveOneAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.File is not null)
            {
                resolved.Add(outcome.File);
            }

            processed++;

            progress?.Report(new ResolveProgress(
                Index: outcome.File?.Request.Index ?? request.Index,
                Total: requests.Count,
                Processed: processed,
                Status: outcome.File is not null
                    ? FileStatus.Found
                    : FileStatus.NotFound,
                ResolvedPath: outcome.File?.AbsolutePath,
                Note: outcome.Note));
        }

        return resolved;
    }

    private sealed record OneOutcome(
        ResolvedFile? File,
        string? Note);

    private async Task<OneOutcome> ResolveOneAsync(
        FileRequest request,
        CancellationToken cancellationToken)
    {
        PathSchemeInfo scheme;
        try
        {
            scheme = PathScheme.Parse(request.RawInput);
        }
        catch (FormatException)
        {
            return new OneOutcome(null, "Malformed cloud path: " + request.RawInput);
        }

        if (scheme.Kind == PathSchemeKind.Local)
        {
            return new OneOutcome(null, null);
        }

        var resolution = await _providerFactory(
            scheme.Kind, scheme.Instance, cancellationToken).ConfigureAwait(false);

        if (!resolution.IsResolved)
        {
            return new OneOutcome(null, BuildFailureNote(scheme, resolution.Failure));
        }

        var provider = resolution.Provider!;

        var exists = await provider.ExistsAsync(
            request.RawInput, cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            return new OneOutcome(null, null);
        }

        var meta = await provider.GetMetadataAsync(
            request.RawInput, cancellationToken).ConfigureAwait(false);

        var resolved = new ResolvedFile(
            Request: request,
            AbsolutePath: request.RawInput,
            SizeBytes: meta.SizeBytes,
            LastModifiedUtc: meta.LastModifiedUtc ?? DateTime.UtcNow,
            Confidence: ResolutionConfidence.Searched);

        return new OneOutcome(resolved, null);
    }

    /// <summary>
    /// Maps a resolution failure to a safe, actionable user note. The note
    /// never contains a credential value, secret, token, or connection
    /// string.
    /// </summary>
    internal static string BuildFailureNote(
        PathSchemeInfo scheme,
        CloudResolutionFailure? failure)
    {
        var prefix = PathScheme.GetSchemePrefix(scheme.Kind) ?? scheme.Scheme;
        var displayName = DisplayNameFor(scheme.Kind);

        return failure switch
        {
            CloudResolutionFailure.NoCredential =>
                "No credential is connected for " + displayName + ". Connect " + displayName + " first.",

            CloudResolutionFailure.MultipleCredentials =>
                "Multiple " + displayName + " credentials are connected. Specify one using " + prefix + "://[instance]/...",

            CloudResolutionFailure.UnknownInstance =>
                "No " + displayName + " credential named [" + (scheme.Instance ?? string.Empty) + "] is connected.",

            CloudResolutionFailure.ProviderConstructionFailed =>
                displayName + " could not be started because its provider failed to construct.",

            _ =>
                "No provider is registered for the " + prefix + ":// scheme in this build.",
        };
    }

    /// <summary>
    /// The user-facing display name for a scheme. Falls back to the scheme
    /// prefix when no built-in profile matches.
    /// </summary>
    private static string DisplayNameFor(PathSchemeKind kind)
    {
        foreach (var profile in BuiltInProfiles.All)
        {
            var mapped = MapKind(kind);
            if (profile.Kind == mapped)
            {
                return profile.DisplayName;
            }
        }

        return PathScheme.GetSchemePrefix(kind) ?? "the provider";
    }

    /// <summary>
    /// The provider kind that serves a scheme. The inverse of the registry
    /// map. Only the four static-credential schemes are mapped; an OAuth
    /// scheme maps to its own kind.
    /// </summary>
    private static CloudProviderKind MapKind(PathSchemeKind kind) => kind switch
    {
        PathSchemeKind.S3          => CloudProviderKind.S3Compatible,
        PathSchemeKind.AzureBlob   => CloudProviderKind.AzureBlob,
        PathSchemeKind.Gcs         => CloudProviderKind.GcsObject,
        PathSchemeKind.GoogleDrive => CloudProviderKind.GoogleDrive,
        PathSchemeKind.Dropbox     => CloudProviderKind.Dropbox,
        _                          => CloudProviderKind.S3Compatible,
    };
}
