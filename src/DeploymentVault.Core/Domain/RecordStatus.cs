// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/RecordStatus.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  The lifecycle status of an Application or InfrastructureRecord.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a (BCL only)
//   Used by    : Application, InfrastructureRecord, IDeploymentVaultStore,
//                VaultQuery, every view model that filters on status
//   See also   : ServiceCategory, EnvironmentKind
//
// NOTES
//   - The set of statuses differs between Application (Active, Archived,
//     Deleted) and InfrastructureRecord (Active, Inactive, Archived, Deleted).
//     `Inactive` exists only for records: a record may be paused without
//     being archived. An application with an inactive record is still Active.
//   - The stored value in SQLite is the enum member name (Active, Archived,
//     ...), never the integer. The database CHECK constraint on the
//     `status` column lists exactly these names, so a hand-edited database
//     cannot hold a value the app cannot parse.
//   - Soft delete is represented by `deleted_at_utc IS NOT NULL`, not by
//     this enum. `Deleted` here is a transition state used only when a row
//     is being permanently deleted and the caller wants the tombstone
//     to reflect it. In normal operation, soft-deleted rows keep their
//     pre-delete status and are filtered by `deleted_at_utc`.
// ============================================================================

namespace DeploymentVault.Core.Domain;

/// <summary>
/// The lifecycle status of an <see cref="Application"/> or
/// <see cref="InfrastructureRecord"/>.
/// </summary>
/// <remarks>
/// Stored as the enum member name in SQLite. New members are appended;
/// existing members are never renumbered or renamed, because the stored
/// strings are the contract.
/// </remarks>
public enum RecordStatus
{
    /// <summary>The row is active and is returned by default queries.</summary>
    Active,

    /// <summary>
    /// The row is paused. Records only. An inactive record is not returned
    /// by default list queries but is not deleted and can be reactivated.
    /// </summary>
    Inactive,

    /// <summary>
    /// The row is archived. Archived rows are not returned by default
    /// list queries but remain readable and can be restored.
    /// </summary>
    Archived,

    /// <summary>
    /// The row is in the process of being permanently deleted. Normally
    /// not used — soft delete is expressed by <c>deleted_at_utc</c>. This
    /// member exists for the permanent-delete confirmation flow, which
    /// sets the status before the row is hard-deleted in the same
    /// transaction.
    /// </summary>
    Deleted,
}