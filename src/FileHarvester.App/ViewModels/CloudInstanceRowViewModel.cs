// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CloudInstanceRowViewModel.cs
// PURPOSE:  One connected instance in the cloud connections list. Shows the
//           instance name and carries a Disconnect command supplied by the
//           parent view model.
// RELATION: Created by CloudPageViewModel. Bound by CloudPage.axaml.
// ============================================================================

using System.Windows.Input;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One connected instance under a provider.
/// </summary>
public sealed class CloudInstanceRowViewModel
{
    /// <summary>
    /// Creates the row.
    /// </summary>
    /// <param name="providerId">The provider id. Not null.</param>
    /// <param name="instanceName">The instance name. Not null.</param>
    /// <param name="disconnectCommand">
    /// The command that disconnects this instance. Not null.
    /// </param>
    public CloudInstanceRowViewModel(
        string providerId,
        string instanceName,
        ICommand disconnectCommand,
        ICommand? chooseGoogleDriveFileCommand = null)
    {
        ProviderId = providerId;
        InstanceName = instanceName;
        DisconnectCommand = disconnectCommand;
        ChooseGoogleDriveFileCommand = chooseGoogleDriveFileCommand;
    }

    /// <summary>The provider id (for example <c>gdrive</c>).</summary>
    public string ProviderId { get; }

    /// <summary>The instance name.</summary>
    public string InstanceName { get; }

    /// <summary>Disconnects this instance.</summary>
    public ICommand DisconnectCommand { get; }

    /// <summary>Starts a Google Drive file selection and download.</summary>
    public ICommand? ChooseGoogleDriveFileCommand { get; }

    /// <summary>True for a Google Drive connection row.</summary>
    public bool IsGoogleDrive =>
        string.Equals(ProviderId, "gdrive", StringComparison.Ordinal);
}
