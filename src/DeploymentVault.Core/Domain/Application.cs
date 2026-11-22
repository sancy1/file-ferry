// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/Application.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  A deployed application tracked in the Deployment Vault. One row
//           per application; infrastructure records belong to it.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : RecordStatus, ApplicationNamePolicy (validation is
//                enforced by the caller before the row is stored)
//   Used by    : IDeploymentVaultStore, SqliteDeploymentVaultStore,
//                DeploymentVaultViewModel, ApplicationRowViewModel
//   See also   : InfrastructureRecord, VaultQuery
//
// NOTES
//   - Immutable record. Updates are expressed by `with` expressions on a
//     value read from the store, never by in-place mutation.
//   - `Id` is a time-ordered GUID v7 rendered as the canonical "D" text
//     form. The store reads and writes it as TEXT.
//   - `Name` is unique per lower-case comparison among non-deleted rows,
//     enforced by a partial unique index in the schema. Two applications
//     named "Foo" and "foo" cannot both exist while neither is deleted.
//   - `Tags` is never null; an empty collection means "no tags". The
//     store serializes it as a JSON array string in the `tags` column.
//   - Timestamps are UTC. `DeletedAtUtc` is null when the row is active.
//   - `RecordCount` is not stored on this row. It is computed on demand by
//     IDeploymentVaultStore.CountRecordsAsync. Exposing it here would
//     require a join on every read.
// ============================================================================

namespace DeploymentVault.Core.Domain;

/// <summary>
/// A deployed application tracked in the Deployment Vault.
/// </summary>
/// <param name="Id">The application's stable identifier. GUID v7 as text.</param>
/// <param name="Name">
/// The display name. Non-empty, unique per lower-case comparison among
/// non-deleted rows. See <see cref="ApplicationNamePolicy"/> for the
/// allowed characters.
/// </param>
/// <param name="Description">Optional human description. May be null or empty.</param>
/// <param name="Owner">Optional owner name or team. May be null or empty.</param>
/// <param name="RepositoryUrl">Optional source repository URL. May be null.</param>
/// <param name="ApplicationUrl">Optional public URL of the deployed app. May be null.</param>
/// <param name="Status">The lifecycle status. See <see cref="RecordStatus"/>.</param>
/// <param name="Tags">
/// Zero or more free-form tags. Never null; an empty collection means "no tags".
/// </param>
/// <param name="Notes">Optional free-text notes. May be null or empty.</param>
/// <param name="CreatedAtUtc">Creation timestamp, UTC.</param>
/// <param name="UpdatedAtUtc">Last modification timestamp, UTC.</param>
/// <param name="DeletedAtUtc">
/// Soft-delete tombstone. Null when the row is active. Non-null means the
/// row is hidden from default queries and may be restored or hard-deleted.
/// </param>
public sealed record Application(
    Guid Id,
    string Name,
    string? Description,
    string? Owner,
    string? RepositoryUrl,
    string? ApplicationUrl,
    RecordStatus Status,
    IReadOnlyList<string> Tags,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc);