// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/CloudNameCatalog.cs
// PURPOSE:  The in-memory catalog of known cloud names, loaded once from
//           the embedded JSON resource cloud-name-catalog.json. Provides
//           alias lookup. Holds public provider metadata only; never a
//           credential.
// RELATION: Consumed by LocalCatalogResolver. The JSON resource is
//           embedded in this assembly.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// The in-memory cloud name catalog.
/// </summary>
/// <remarks>
/// Loaded once from the embedded resource. Immutable after load. A
/// malformed or missing resource yields an empty catalog, not a throw:
/// name resolution degrades to no candidates rather than failing the app.
/// </remarks>
public sealed class CloudNameCatalog
{
    private const string ResourceName = "FileHarvester.Platform.Cloud.Discovery.Resources.cloud-name-catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<CatalogEntry> _entries;

    /// <summary>
    /// Creates the catalog from the embedded resource.
    /// </summary>
    public CloudNameCatalog()
    {
        _entries = Load();
    }

    /// <summary>
    /// Creates the catalog from an explicit entry list. Used by tests.
    /// </summary>
    /// <param name="entries">The entries. Not null.</param>
    public CloudNameCatalog(IReadOnlyList<CatalogEntry> entries)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    /// <summary>Every catalog entry.</summary>
    public IReadOnlyList<CatalogEntry> Entries => _entries;

    private static IReadOnlyList<CatalogEntry> Load()
    {
        try
        {
            var assembly = typeof(CloudNameCatalog).GetTypeInfo().Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return Array.Empty<CatalogEntry>();
            }

            var list = JsonSerializer.Deserialize<List<CatalogEntry>>(stream, JsonOptions);
            return list ?? (IReadOnlyList<CatalogEntry>)Array.Empty<CatalogEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<CatalogEntry>();
        }
    }

    /// <summary>
    /// One catalog entry: the canonical name and its aliases.
    /// </summary>
    /// <param name="CanonicalName">The user-visible name.</param>
    /// <param name="Aliases">The known aliases. Never null.</param>
    /// <param name="ProviderId">The provider id.</param>
    /// <param name="AuthKind">The authentication shape.</param>
    /// <param name="OfficialUrl">The provider official URL, or null.</param>
    public sealed record CatalogEntry(
        string CanonicalName,
        IReadOnlyList<string> Aliases,
        string ProviderId,
        AuthKind AuthKind,
        string? OfficialUrl);
}
