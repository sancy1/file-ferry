// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/VaultQuery.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  A typed search/filter/sort request passed to
//           IDeploymentVaultStore.ListApplicationsAsync and
//           ListRecordsAsync. Replaces ad-hoc string parameters.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : RecordStatus, ServiceCategory, EnvironmentKind
//   Used by    : IDeploymentVaultStore, SqliteDeploymentVaultStore,
//                DeploymentVaultViewModel
//   See also   : Application, InfrastructureRecord
//
// NOTES
//   - The default instance (no criteria) returns the first page of active
//     rows sorted by most-recently-updated. `Take` defaults to 100; a
//     `Take` of 0 or less is treated as 100 by the store. The plan's
//     Part 8 rule ("unbounded list endpoints are a defect") is honored
//     by always capping the result.
//   - `SearchText` is a substring match against name and provider only.
//     It is case-insensitive. It is NOT a full-text index; Phase A uses
//     LIKE '%x%'. An FTS5 index is a Phase D concern if the corpus grows.
//   - `IncludeDeleted` is false by default. A tombstone view passes true.
//   - Every filter field is nullable. A null filter means "do not filter
//     on this field." This is the correct shape for a typed query.
// ============================================================================

namespace DeploymentVault.Core.Domain;

/// <summary>
/// A typed search/filter/sort request for the Deployment Vault.
/// </summary>
/// <param name="SearchText">
/// Optional substring match against name and provider. Case-insensitive.
/// Null or whitespace means no text filter.
/// </param>
/// <param name="StatusFilter">
/// When set, only rows whose status equals this value are returned.
/// Null means all statuses are returned.
/// </param>
/// <param name="CategoryFilter">
/// Records only. When set, only records whose service category equals
/// this value are returned. Null means all categories. Ignored by
/// ListApplicationsAsync.
/// </param>
/// <param name="EnvironmentFilter">
/// Records only. When set, only records whose environment equals this
/// value are returned. Null means all environments. Ignored by
/// ListApplicationsAsync.
/// </param>
/// <param name="IncludeDeleted">
/// When true, soft-deleted rows are included in the result. When false
/// (the default), they are filtered out.
/// </param>
/// <param name="Skip">
/// Zero-based row offset. Must be >= 0. The store rejects negative values
/// by throwing ArgumentOutOfRangeException.
/// </param>
/// <param name="Take">
/// Maximum rows to return. If <= 0, the store substitutes 100. The store
/// caps at 500 to prevent an accidental unbounded list.
/// </param>
/// <param name="SortBy">
/// Which field to sort on. See <see cref="VaultSortField"/>. Default
/// UpdatedAtDesc.
/// </param>
public sealed record VaultQuery(
    string? SearchText,
    RecordStatus? StatusFilter,
    ServiceCategory? CategoryFilter,
    EnvironmentKind? EnvironmentFilter,
    bool IncludeDeleted,
    int Skip,
    int Take,
    VaultSortField SortBy)
{
    /// <summary>
    /// The default query: first 100 active rows, most-recently-updated first.
    /// </summary>
    public static VaultQuery Default { get; } = new(
        SearchText: null,
        StatusFilter: null,
        CategoryFilter: null,
        EnvironmentFilter: null,
        IncludeDeleted: false,
        Skip: 0,
        Take: 100,
        SortBy: VaultSortField.UpdatedAtDesc);
}

/// <summary>
/// The sort order applied to a <see cref="VaultQuery"/> result.
/// </summary>
/// <remarks>
/// Each member is a complete sort specification, not a field. Adding a
/// field means adding two members (ascending and descending), not one.
/// This makes the store's ORDER BY generation a simple lookup.
/// </remarks>
public enum VaultSortField
{
    /// <summary>Most recently updated first. The default.</summary>
    UpdatedAtDesc,

    /// <summary>Least recently updated first.</summary>
    UpdatedAtAsc,

    /// <summary>Alphabetical by name, case-insensitive.</summary>
    NameAsc,

    /// <summary>Reverse alphabetical by name, case-insensitive.</summary>
    NameDesc,

    /// <summary>Most recently created first.</summary>
    CreatedAtDesc,
}