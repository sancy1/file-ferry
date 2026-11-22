// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/ApplicationRowViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (Deployment Vault registry)
// purpose:  One row in the Deployment Vault's application list. Wraps an
//           Application domain record and exposes display properties.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : DeploymentVault.Core.Domain.Application
//   Used by    : DeploymentVaultViewModel.Applications
//   See also   : InfrastructureRecordRowViewModel, DeploymentVaultViewModel
//
// NOTES
//   - Read-only display wrapper. No commands. Selection is managed by
//     DeploymentVaultViewModel.SelectedApplication; the row itself does
//     not know it is selected.
//   - The Domain record is exposed as `Source` so the VM can rebuild a
//     display row when the underlying record updates.
// ============================================================================

using System.Globalization;
using DeploymentVault.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the Deployment Vault's application list.
/// </summary>
public sealed class ApplicationRowViewModel : ViewModelBase
{
    /// <summary>Builds the row from a domain record.</summary>
    /// <param name="source">The application. Not null.</param>
    /// <param name="recordCount">Active records under the application.</param>
    public ApplicationRowViewModel(Application source, int recordCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        RecordCount = recordCount;
    }

    /// <summary>The underlying domain record. Never null.</summary>
    public Application Source { get; }

    /// <summary>The application's identifier.</summary>
    public Guid Id => Source.Id;

    /// <summary>The application's display name.</summary>
    public string Name => Source.Name;

    /// <summary>Optional description, or empty string.</summary>
    public string Description => Source.Description ?? string.Empty;

    /// <summary>Optional owner, or empty string.</summary>
    public string Owner => Source.Owner ?? string.Empty;

    /// <summary>The lifecycle status.</summary>
    public RecordStatus Status => Source.Status;

    /// <summary>True when the row is soft-deleted.</summary>
    public bool IsDeleted => Source.DeletedAtUtc.HasValue;

    /// <summary>Number of active records under this application.</summary>
    public int RecordCount { get; }

    /// <summary>Creation timestamp, UTC.</summary>
    public DateTimeOffset CreatedAtUtc => Source.CreatedAtUtc;

    /// <summary>Last modification timestamp, UTC.</summary>
    public DateTimeOffset UpdatedAtUtc => Source.UpdatedAtUtc;

    /// <summary>Short formatted description for the card (e.g. "3 records").</summary>
    public string RecordCountText => RecordCount == 1 ? "1 record" : $"{RecordCount} records";

    /// <summary>The lifecycle status, string form for display.</summary>
    public string StatusLabel => Source.Status.ToString();

    /// <summary>Creation timestamp formatted for table display.</summary>
    public string CreatedAtUtcText => CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Last modification timestamp formatted for table display.</summary>
    public string UpdatedAtUtcText => UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}