// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CloudPageViewModel.cs
// PURPOSE:  View model for the cloud connections page. Builds the provider
//           list from the built-in profiles, loads the connected instances
//           for the OAuth providers, and holds the connect and disconnect
//           commands. Static-credential providers raise ConfigureRequested
//           so the page can open the credential form.
// RELATION: Exposed as MainWindowViewModel.Cloud. Bound by CloudPage.axaml.
//           ConfigureRequested is handled by CloudPage.axaml.cs.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using Avalonia.Threading;
using FileHarvester.Platform.Cloud.Services;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// View model for the cloud connections page.
/// </summary>
public sealed class CloudPageViewModel : ViewModelBase, IDisposable
{
    private readonly ICloudConnectionService _connections;
    private readonly IOAuthClientConfigStore _configStore;
    private readonly ICredentialStore _credentials;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private string _providerFilter = string.Empty;
    private CancellationTokenSource? _driveDownloadCts;
    private bool _isDriveDownloadInProgress;
    private string _driveDownloadStatus = string.Empty;

    /// <summary>
    /// Creates the view model.
    /// </summary>
    /// <param name="connections">The cloud connection service. Not null.</param>
    /// <param name="configStore">The OAuth client config store. Not null.</param>
    public CloudPageViewModel(
        ICloudConnectionService connections,
        IOAuthClientConfigStore configStore,
        ICredentialStore credentials)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));

        DismissMessageCommand = new RelayCommand(() => ConnectionMessage = string.Empty);

        ConnectCommand = new AsyncRelayCommand<CloudProviderRowViewModel>(RunConnectAsync);
        DisconnectCommand = new AsyncRelayCommand<CloudInstanceRowViewModel>(RunDisconnectAsync);
        ChooseGoogleDriveFileCommand = new AsyncRelayCommand<CloudInstanceRowViewModel>(
            RunGoogleDriveDownloadAsync,
            row => row?.IsGoogleDrive == true && !IsDriveDownloadInProgress,
            ex => SetDriveDownloadStatus("Google Drive download failed (" + ex.GetType().Name + ")."));
        CancelDriveDownloadCommand = new RelayCommand(
            () => _driveDownloadCts?.Cancel(),
            () => IsDriveDownloadInProgress);
    }

    private async Task RunGoogleDriveDownloadAsync(CloudInstanceRowViewModel? instance)
    {
        if (instance is null || !instance.IsGoogleDrive || IsDriveDownloadInProgress)
        {
            return;
        }

        var handler = GoogleDriveDownloadRequested;
        if (handler is null)
        {
            SetDriveDownloadStatus("Google Drive download UI is unavailable.");
            return;
        }

        _driveDownloadCts = new CancellationTokenSource();
        IsDriveDownloadInProgress = true;
        DriveDownloadStatus = "Starting Google Drive file selection...";
        try
        {
            await handler(instance, _driveDownloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            SetDriveDownloadStatus("Google Drive download cancelled.");
        }
        catch (OAuthConsentDeniedException)
        {
            SetDriveDownloadStatus("Google Drive file selection was cancelled.");
        }
        catch (OAuthStateException)
        {
            SetDriveDownloadStatus("Google Drive selection could not be verified. Try again.");
        }
        catch (OAuthCallbackException)
        {
            SetDriveDownloadStatus("Google Drive returned an invalid file-selection response. Try again.");
        }
        catch (GoogleDrivePickerException ex)
        {
            SetDriveDownloadStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetDriveDownloadStatus("Google Drive download failed (" + ex.GetType().Name + ").");
        }
        finally
        {
            IsDriveDownloadInProgress = false;
            _driveDownloadCts.Dispose();
            _driveDownloadCts = null;
        }
    }

    /// <summary>
    /// Raised when a static-credential provider with credential fields
    /// requests the credential form. The payload is the provider profile.
    /// Carries no credential value.
    /// </summary>
    public event EventHandler<ProviderProfile>? ConfigureRequested;

    public event Func<CloudInstanceRowViewModel, CancellationToken, Task>? GoogleDriveDownloadRequested;

    /// <summary>
    /// The provider rows, in the order of the built-in profiles.
    /// </summary>
    public ObservableCollection<CloudProviderRowViewModel> Providers { get; } = new();

    /// <summary>
    /// The filtered provider rows shown in Quick Start. Recomputed
    /// when ProviderFilter changes and after LoadAsync. Matches the
    /// display name and provider id, case-insensitively. No resolver,
    /// no web, no credential store access.
    /// </summary>
    public ObservableCollection<CloudProviderRowViewModel> FilteredProviders { get; } = new();

    /// <summary>
    /// True once the provider list has been loaded at least once. Gates
    /// the navigation trigger, not LoadAsync itself: LoadAsync always runs
    /// so a deliberate refresh after a connection is not blocked.
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Starts a connection or opens the credential form for the row.</summary>
    /// <summary>Starts a connection or opens the credential form for the row.</summary>
    public AsyncRelayCommand<CloudProviderRowViewModel> ConnectCommand { get; }

    /// <summary>Disconnects the selected instance.</summary>
    /// <summary>Disconnects the selected instance.</summary>
    public AsyncRelayCommand<CloudInstanceRowViewModel> DisconnectCommand { get; }

    public AsyncRelayCommand<CloudInstanceRowViewModel> ChooseGoogleDriveFileCommand { get; }
    public ICommand CancelDriveDownloadCommand { get; }

    public bool IsDriveDownloadInProgress
    {
        get => _isDriveDownloadInProgress;
        private set
        {
            if (SetProperty(ref _isDriveDownloadInProgress, value))
            {
                ChooseGoogleDriveFileCommand.RaiseCanExecuteChanged();
                ((RelayCommand)CancelDriveDownloadCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasDriveDownloadStatus));
            }
        }
    }

    public string DriveDownloadStatus
    {
        get => _driveDownloadStatus;
        private set
        {
            if (SetProperty(ref _driveDownloadStatus, value))
            {
                OnPropertyChanged(nameof(HasDriveDownloadStatus));
            }
        }
    }

    public bool HasDriveDownloadStatus =>
        IsDriveDownloadInProgress || !string.IsNullOrEmpty(DriveDownloadStatus);

    public void SetDriveDownloadStatus(string message) =>
        SetDriveDownloadStatusOnUiThread(message);

    private void SetDriveDownloadStatusOnUiThread(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            DriveDownloadStatus = message;
            return;
        }

        Dispatcher.UIThread.Post(
            () => DriveDownloadStatus = message,
            DispatcherPriority.Background);
    }

    private string _connectionMessage = string.Empty;

    /// <summary>
    /// The latest user-facing connection message. Empty when there is
    /// nothing to show. Set by the Connect and Disconnect runners;
    /// cleared by DismissMessageCommand.
    /// </summary>
    public string ConnectionMessage
    {
        get => _connectionMessage;
        private set
        {
            if (SetProperty(ref _connectionMessage, value))
            {
                OnPropertyChanged(nameof(HasConnectionMessage));
            }
        }
    }

    /// <summary>
    /// True when there is a connection message to show. Computed from
    /// ConnectionMessage; notified by the ConnectionMessage setter.
    /// </summary>
    public bool HasConnectionMessage =>
        !string.IsNullOrEmpty(ConnectionMessage);

    /// <summary>Clears the connection message.</summary>
    public ICommand DismissMessageCommand { get; }

    /// <summary>
    /// The Quick Start filter text. Setting it recomputes
    /// FilteredProviders. This is a local list filter, not discovery.
    /// </summary>
    public string ProviderFilter
    {
        get => _providerFilter;
        set
        {
            if (SetProperty(ref _providerFilter, value))
            {
                RecomputeFilter();
            }
        }
    }

    /// <summary>
    /// Builds the provider rows and loads the connected instances for the
    /// OAuth providers. No network call is made for static-credential
    /// providers. Safe to call more than once.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Providers.Clear();

            foreach (var profile in BuiltInProfiles.All)
            {
                var isOAuth = profile.AuthKind == AuthKind.OAuth;
                var row = new CloudProviderRowViewModel(
                    profile: profile,
                    providerId: profile.Id,
                    displayName: profile.DisplayName,
                    iconKey: profile.IconKey,
                    isConnectable: isOAuth,
                    connectCommand: ConnectCommand);

                if (isOAuth)
                {
                    var instances = await _connections
                        .ListConnectedInstancesAsync(profile.Id, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var instance in instances)
                    {
                        row.Instances.Add(new CloudInstanceRowViewModel(
                            providerId: profile.Id,
                            instanceName: instance,
                            disconnectCommand: DisconnectCommand,
                            chooseGoogleDriveFileCommand: ChooseGoogleDriveFileCommand));
                    }
                }

                Providers.Add(row);
            }

            IsLoaded = true;
            RecomputeFilter();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    // ------------------------------------------------------------------
    // Commands. OnConnect routes an OAuth row to the (deferred) OAuth
    // flow, and a static row with credential fields to the
    // ConfigureRequested event that the page handles by opening the
    // credential form.
    // ------------------------------------------------------------------

    private void RecomputeFilter()
    {
        FilteredProviders.Clear();

        var filter = _providerFilter?.Trim() ?? string.Empty;

        foreach (var row in Providers)
        {
            if (filter.Length == 0
                || row.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || row.ProviderId.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredProviders.Add(row);
            }
        }
    }

    private async Task RunConnectAsync(CloudProviderRowViewModel? row)
    {
        if (row is null) return;

        // Static-credential providers still open the credential form.
        // An OAuth provider with a credential field (the client secret)
        // must have its secret stored before the OAuth flow runs. If it
        // is not stored, open the form and return; the next Connect
        // proceeds once the secret is saved.
        if (row.Profile.AuthKind == AuthKind.OAuth
            && row.Profile.CredentialFields.Count > 0)
        {
            if (!await HasClientSecretAsync(row.ProviderId).ConfigureAwait(false))
            {
                ConfigureRequested?.Invoke(this, row.Profile);
                return;
            }
        }

        if (!row.IsConnectable)
        {
            if (row.Profile.CredentialFields.Count > 0)
            {
                ConfigureRequested?.Invoke(this, row.Profile);
            }
            return;
        }

        try
        {
            var config = await _configStore
                .ReadAsync(row.ProviderId, CancellationToken.None)
                .ConfigureAwait(false);
            if (config is null)
            {
                SetMessage("This provider is not registered. Add a client registration first.");
                return;
            }

            var name = (row.InstanceName ?? string.Empty).Trim();

            try
            {
                _ = new CredentialKey(row.ProviderId, name).ToStoreKey();
            }
            catch (InvalidOperationException)
            {
                SetMessage("Enter a valid instance name.");
                return;
            }

            var existing = await _connections
                .ListConnectedInstancesAsync(row.ProviderId, CancellationToken.None)
                .ConfigureAwait(false);
            if (Enumerable.Contains(existing, name, StringComparer.Ordinal))
            {
                SetMessage("An instance with that name already exists.");
                return;
            }

            await _connections
                .ConnectAsync(config, new CredentialKey(row.ProviderId, name), CancellationToken.None)
                .ConfigureAwait(false);

            await LoadAsync(CancellationToken.None).ConfigureAwait(false);
            SetMessage(string.Empty);
        }
        catch (CloudCredentialUnavailableException ex)
        {
            SetMessage(ex.Message);
        }
        catch (OAuthConsentDeniedException)
        {
            SetMessage("Sign-in was cancelled at the provider.");
        }
        catch (OAuthStateException)
        {
            SetMessage("Sign-in failed: the provider callback did not match. Try again.");
        }
        catch (OAuthCallbackException ex)
        {
            SetMessage("Sign-in failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            SetMessage("Sign-in failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private async Task RunDisconnectAsync(CloudInstanceRowViewModel? instance)
    {
        if (instance is null) return;

        try
        {
            var config = await _configStore
                .ReadAsync(instance.ProviderId, CancellationToken.None)
                .ConfigureAwait(false);
            if (config is null)
            {
                SetMessage("This provider is not registered. Add a client registration first.");
                return;
            }

            await _connections
                .DisconnectAsync(config, new CredentialKey(instance.ProviderId, instance.InstanceName), CancellationToken.None)
                .ConfigureAwait(false);

            await LoadAsync(CancellationToken.None).ConfigureAwait(false);
            SetMessage(string.Empty);
        }
        catch (CloudCredentialUnavailableException ex)
        {
            SetMessage(ex.Message);
        }
        catch (Exception)
        {
            SetMessage("Disconnect failed.");
        }
    }

    /// <summary>
    /// True when the reserved provider-level client-secret credential
    /// exists. Uses ListInstanceNamesAsync, which returns names only,
    /// never values.
    /// </summary>
    private async Task<bool> HasClientSecretAsync(string providerId)
    {
        try
        {
            var names = await _credentials
                .ListInstanceNamesAsync(providerId, CancellationToken.None)
                .ConfigureAwait(false);
            return names.Contains(
                OAuthClientConfig.ClientSecretInstanceName, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SetMessage(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ConnectionMessage = message;
            return;
        }

        // Post without awaiting: a non-UI host may have no dispatcher pump.
        Dispatcher.UIThread.Post(() => ConnectionMessage = message, DispatcherPriority.Background);
    }
    /// <summary>
    /// Disposes the load semaphore.
    /// </summary>
    public void Dispose()
    {
        _driveDownloadCts?.Cancel();
        _loadLock.Dispose();
    }

}
