// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/CloudNameCandidate.cs
// PURPOSE:  One provider candidate returned by the name resolver. Carries
//           the canonical name, the provider id, the matched aliases, the
//           auth kind, the official URL, the source of the candidate, and
//           the confidence of the match. Holds no credential.
// RELATION: Produced by ICloudNameResolver implementations and consumed by
//           the cloud discovery UI.
// ============================================================================

using System.Collections.Generic;
using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// Where a candidate came from.
/// </summary>
public enum CloudNameSource
{
    /// <summary>The bundled local catalog.</summary>
    Local = 0,

    /// <summary>A custom profile the user defined.</summary>
    UserProfile = 1,

    /// <summary>The opt-in remote resolver.</summary>
    Remote = 2,
}

/// <summary>
/// How strong the match is.
/// </summary>
public enum CloudNameConfidence
{
    /// <summary>The typed text equals an alias, case-insensitively.</summary>
    Exact = 0,

    /// <summary>The typed text contains an alias, or an alias contains it.</summary>
    Strong = 1,

    /// <summary>A loose match; the user must confirm.</summary>
    Weak = 2,
}

/// <summary>
/// One provider candidate from the name resolver.
/// </summary>
/// <param name="CanonicalName">The user-visible name (for example Google Drive).</param>
/// <param name="ProviderId">The provider id (for example gdrive).</param>
/// <param name="Aliases">The aliases that matched.</param>
/// <param name="AuthKind">The authentication shape of the provider.</param>
/// <param name="OfficialUrl">The provider official URL, or null.</param>
/// <param name="Source">Where the candidate came from.</param>
/// <param name="Confidence">How strong the match is.</param>
public sealed record CloudNameCandidate(
    string CanonicalName,
    string ProviderId,
    IReadOnlyList<string> Aliases,
    AuthKind AuthKind,
    string? OfficialUrl,
    CloudNameSource Source,
    CloudNameConfidence Confidence);
