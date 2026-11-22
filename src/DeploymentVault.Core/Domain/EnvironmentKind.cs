// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/EnvironmentKind.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  The deployment environment an InfrastructureRecord belongs to
//           (production, staging, development, or other).
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a (BCL only)
//   Used by    : InfrastructureRecord, VaultQuery
//   See also   : RecordStatus, ServiceCategory
//
// NOTES
//   - Named EnvironmentKind rather than Environment to avoid colliding
//     with System.Environment in files that import both namespaces.
//   - Stored as the enum member name in SQLite. New members are appended.
//   - `Other` exists because a user may describe environments that are
//     not one of the three named tiers (preview, DR, sandbox). The label
//     shown in the UI for `Other` is the enum name; the free-text notes
//     on the record hold the specifics.
// ============================================================================

namespace DeploymentVault.Core.Domain;

/// <summary>
/// The deployment environment an <see cref="InfrastructureRecord"/> belongs to.
/// </summary>
/// <remarks>
/// Stored as the enum member name in SQLite. New members are appended.
/// </remarks>
public enum EnvironmentKind
{
    /// <summary>Production traffic. Highest criticality.</summary>
    Production,

    /// <summary>Pre-production staging that mirrors production.</summary>
    Staging,

    /// <summary>Development or local environment.</summary>
    Development,

    /// <summary>
    /// Any environment that does not fit the named tiers (preview, DR,
    /// sandbox). The specifics live in the record's free-text notes.
    /// </summary>
    Other,
}