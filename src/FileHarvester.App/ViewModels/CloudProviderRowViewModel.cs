// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CloudProviderRowViewModel.cs
// PURPOSE:  One provider row in the cloud connections list. Shows the
//           display name, the icon key, whether the provider is OAuth or
//           static-credential, the connectable state, the action label,
//           the status text, and the connected instances.
// RELATION: Created by CloudPageViewModel. Bound by CloudPage.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One provider in the cloud connections list.
/// </summary>
public sealed class CloudProviderRowViewModel : ViewModelBase
{
    /// <summary>
    /// Creates the row.
    /// </summary>
    /// <param name="profile">The provider profile. Not null.</param>
    /// <param name="providerId">The provider id (for example <c>gdrive</c>).</param>
    /// <param name="displayName">The user-visible name.</param>
    /// <param name="iconKey">The icon key, or null.</param>
    /// <param name="isConnectable">
    /// True when this provider can be connected through the OAuth flow.
    /// </param>
    /// <param name="connectCommand">
    /// The command that starts a connection or opens the credential form.
    /// Not null; disabled when the row has no action.
    /// </param>
    public CloudProviderRowViewModel(
        ProviderProfile profile,
        string providerId,
        string displayName,
        string? iconKey,
        bool isConnectable,
        ICommand connectCommand)
    {
        Profile = profile;
        ProviderId = providerId;
        DisplayName = displayName;
        IconKey = iconKey;
        IsConnectable = isConnectable;
        ConnectCommand = connectCommand;
        Instances = new ObservableCollection<CloudInstanceRowViewModel>();

        // StatusText is computed from Instances.Count. The collection
        // is mutated (Add/Clear), never replaced, so the notification
        // must come from CollectionChanged, not a property setter.
        Instances.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>The provider profile.</summary>
    public ProviderProfile Profile { get; }

    /// <summary>The provider id.</summary>
    public string ProviderId { get; }

    /// <summary>The user-visible name.</summary>
    public string DisplayName { get; }

    /// <summary>The icon key, or null for the default icon.</summary>
    public string? IconKey { get; }

    /// <summary>
    /// True when this provider connects through the OAuth flow. False for
    /// static-credential providers.
    /// </summary>
    public bool IsConnectable { get; }

    /// <summary>Starts a connection or opens the credential form for this provider.</summary>
    public ICommand ConnectCommand { get; }

    /// <summary>The connected instances for this provider.</summary>
    public ObservableCollection<CloudInstanceRowViewModel> Instances { get; }

    private string _instanceName = string.Empty;

    /// <summary>
    /// The user-entered instance name for this provider row. Trimmed
    /// by the Connect runner before validation; stored verbatim here.
    /// </summary>
    public string InstanceName
    {
        get => _instanceName;
        set => SetProperty(ref _instanceName, value);
    }

    /// <summary>
    /// The action label on the row: Connect for OAuth, Configure for
    /// static-credential providers.
    /// </summary>
    public string ConnectLabel => IsConnectable ? "Connect" : "Configure";

    /// <summary>
    /// True when the row has an action available: OAuth Connect, or
    /// Configure for a static provider that declares credential fields.
    /// </summary>
    public bool CanConfigure => IsConnectable || Profile.CredentialFields.Count > 0;

    /// <summary>
    /// The status text shown on the row: the static-credential notice,
    /// the connected count, or the not-connected text.
    /// </summary>
    public string StatusText =>
        !IsConnectable
            ? "Static credentials"
            : Instances.Count == 0
                ? "Not connected"
                : Instances.Count + (Instances.Count == 1 ? " connection" : " connections");
}
