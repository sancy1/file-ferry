// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/InfrastructureRecord.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  A single infrastructure item (hosting service, database,
//           storage bucket, source repository, monitoring endpoint, ...)
//           that belongs to one Application in the Deployment Vault.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : RecordStatus, ServiceCategory, EnvironmentKind
//   Used by    : IDeploymentVaultStore, SqliteDeploymentVaultStore,
//                DeploymentVaultViewModel, InfrastructureRecordRowViewModel
//   See also   : Application, VaultQuery
//
// NOTES
//   - Immutable record. Updates are expressed by `with` expressions.
//   - `ApplicationId` is the foreign key to `Application.Id`. The schema
//     enforces `ON DELETE CASCADE`, so hard-deleting an application
//     permanently removes its records. Soft-deleting an application does
//     not touch its records.
//   - `Provider` is free text (Render, Vercel, AWS, NeonDB, ...). It is
//     not constrained to a list; the category is the constrained field.
//   - `Tags` is never null; an empty collection means "no tags".
//   - Timestamps are UTC. `DeletedAtUtc` is null when the row is active.
//   - Phase A does not store secrets on this record. The secrets table
//     is Phase C. This record only holds the metadata (URL, email,
//     region, resource id, notes, tags).
// ============================================================================

namespace DeploymentVault.Core.Domain;

/// <summary>
/// An infrastructure item that belongs to one <see cref="Application"/>.
/// </summary>
/// <param name="Id">The record's stable identifier. GUID v7 as text.</param>
/// <param name="ApplicationId">
/// The parent application. Not null. Foreign key to <see cref="Application.Id"/>.
/// </param>
/// <param name="Name">
/// The record's display name (for example "Render API"). Non-empty. Not
/// unique across applications; unique only within the same application
/// is enforced at the UI level, not by the schema.
/// </param>
/// <param name="Provider">
/// The concrete provider name (Render, Vercel, AWS, ...). May be null or
/// empty. Free text.
/// </param>
/// <param name="ServiceCategory">The kind of service. See <see cref="ServiceCategory"/>.</param>
/// <param name="Environment">The deployment environment. See <see cref="EnvironmentKind"/>.</param>
/// <param name="DashboardUrl">Optional provider dashboard URL. May be null.</param>
/// <param name="LoginEmail">
/// Optional login email associated with the record. May be null or empty.
/// In Phase A this is informational only; it is not a credential.
/// </param>
/// <param name="Region">Optional provider region (for example "eu-west-1"). May be null.</param>
/// <param name="ResourceId">
/// Optional provider-side resource identifier (bucket name, database id,
/// project slug). May be null.
/// </param>
/// <param name="Status">The lifecycle status. See <see cref="RecordStatus"/>.</param>
/// <param name="Notes">Optional free-text notes. May be null or empty.</param>
/// <param name="Tags">
/// Zero or more free-form tags. Never null; an empty collection means "no tags".
/// </param>
/// <param name="CreatedAtUtc">Creation timestamp, UTC.</param>
/// <param name="UpdatedAtUtc">Last modification timestamp, UTC.</param>
/// <param name="DeletedAtUtc">
/// Soft-delete tombstone. Null when active. Non-null means hidden from
/// default queries and eligible for restore or hard-delete.
/// </param>
public sealed record InfrastructureRecord(
    Guid Id,
    Guid ApplicationId,
    string Name,
    string? Provider,
    ServiceCategory ServiceCategory,
    EnvironmentKind Environment,
    string? DashboardUrl,
    string? LoginEmail,
    string? Region,
    string? ResourceId,
    RecordStatus Status,
    string? Notes,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc);