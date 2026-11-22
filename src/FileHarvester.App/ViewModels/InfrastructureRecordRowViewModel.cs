// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/InfrastructureRecordRowViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (Deployment Vault registry)
// purpose:  One row in the Deployment Vault's infrastructure-record list.
//           Wraps an InfrastructureRecord domain record.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : DeploymentVault.Core.Domain.InfrastructureRecord
//   Used by    : DeploymentVaultViewModel.Records
//   See also   : ApplicationRowViewModel, DeploymentVaultViewModel
//
// NOTES
//   - Read-only display wrapper. No commands. Selection is managed by
//     DeploymentVaultViewModel.SelectedRecord.
//   - `StatusLabel` and `CategoryLabel` and `EnvironmentLabel` are string
//     forms for the UI, computed from the enum members. The UI binds to
//     the string forms; the enum is exposed as a typed property for
//     filters.
// ============================================================================

using System.Globalization;
using DeploymentVault.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the Deployment Vault's infrastructure-record list.
/// </summary>
public sealed class InfrastructureRecordRowViewModel : ViewModelBase
{
    /// <summary>Builds the row from a domain record.</summary>
    /// <param name="source">The record. Not null.</param>
    public InfrastructureRecordRowViewModel(InfrastructureRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <summary>The underlying domain record. Never null.</summary>
    public InfrastructureRecord Source { get; }

    /// <summary>The record's identifier.</summary>
    public Guid Id => Source.Id;

    /// <summary>The parent application's identifier.</summary>
    public Guid ApplicationId => Source.ApplicationId;

    /// <summary>The record's display name.</summary>
    public string Name => Source.Name;

    /// <summary>The provider (Render, Vercel, ...), or empty string.</summary>
    public string Provider => Source.Provider ?? string.Empty;

    /// <summary>The service category, string form for display.</summary>
    public string CategoryLabel => Source.ServiceCategory.ToString();

    /// <summary>The environment, string form for display.</summary>
    public string EnvironmentLabel => Source.Environment.ToString();

    /// <summary>The lifecycle status, string form for display.</summary>
    public string StatusLabel => Source.Status.ToString();

    /// <summary>True when the row is soft-deleted.</summary>
    public bool IsDeleted => Source.DeletedAtUtc.HasValue;

    /// <summary>Creation timestamp, UTC.</summary>
    public DateTimeOffset CreatedAtUtc => Source.CreatedAtUtc;

    /// <summary>Last modification timestamp, UTC.</summary>
    public DateTimeOffset UpdatedAtUtc => Source.UpdatedAtUtc;

    /// <summary>Creation timestamp formatted for table display.</summary>
    public string CreatedAtUtcText => CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Last modification timestamp formatted for table display.</summary>
    public string UpdatedAtUtcText => UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}