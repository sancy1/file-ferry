using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using FileHarvester.App.Services;
using FileHarvester.Platform.Cloud.Credentials;
using Microsoft.Extensions.DependencyInjection;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Metadata;
using SchemaLence.Core.Providers;
using SchemaLence.Core.Storage;
using SchemaLence.Platform.Postgres;

namespace FileHarvester.App.ViewModels;

public sealed class SchemaLenceViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ISchemaLenceLocalStore _localStore;
    private readonly ISchemaLenceCredentialStore _credentialStore;
    private readonly IDatabaseProvider _provider;
    private readonly IPostgresBackupService _backupService;
    private readonly Dictionary<string, string> _sessionPasswords = new(StringComparer.Ordinal);
    private int _operationRunning;
    private bool _settingNamespaceFromRefresh;

    private string _connectionName = string.Empty;
    private bool _useConnectionUri;
    private string _connectionUri = string.Empty;
    private string _host = string.Empty;
    private string _port = "5432";
    private string _database = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _sslMode = "Require";
    private bool _rememberPassword;
    private string _statusText = "Connect to a PostgreSQL database to browse accessible schemas.";
    private string _searchText = string.Empty;
    private string? _selectedNamespace;
    private DatabaseConnectionProfile? _selectedConnection;
    private DatabaseConnectionProfile? _activeConnection;
    private string? _activePassword;
    private SchemaLenceObjectRowViewModel? _selectedObjectRow;
    private DatabaseObjectStructure? _structure;
    private DatabasePage? _page;
    private long? _exactRowCount;
    private bool _showData = true;
    private bool _isBusy;
    private bool _isBackupRunning;
    private bool _isExcelExportRunning;

    public SchemaLenceViewModel(
        ISchemaLenceLocalStore localStore,
        ISchemaLenceCredentialStore credentialStore,
        IEnumerable<IDatabaseProvider> providers,
        IPostgresBackupService? backupService = null)
    {
        _localStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        ArgumentNullException.ThrowIfNull(providers);
        _provider = providers.FirstOrDefault(provider => provider.ProviderId == "postgresql")
            ?? throw new InvalidOperationException("The PostgreSQL provider is not registered.");
        _backupService = backupService ?? new SchemaLence.Platform.Postgres.PostgresBackupService();

        Connections = new ObservableCollection<DatabaseConnectionProfile>();
        Namespaces = new ObservableCollection<string>();
        Objects = new ObservableCollection<SchemaLenceObjectRowViewModel>();
        VisibleObjects = new ObservableCollection<SchemaLenceObjectRowViewModel>();
        Rows = new ObservableCollection<SchemaLenceDataRowViewModel>();

        InitializeCommand = CreateAsyncCommand(InitializeAsync);
        TestConnectionCommand = CreateAsyncCommand(TestConnectionAsync);
        SaveAndConnectCommand = CreateAsyncCommand(SaveAndConnectAsync);
        ConnectSelectedCommand = CreateAsyncCommand(ConnectSelectedAsync);
        DeleteSelectedConnectionCommand = CreateAsyncCommand(DeleteSelectedConnectionAsync);
        AddConnectionCommand = new RelayCommand(AddConnection);
        ShowAllObjectsCommand = new AsyncRelayCommand<object>(
            _ => ShowAllObjectsAsync(),
            onUnexpectedFailure: ReportUnexpectedFailure);
        ShowDataCommand = new RelayCommand(() => ShowData = true);
        ShowStructureCommand = new RelayCommand(() => ShowData = false);
        DisconnectCommand = CreateAsyncCommand(DisconnectAsync);
        RefreshCommand = CreateAsyncCommand(RefreshAsync);
        PreviousPageCommand = new AsyncRelayCommand<object>(
            _ => LoadPageAsync(PageIndex - 1),
            _ => IsConnected && PageIndex > 0,
            ReportUnexpectedFailure);
        NextPageCommand = new AsyncRelayCommand<object>(
            _ => LoadPageAsync(PageIndex + 1),
            _ => IsConnected && Rows.Count == PageSize,
            ReportUnexpectedFailure);
        CopyReportCommand = new RelayCommand(
            () => CopyReportRequested?.Invoke(this, BuildStructureReport()),
            () => Structure is not null);
        CountRowsCommand = CreateAsyncCommand(CountRowsAsync);
        ExportExcelCommand = new RelayCommand(
            () => ExportExcelRequested?.Invoke(this, EventArgs.Empty),
            () => IsConnected && Structure is not null);
        BackupDatabaseCommand = new RelayCommand(
            () => BackupDatabaseRequested?.Invoke(this, EventArgs.Empty),
            () => IsConnected);
        CancelBackupCommand = new RelayCommand(
            () => CancelBackupRequested?.Invoke(this, EventArgs.Empty),
            () => IsBackupRunning);
        CancelExcelExportCommand = new RelayCommand(
            () => CancelExcelExportRequested?.Invoke(this, EventArgs.Empty),
            () => IsExcelExportRunning);
        SaveReportCommand = new RelayCommand(
            () => SaveReportRequested?.Invoke(this, BuildStructureReport()),
            () => Structure is not null);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler<string>? CopyReportRequested;
    public event EventHandler<string>? SaveReportRequested;
    public event EventHandler? ExportExcelRequested;
    public event EventHandler? BackupDatabaseRequested;
    public event EventHandler? CancelBackupRequested;
    public event EventHandler? CancelExcelExportRequested;
    public event EventHandler? CloseRequested;

    public ObservableCollection<DatabaseConnectionProfile> Connections { get; }
    public ObservableCollection<string> Namespaces { get; }
    public ObservableCollection<SchemaLenceObjectRowViewModel> Objects { get; }
    public ObservableCollection<SchemaLenceObjectRowViewModel> VisibleObjects { get; }
    public ObservableCollection<SchemaLenceDataRowViewModel> Rows { get; }

    public ICommand InitializeCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand SaveAndConnectCommand { get; }
    public ICommand ConnectSelectedCommand { get; }
    public ICommand DeleteSelectedConnectionCommand { get; }
    public ICommand AddConnectionCommand { get; }
    public ICommand ShowAllObjectsCommand { get; }
    public ICommand ShowDataCommand { get; }
    public ICommand ShowStructureCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand CopyReportCommand { get; }
    public ICommand CountRowsCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand CancelBackupCommand { get; }
    public ICommand CancelExcelExportCommand { get; }
    public ICommand SaveReportCommand { get; }
    public ICommand CloseCommand { get; }

    public string ConnectionName
    {
        get => _connectionName;
        set => SetProperty(ref _connectionName, value);
    }
    public bool UseConnectionUri
    {
        get => _useConnectionUri;
        set => SetProperty(ref _useConnectionUri, value);
    }
    public string ConnectionUri
    {
        get => _connectionUri;
        set => SetProperty(ref _connectionUri, value);
    }
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }
    public string Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }
    public string Database
    {
        get => _database;
        set => SetProperty(ref _database, value);
    }
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }
    public string SslMode
    {
        get => _sslMode;
        set => SetProperty(ref _sslMode, value);
    }
    public bool RememberPassword
    {
        get => _rememberPassword;
        set => SetProperty(ref _rememberPassword, value);
    }
    public IReadOnlyList<string> SslModes { get; } =
        new[] { "Require", "VerifyFull", "VerifyCA", "Prefer", "Disable" };
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                UpdateVisibleObjects();
            }
        }
    }
    public string? SelectedNamespace
    {
        get => _selectedNamespace;
        set
        {
            if (SetProperty(ref _selectedNamespace, value)
                && IsConnected
                && !_settingNamespaceFromRefresh)
            {
                _ = LoadObjectsSafelyAsync();
            }
        }
    }
    public DatabaseConnectionProfile? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (SetProperty(ref _selectedConnection, value) && value is not null)
            {
                LoadConnectionFields(value);
            }
        }
    }
    public DatabaseConnectionProfile? ActiveConnection
    {
        get => _activeConnection;
        private set
        {
            if (SetProperty(ref _activeConnection, value))
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ActiveConnectionLabel));
                RaiseCommandStates();
                ((RelayCommand)BackupDatabaseCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ExportExcelCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public string ActiveConnectionLabel => ActiveConnection is null
        ? "Not connected"
        : $"{ActiveConnection.Name} — {ActiveConnection.Database}@{ActiveConnection.Host}";
    public bool IsConnected => ActiveConnection is not null;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }
    public bool IsBackupRunning
    {
        get => _isBackupRunning;
        private set
        {
            if (SetProperty(ref _isBackupRunning, value))
            {
                ((RelayCommand)CancelBackupCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public void SetBackupRunning(bool isRunning) => IsBackupRunning = isRunning;
    public bool IsExcelExportRunning
    {
        get => _isExcelExportRunning;
        private set
        {
            if (SetProperty(ref _isExcelExportRunning, value))
            {
                ((RelayCommand)CancelExcelExportCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public void SetExcelExportRunning(bool isRunning) => IsExcelExportRunning = isRunning;
    public DatabaseObjectStructure? Structure
    {
        get => _structure;
        private set
        {
            if (SetProperty(ref _structure, value))
            {
                OnPropertyChanged(nameof(HasStructure));
                OnPropertyChanged(nameof(StructureReport));
                ((RelayCommand)CopyReportCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveReportCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ExportExcelCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public DatabasePage? Page
    {
        get => _page;
        private set
        {
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(PageStatus));
                OnPropertyChanged(nameof(IsDataPreviewUnavailable));
                OnPropertyChanged(nameof(StructureReport));
                Rows.Clear();
                if (value is not null)
                {
                    foreach (var row in value.Rows)
                    {
                        Rows.Add(new SchemaLenceDataRowViewModel(
                            row.Select(cell => cell is null ? "(NULL)" : cell).ToArray()));
                    }
                }
                RaiseCommandStates();
            }
        }
    }
    public SchemaLenceObjectRowViewModel? SelectedObjectRow
    {
        get => _selectedObjectRow;
        set
        {
            if (SetProperty(ref _selectedObjectRow, value) && value is not null)
            {
                _ = SelectObjectSafelyAsync(value.DatabaseObject);
            }
        }
    }
    public bool ShowData
    {
        get => _showData;
        set
        {
            if (SetProperty(ref _showData, value))
            {
                OnPropertyChanged(nameof(ShowStructure));
                OnPropertyChanged(nameof(IsDataPreviewUnavailable));
            }
        }
    }
    public bool ShowStructure
    {
        get => !ShowData;
        set => ShowData = !value;
    }
    public int PageIndex => Page?.PageIndex ?? 0;
    public int PageSize { get; private set; } = 100;
    public string PageStatus => Page is null
        ? Structure is null
            ? "Select an object to preview data."
            : "Data preview unavailable. See the status message for details."
        : $"Page {Page.PageIndex + 1} · {Rows.Count} rows"
            + (Rows.Count == 0
                ? " visible to this role; check row-level security and table contents."
                : string.Empty)
            + (Page.EstimatedTotalRows is { } rows ? $" · estimated total ~{rows:N0}" : string.Empty);
    public string StructureReport => BuildStructureReport();
    public long? ExactRowCount
    {
        get => _exactRowCount;
        private set
        {
            if (SetProperty(ref _exactRowCount, value))
            {
                OnPropertyChanged(nameof(ExactRowCountText));
                OnPropertyChanged(nameof(StructureReport));
            }
        }
    }
    public string ExactRowCountText => ExactRowCount?.ToString(
        "N0", System.Globalization.CultureInfo.InvariantCulture) ?? "Not counted";
    public bool IsDataPreviewUnavailable =>
        ShowData && Structure is not null && Page is null;
    public bool HasStructure => Structure is not null;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var saved = await _localStore.GetConnectionsAsync().ConfigureAwait(true);
            Replace(Connections, saved);
            StatusText = Connections.Count == 0
                ? "Add a PostgreSQL database connection to begin."
                : $"Loaded {Connections.Count} saved connection(s). Select one to connect.";
        }
        catch
        {
            StatusText = "Could not load saved database connection settings from local storage.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetStatus(string message) => StatusText = message;

    public async Task TestConnectionAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }
        IsBusy = true;
        try
        {
            var input = BuildInput();
            var result = await _provider.TestConnectionAsync(input).ConfigureAwait(true);
            StatusText = result.Succeeded
                ? $"{result.Message} {result.ServerDescription}; database {result.DatabaseName}."
                : result.Message;
        }
        catch (FormatException ex)
        {
            StatusText = ex.Message;
        }
        catch (ArgumentException ex)
        {
            StatusText = ex.Message;
        }
        catch
        {
            StatusText = "Connection test failed. Check the connection settings and try again.";
        }
        finally
        {
            IsBusy = false;
            EndOperation();
        }
    }

    public async Task SaveAndConnectAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }
        IsBusy = true;
        try
        {
            var input = BuildInput();
            var profile = PostgresConnectionParser.ToProfile(
                Guid.NewGuid().ToString("N"),
                input);
            await _localStore.SaveConnectionAsync(profile).ConfigureAwait(true);

            _sessionPasswords[profile.Id] = input.Password;
            string? credentialMessage = null;
            if (RememberPassword)
            {
                try
                {
                    await _credentialStore.SavePasswordAsync(profile.Id, input.Password)
                        .ConfigureAwait(true);
                }
                catch
                {
                    credentialMessage = " Secure password storage is unavailable; password was not remembered.";
                }
            }

            await ReloadConnectionsAsync().ConfigureAwait(true);
            SelectedConnection = Connections.FirstOrDefault(item => item.Id == profile.Id);
            await ConnectAsync(profile, input.Password).ConfigureAwait(true);
            if (credentialMessage is not null)
            {
                StatusText += credentialMessage;
            }
        }
        catch (FormatException ex)
        {
            StatusText = ex.Message;
        }
        catch (ArgumentException ex)
        {
            StatusText = ex.Message;
        }
        catch
        {
            StatusText = "Could not save or connect to this database. Check the settings and local permissions.";
        }
        finally
        {
            IsBusy = false;
            EndOperation();
        }
    }

    public async Task ConnectSelectedAsync()
    {
        if (SelectedConnection is null)
        {
            StatusText = "Select a saved connection first.";
            return;
        }

        if (!TryBeginOperation())
        {
            return;
        }
        IsBusy = true;
        try
        {
            var password = Password;
            if (password.Length == 0 && _sessionPasswords.TryGetValue(SelectedConnection.Id, out var cached))
            {
                password = cached;
            }
            if (password.Length == 0)
            {
                try
                {
                    password = await _credentialStore.GetPasswordAsync(SelectedConnection.Id)
                        .ConfigureAwait(true) ?? string.Empty;
                }
                catch
                {
                    StatusText = "Secure password storage is unavailable on this platform. Enter the password to connect.";
                    return;
                }
            }
            if (password.Length == 0)
            {
                StatusText = "Enter the password for this connection, then connect.";
                return;
            }

            await ConnectAsync(SelectedConnection, password).ConfigureAwait(true);
        }
        catch
        {
            StatusText = "Could not connect to the saved database. Check the password and connection settings.";
        }
        finally
        {
            IsBusy = false;
            EndOperation();
        }
    }

    public async Task DeleteSelectedConnectionAsync()
    {
        if (SelectedConnection is null)
        {
            StatusText = "Select a saved connection to remove.";
            return;
        }

        var profile = SelectedConnection;
        var credentialCleanupFailed = false;
        try
        {
            await _localStore.DeleteConnectionAsync(profile.Id).ConfigureAwait(true);
            _sessionPasswords.Remove(profile.Id);
            try
            {
                await _credentialStore.DeletePasswordAsync(profile.Id).ConfigureAwait(true);
            }
            catch
            {
                credentialCleanupFailed = true;
            }
            if (ActiveConnection?.Id == profile.Id)
            {
                await DisconnectAsync().ConfigureAwait(true);
            }
            await ReloadConnectionsAsync().ConfigureAwait(true);
            StatusText = credentialCleanupFailed
                ? "Connection removed from local settings; secure credential cleanup could not be verified."
                : "Saved connection removed.";
        }
        catch
        {
            StatusText = "Could not remove the saved connection.";
        }
    }

    public async Task DisconnectAsync()
    {
        ActiveConnection = null;
        _activePassword = null;
        Namespaces.Clear();
        Objects.Clear();
        VisibleObjects.Clear();
        Rows.Clear();
        Structure = null;
        Page = null;
        SelectedNamespace = null;
        SelectedObjectRow = null;
        StatusText = "Disconnected. Saved connection settings remain available.";
        await Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (ActiveConnection is null || _activePassword is null)
        {
            StatusText = "Connect to a database before refreshing.";
            return;
        }

        IsBusy = true;
        try
        {
            _settingNamespaceFromRefresh = true;
            try
            {
                Namespaces.Clear();
                Objects.Clear();
                VisibleObjects.Clear();
                SelectedObjectRow = null;
                Structure = null;
                Page = null;
                ExactRowCount = null;

                var namespaces = await _provider.ListNamespacesAsync(
                    ActiveConnection, _activePassword).ConfigureAwait(true);
                Namespaces.Add("All accessible schemas");
                foreach (var item in namespaces)
                {
                    Namespaces.Add(item.Name);
                }
                SelectedNamespace = "All accessible schemas";
            }
            finally
            {
                _settingNamespaceFromRefresh = false;
            }

            if (await LoadObjectsSafelyAsync().ConfigureAwait(true))
            {
                StatusText = Objects.Count == 0
                    ? "Connected, but no accessible tables or views were found in this database."
                    : $"Refreshed {Objects.Count} accessible database object(s) at {DateTimeOffset.Now:t}.";
            }
        }
        catch (Exception exception)
        {
            StatusText = exception is DatabaseProviderException providerException
                ? providerException.Message
                : "Schema refresh failed (" + exception.GetType().Name
                    + "). Check database connectivity and schema permissions.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadPageAsync(int pageIndex)
    {
        if (ActiveConnection is null || _activePassword is null || Structure is null)
        {
            return;
        }
        if (pageIndex < 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Page = await _provider.GetPageAsync(
                ActiveConnection,
                _activePassword,
                Structure.DatabaseItem.Namespace,
                Structure.DatabaseItem.Name,
                pageIndex,
                PageSize,
                null,
                false).ConfigureAwait(true);
            StatusText = Page.Rows.Count == 0
                ? "The selected object is readable but contains no rows on this page."
                : $"Loaded {Rows.Count} row(s) from {Structure.DatabaseItem.Namespace}.{Structure.DatabaseItem.Name}.";
        }
        catch (DatabaseProviderException exception)
        {
            Page = null;
            StatusText = exception.Message;
        }
        catch (Exception exception)
        {
            Page = null;
            StatusText = "Could not load this data page (" + exception.GetType().Name
                + "). Check SELECT permissions and connectivity.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CountRowsAsync()
    {
        if (ActiveConnection is null || _activePassword is null || Structure is null)
        {
            StatusText = "Select an object before requesting an exact row count.";
            return;
        }

        IsBusy = true;
        try
        {
            ExactRowCount = await _provider.CountRowsAsync(
                ActiveConnection,
                _activePassword,
                Structure.DatabaseItem.Namespace,
                Structure.DatabaseItem.Name).ConfigureAwait(true);
            StatusText = $"Exact row count: {ExactRowCountText}.";
        }
        catch (DatabaseProviderException exception)
        {
            StatusText = exception.Message;
        }
        catch (Exception exception)
        {
            StatusText = "Could not count rows (" + exception.GetType().Name + ").";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAsync(DatabaseConnectionProfile profile, string password)
    {
        var test = await _provider.TestConnectionAsync(
            new DatabaseConnectionInput(
                profile.Name,
                profile.Host,
                profile.Port,
                profile.Database,
                profile.Username,
                password,
                profile.SslMode)).ConfigureAwait(true);
        if (!test.Succeeded)
        {
            StatusText = test.Message;
            return;
        }

        ActiveConnection = profile;
        _activePassword = password;
        _sessionPasswords[profile.Id] = password;
        Password = string.Empty;
        await RefreshAsync().ConfigureAwait(true);
        StatusText = $"Connected to {profile.Name} ({profile.Database}). {StatusText}";
    }

    private async Task ReloadConnectionsAsync()
    {
        var saved = await _localStore.GetConnectionsAsync().ConfigureAwait(true);
        Replace(Connections, saved);
    }

    private void AddConnection()
    {
        SelectedConnection = null;
        ConnectionName = string.Empty;
        ConnectionUri = string.Empty;
        Host = string.Empty;
        Port = "5432";
        Database = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        SslMode = "Require";
        RememberPassword = false;
        StatusText = "Enter settings for another PostgreSQL database.";
    }

    private DatabaseConnectionInput BuildInput()
    {
        if (UseConnectionUri)
        {
            var parsed = PostgresConnectionParser.ParseUri(ConnectionName, ConnectionUri);
            var password = parsed.Password ?? Password;
            return parsed.Input with { Password = password };
        }

        if (!int.TryParse(Port, out var port))
        {
            throw new ArgumentException("Enter a valid TCP port number.");
        }
        return new DatabaseConnectionInput(
            ConnectionName,
            Host,
            port,
            Database,
            Username,
            Password,
            SslMode);
    }

    private void LoadConnectionFields(DatabaseConnectionProfile profile)
    {
        ConnectionName = profile.Name;
        UseConnectionUri = false;
        ConnectionUri = string.Empty;
        Host = profile.Host;
        Port = profile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Database = profile.Database;
        Username = profile.Username;
        Password = string.Empty;
        SslMode = profile.SslMode;
        RememberPassword = false;
    }

    private async Task<bool> LoadObjectsSafelyAsync()
    {
        try
        {
            await LoadObjectsAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception)
        {
            StatusText = exception is DatabaseProviderException providerException
                ? providerException.Message
                : "Could not list accessible database objects ("
                    + exception.GetType().Name + ").";
            return false;
        }
    }

    private async Task LoadObjectsAsync()
    {
        if (ActiveConnection is null || _activePassword is null)
        {
            return;
        }

        var filter = SelectedNamespace is null or "All accessible schemas"
            ? null
            : SelectedNamespace;
        var items = await _provider.ListObjectsAsync(
            ActiveConnection, _activePassword, filter).ConfigureAwait(true);
        Objects.Clear();
        foreach (var item in items)
        {
            Objects.Add(new SchemaLenceObjectRowViewModel(item, SelectObjectSafelyAsync));
        }
        UpdateVisibleObjects();
    }

    private async Task ShowAllObjectsAsync()
    {
        if (!IsConnected)
        {
            StatusText = "Connect to a database before listing objects.";
            return;
        }
        _settingNamespaceFromRefresh = true;
        try
        {
            SelectedNamespace = "All accessible schemas";
        }
        finally
        {
            _settingNamespaceFromRefresh = false;
        }
        if (await LoadObjectsSafelyAsync().ConfigureAwait(true))
        {
            StatusText = Objects.Count == 0
                ? "No accessible tables or views were found across the visible schemas."
                : $"Showing {Objects.Count} accessible objects across schemas.";
        }
    }

    private async Task SelectObjectSafelyAsync(DatabaseObject databaseObject)
    {
        try
        {
            await SelectObjectAsync(databaseObject).ConfigureAwait(true);
        }
        catch
        {
            StatusText = "Could not inspect the selected object. Check metadata and SELECT permissions.";
        }
    }

    private async Task SelectObjectAsync(DatabaseObject databaseObject)
    {
        if (ActiveConnection is null || _activePassword is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = $"Loading {databaseObject.Namespace}.{databaseObject.Name} structure and data…";
            Structure = null;
            Page = null;
            ExactRowCount = null;
            Structure = await _provider.GetStructureAsync(
                ActiveConnection,
                _activePassword,
                databaseObject.Namespace,
                databaseObject.Name).ConfigureAwait(true);
            ShowData = true;
            await LoadPageAsync(0).ConfigureAwait(true);
            if (Page is not null)
            {
                StatusText = $"Selected {databaseObject.Namespace}.{databaseObject.Name}. "
                    + (databaseObject.CanReadData
                        ? Rows.Count == 0
                            ? "The query returned no rows visible to this role. Check table contents and row-level security."
                            : $"Loaded {Rows.Count} data row(s). Visibility reflects the connected role's permissions."
                        : "The connected role does not have SELECT permission.");
            }
        }
        catch (DatabaseProviderException exception)
        {
            Structure = null;
            Page = null;
            StatusText = exception.Message;
        }
        catch (Exception exception)
        {
            Structure = null;
            Page = null;
            StatusText = "Could not read object metadata ("
                + exception.GetType().Name
                + "). Check permissions and connectivity.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateVisibleObjects()
    {
        var filter = SearchText.Trim();
        var matches = Objects.Where(item => filter.Length == 0
            || item.QualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || item.KindLabel.Contains(filter, StringComparison.OrdinalIgnoreCase));
        Replace(VisibleObjects, matches);
    }

    internal SchemaLenceExportRequest CaptureExcelExportRequest()
    {
        if (ActiveConnection is null || _activePassword is null || Structure is null)
        {
            throw new InvalidOperationException("Select a connected table or view before exporting.");
        }

        return new SchemaLenceExportRequest(
            ActiveConnection,
            _activePassword,
            Structure.DatabaseItem.Namespace,
            Structure.DatabaseItem.Name);
    }

    internal Task<long> ExportSelectedObjectToExcelAsync(
        SchemaLenceExportRequest request,
        string destinationPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        SchemaLenceExcelExporter.ExportAsync(
            _provider,
            request.Profile,
            request.Password,
            request.NamespaceName,
            request.ObjectName,
            destinationPath,
            progress,
            cancellationToken);

    internal SchemaLenceBackupRequest CaptureBackupRequest()
    {
        if (ActiveConnection is null || _activePassword is null)
        {
            throw new InvalidOperationException("Connect to a database before creating a backup.");
        }

        return new SchemaLenceBackupRequest(ActiveConnection, _activePassword);
    }

    internal Task CreateDatabaseBackupAsync(
        SchemaLenceBackupRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        _backupService.CreateCustomArchiveAsync(
            request.Profile,
            request.Password,
            destinationPath,
            cancellationToken);

    internal async Task RecordBackupAsync(
        SchemaLenceBackupRequest request,
        string destinationPath)
    {
        var info = new FileInfo(destinationPath);
        await _localStore.AddBackupHistoryAsync(new BackupHistoryEntry(
            Guid.NewGuid().ToString("N"),
            request.Profile.Name,
            request.Profile.Database,
            "Full database: schema and data visible to the connected role",
            "PostgreSQL custom archive (.dump)",
            Path.GetFullPath(destinationPath),
            info.Length,
            DateTimeOffset.UtcNow)).ConfigureAwait(true);
    }

    private string BuildStructureReport()
    {
        if (Structure is null)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        result.AppendLine(FormattableString.Invariant(
            $"# {Structure.DatabaseItem.Namespace}.{Structure.DatabaseItem.Name}"));
        result.AppendLine();
        result.AppendLine(FormattableString.Invariant($"Object type: {Structure.DatabaseItem.Kind}"));
        result.AppendLine(FormattableString.Invariant(
            $"Description: {Structure.DatabaseItem.Description ?? "No description available."}"));
        result.AppendLine(FormattableString.Invariant(
            $"Estimated rows: {(Structure.DatabaseItem.EstimatedRows is { } rows ? "~" + rows.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable")}"));
        result.AppendLine(FormattableString.Invariant(
            $"Size: {Structure.DatabaseItem.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable"} bytes"));
        result.AppendLine(FormattableString.Invariant(
            $"Row-level security enabled: {Structure.RowLevelSecurityEnabled}"));
        result.AppendLine("Exact row count visible to this role: " + ExactRowCountText);
        result.AppendLine();
        result.AppendLine("## Columns");
        foreach (var column in Structure.Columns)
        {
            result.AppendLine(FormattableString.Invariant(
                $"- {column.Name}: {column.DataType}; nullable={column.IsNullable}; default={column.DefaultExpression ?? "none"}; identity={column.IdentityKind ?? "none"}; generated={column.GeneratedKind ?? "none"}; description={column.Description ?? "none"}"));
        }
        var allowedValues = Structure.Columns
            .Where(column => column.AllowedValues is { Count: > 0 })
            .Select(column => $"- {column.Name}: {JsonSerializer.Serialize(column.AllowedValues)}")
            .Concat(Structure.Constraints
                .Where(item => item.Kind.Equals("CHECK", StringComparison.OrdinalIgnoreCase))
                .Select(item => $"- {item.Name}: {item.Definition}"))
            .ToArray();
        result.AppendLine();
        result.AppendLine("## Allowed values");
        if (allowedValues.Length == 0)
        {
            result.AppendLine("No CHECK constraints or enum values are defined for this object.");
        }
        else
        {
            foreach (var allowedValue in allowedValues)
            {
                result.AppendLine(allowedValue);
            }
        }
        result.AppendLine();
        result.AppendLine("## Example row");
        if (Page is { Rows.Count: > 0 } samplePage)
        {
            var sample = samplePage.Columns
                .Select((name, index) => new KeyValuePair<string, string?>(
                    name,
                    index < samplePage.Rows[0].Count ? samplePage.Rows[0][index] : null))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            result.AppendLine(JsonSerializer.Serialize(sample, IndentedJsonOptions));
        }
        else
        {
            result.AppendLine("No readable example row is available. Check SELECT permissions.");
        }
        AppendReportSection(result, "Constraints and relationships", Structure.Constraints.Select(item =>
            $"- {item.Kind} {item.Name}: {item.Definition}"
            + (item.ReferencedObject is null ? string.Empty : $" -> {item.ReferencedObject}")));
        AppendReportSection(result, "Indexes", Structure.Indexes.Select(item =>
            $"- {item.Name} (unique={item.IsUnique}): {item.Definition}"));
        AppendReportSection(result, "Triggers", Structure.Triggers.Select(item =>
            $"- {item.Name} ({item.Timing}; {item.Events}; enabled={item.IsEnabled}): {item.Definition}"));
        AppendReportSection(result, "Vector columns", Structure.VectorColumns.Select(item =>
            $"- {item.ColumnName}: {item.TypeName}; dimensions={item.Dimensions?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "not exposed"}"));
        result.AppendLine();
        result.AppendLine(Structure.MetadataNote);
        if (Structure.Definition is not null)
        {
            result.AppendLine();
            result.AppendLine("## Definition");
            result.AppendLine("```sql");
            result.AppendLine(Structure.Definition);
            result.AppendLine("```");
        }
        return result.ToString();
    }

    private static void AppendReportSection(
        StringBuilder builder,
        string title,
        IEnumerable<string> lines)
    {
        builder.AppendLine();
        builder.AppendLine("## " + title);
        var list = lines.ToArray();
        if (list.Length == 0)
        {
            builder.AppendLine("No visible entries.");
            return;
        }
        foreach (var line in list)
        {
            builder.AppendLine(line);
        }
    }

    private void ReportUnexpectedFailure(Exception exception) =>
        StatusText = "Database action failed (" + exception.GetType().Name
            + "). Check connectivity and permissions.";

    private void RaiseCommandStates()
    {
        ((AsyncRelayCommand<object>)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand<object>)PreviousPageCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand<object>)NextPageCommand).RaiseCanExecuteChanged();
    }

    private AsyncRelayCommand<object> CreateAsyncCommand(Func<Task> execute) =>
        new(_ => execute(), onUnexpectedFailure: ReportUnexpectedFailure);

    private bool TryBeginOperation()
    {
        if (Interlocked.CompareExchange(ref _operationRunning, 1, 0) != 0)
        {
            StatusText = "Another database operation is already running.";
            return false;
        }
        return true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        Volatile.Write(ref _operationRunning, 0);
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

internal sealed record SchemaLenceExportRequest(
    DatabaseConnectionProfile Profile,
    string Password,
    string NamespaceName,
    string ObjectName);

internal sealed record SchemaLenceBackupRequest(
    DatabaseConnectionProfile Profile,
    string Password);

public sealed class SchemaLenceDataRowViewModel
{
    public SchemaLenceDataRowViewModel(IReadOnlyList<string?> cells)
    {
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));
    }

    public IReadOnlyList<string?> Cells { get; }
    public string? this[int index] => index >= 0 && index < Cells.Count
        ? Cells[index]
        : null;
}
