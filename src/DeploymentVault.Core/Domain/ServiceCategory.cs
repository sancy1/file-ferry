// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/ServiceCategory.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  The kind of infrastructure service an InfrastructureRecord
//           describes (hosting, database, storage, secrets, source control,
//           monitoring, DNS, or other).
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a (BCL only)
//   Used by    : InfrastructureRecord, VaultQuery, the record editor
//   See also   : RecordStatus
//
// NOTES
//   - Stored as the enum member name in SQLite. New members are appended;
//     existing members are never renumbered or renamed.
//   - `Other` is the escape hatch for a provider that does not fit any
//     named category. The `provider` free-text field on the record holds
//     the concrete name (Render, Vercel, AWS, ...).
//   - Phase A uses this only as a display label and a filter value. It is
//     not used to dispatch behavior; the store treats all categories
//     identically. If a later phase adds per-category fields, that is an
//     additive change (new nullable columns), not a renumbering.
// ============================================================================

namespace DeploymentVault.Core.Domain;

/// <summary>
/// The kind of infrastructure service an <see cref="InfrastructureRecord"/>
/// describes.
/// </summary>
/// <remarks>
/// Stored as the enum member name in SQLite. New members are appended.
/// </remarks>
public enum ServiceCategory
{
    /// <summary>Application hosting and compute (Render, Vercel, Fly, ...).</summary>
    Hosting,

    /// <summary>Managed or self-hosted database service (NeonDB, RDS, ...).</summary>
    Database,

    /// <summary>Object storage, file storage, backups (S3, R2, ...).</summary>
    Storage,

    /// <summary>Secret manager, password vault, KMS (Vault, AWS Secrets Manager, ...).</summary>
    Secrets,

    /// <summary>Source control or CI (GitHub, GitLab, Bitbucket, ...).</summary>
    SourceControl,

    /// <summary>Monitoring, logging, alerting (Datadog, Sentry, ...).</summary>
    Monitoring,

    /// <summary>DNS and domain management (Cloudflare, Route 53, ...).</summary>
    Dns,

    /// <summary>Any service that does not fit the named categories.</summary>
    Other,
}