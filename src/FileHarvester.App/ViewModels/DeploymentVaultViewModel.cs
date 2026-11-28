// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/DeploymentVaultViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (Deployment Vault registry)
// purpose:  Sub-ViewModel for the Deployment Vault workspace. Owns the
//           application list, the selected application, its records, and
//           the create/update/delete commands for both.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : DeploymentVault.Core.Abstractions.IDeploymentVaultStore,
//                DeploymentVault.Core.Domain.* (Application, InfrastructureRecord,
//                VaultQuery, ApplicationNamePolicy, ...),
//                ApplicationRowViewModel, InfrastructureRecordRowViewModel
//   Used by    : MainWindowViewModel.DeploymentVault; DeploymentVaultPage.axaml
//   See also   : ConversionViewModel (the sibling sub-VM pattern this mirrors)
//
// NOTES
//   - Avalonia-free. File pickers and the delete-confirm are injected
//     delegates so this VM is unit-testable without an Avalonia harness.
//   - State machine: Empty -> ApplicationsLoaded -> ApplicationSelected
//     -> RecordsLoaded -> RecordSelected. Boolean state properties surface
//     the current state to XAML; no enum state machine.
//   - Every store call is awaited with the page's cancellation token.
//     The VM catches and surfaces errors to StatusText; it does not throw
//     past its own boundary, so a failed load does not crash the app.
//   - `SelectedApplication` setter fires the record load. It is deliberate:
//     selecting an app is the trigger for its records. `SelectedRecord` does
//     not trigger anything (it is a leaf selection).
//   - The confirm-delete delegate: `Func<string, string, Task<bool>>` —
//     first argument is a title, second is the message. The delegate is
//     provided by MainWindowViewModel and shows a dialog.
// ============================================================================

using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DeploymentVault.Core.Abstractions;
using DeploymentVault.Core.Domain;
using DeploymentVault.Platform.Export;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// Which of the three Deployment Vault views is currently shown.
/// </summary>
public enum VaultView
{
    /// <summary>The application list (master view).</summary>
    Applications,

    /// <summary>One application's detail form plus its record table.</summary>
    ApplicationDetail,

    /// <summary>One infrastructure record's detail form.</summary>
    RecordDetail,
}

/// <summary>
/// The Deployment Vault workspace view model.
/// </summary>
public sealed class DeploymentVaultViewModel : ViewModelBase
{
    private readonly IDeploymentVaultStore _store;
    private readonly Func<string, string, Task<bool>> _confirmDelete;
    private readonly Func<string, string, string, Task<string?>>? _saveFile;
    private readonly IVaultExporter? _vaultExporter;

    private ApplicationRowViewModel? _selectedApplication;
    private InfrastructureRecordRowViewModel? _selectedRecord;
    private bool _isLoading;
    private bool _isSaving;
    private string _statusText = "Ready.";
    private string _searchText = string.Empty;
    private string _editName = string.Empty;
    private string _editDescription = string.Empty;
    private string _editOwner = string.Empty;
    private string _editRepositoryUrl = string.Empty;
    private string _editApplicationUrl = string.Empty;
    private string _editTags = string.Empty;
    private string _editNotes = string.Empty;
    private RecordStatus _editStatus = RecordStatus.Active;
    private string _editRecordName = string.Empty;
    private string _editRecordProvider = string.Empty;
    private ServiceCategory _editRecordCategory = ServiceCategory.Other;
    private EnvironmentKind _editRecordEnvironment = EnvironmentKind.Other;
    private string _editRecordDashboardUrl = string.Empty;
    private string _editRecordLoginEmail = string.Empty;
    private string _editRecordRegion = string.Empty;
    private string _editRecordResourceId = string.Empty;
    private string _editRecordNotes = string.Empty;
    private string _editRecordTags = string.Empty;
    private RecordStatus _editRecordStatus = RecordStatus.Active;
    private bool _includeDeleted;
    private VaultView _currentView = VaultView.Applications;
    private string _exportRecordFormat = ".md";
    private bool _isEnvSafeActive;
    private bool _isKeyForgeActive;
    private bool _isKeyMasterActive;
    private readonly EnvSafeViewModel? _envSafe;
    private readonly KeyForgeViewModel? _keyForge;
    private readonly KeyMasterViewModel? _keyMaster;

    /// <summary>Builds the sub-ViewModel.</summary>
    /// <param name="store">The persistence store. Not null.</param>
    /// <param name="confirmDelete">
    /// Confirmation delegate for permanent-delete actions. (title, message)
    /// -> true to proceed, false to cancel. Not null.
    /// </param>
    /// <param name="saveFile">
    /// Export delegate: (title, suggestedFileName, extension) -> local path
    /// or null when the user cancels. Optional; without it Export reports
    /// unavailable.
    /// </param>
    /// <param name="vaultExporter">
    /// The vault export orchestrator. Optional; without it record export
    /// reports unavailable.
    /// </param>
    /// <param name="envSafe">
    /// The EnvSafe sub-app view model. Optional; without it the EnvSafe rail
    /// button reports unavailable.
    /// </param>
    /// <param name="keyForge">
    /// The KeyForge sub-app view model. Optional; without it the KeyForge rail
    /// button reports unavailable.
    /// </param>
    /// <param name="keyMaster">
    /// The KeyMaster sub-app view model. Optional; without it the KeyMaster
    /// rail button reports unavailable.
    /// </param>
    public DeploymentVaultViewModel(
        IDeploymentVaultStore store,
        Func<string, string, Task<bool>> confirmDelete,
        Func<string, string, string, Task<string?>>? saveFile = null,
        IVaultExporter? vaultExporter = null,
        EnvSafeViewModel? envSafe = null,
        KeyForgeViewModel? keyForge = null,
        KeyMasterViewModel? keyMaster = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(confirmDelete);
        _store = store;
        _confirmDelete = confirmDelete;
        _saveFile = saveFile;
        _vaultExporter = vaultExporter;
        _envSafe = envSafe;
        _keyForge = keyForge;
        _keyMaster = keyMaster;

        Applications = new ObservableCollection<ApplicationRowViewModel>();
        Records = new ObservableCollection<InfrastructureRecordRowViewModel>();

        LoadCommand = new AsyncRelayCommand<object>(_ => LoadAsync());
        CreateApplicationCommand = new AsyncRelayCommand<object>(_ => CreateApplicationAsync());
        UpdateApplicationCommand = new AsyncRelayCommand<object>(_ => UpdateApplicationAsync());
        DeleteApplicationCommand = new AsyncRelayCommand<object>(_ => DeleteApplicationAsync());
        RestoreApplicationCommand = new AsyncRelayCommand<object>(_ => RestoreApplicationAsync());
        HardDeleteApplicationCommand = new AsyncRelayCommand<object>(_ => HardDeleteApplicationAsync());
        CreateRecordCommand = new AsyncRelayCommand<object>(_ => CreateRecordAsync());
        UpdateRecordCommand = new AsyncRelayCommand<object>(_ => UpdateRecordAsync());
        DeleteRecordCommand = new AsyncRelayCommand<object>(_ => DeleteRecordAsync());
        RestoreRecordCommand = new AsyncRelayCommand<object>(_ => RestoreRecordAsync());
        HardDeleteRecordCommand = new AsyncRelayCommand<object>(_ => HardDeleteRecordAsync());
        RefreshCommand = new AsyncRelayCommand<object>(_ => LoadAsync());
        OpenApplicationCommand = new AsyncRelayCommand<object>(p => OpenApplicationAsync(p as ApplicationRowViewModel));
        OpenRecordCommand = new AsyncRelayCommand<object>(p => OpenRecordAsync(p as InfrastructureRecordRowViewModel));
        BackToApplicationsCommand = new AsyncRelayCommand<object>(_ => BackToApplicationsAsync());
        BackToApplicationCommand = new AsyncRelayCommand<object>(_ => BackToApplicationAsync());
        DeleteRecordRowCommand = new AsyncRelayCommand<object>(p => DeleteRecordRowAsync(p as InfrastructureRecordRowViewModel));
        ExportCommand = new AsyncRelayCommand<object>(_ => ExportAsync());
        ExportApplicationCommand = new AsyncRelayCommand<object>(_ => ExportApplicationAsync());
        ExportRecordCommand = new AsyncRelayCommand<object>(_ => ExportRecordAsync());
        OpenEnvSafeCommand = new RelayCommand(OpenEnvSafe);
        CloseEnvSafeCommand = new RelayCommand(CloseEnvSafe);
        OpenKeyForgeCommand = new RelayCommand(OpenKeyForge);
        CloseKeyForgeCommand = new RelayCommand(CloseKeyForge);
        OpenKeyMasterCommand = new RelayCommand(OpenKeyMaster);
        CloseKeyMasterCommand = new RelayCommand(CloseKeyMaster);
    }

    /// <summary>The list of applications shown in the left pane.</summary>
    public ObservableCollection<ApplicationRowViewModel> Applications { get; }

    /// <summary>The list of records under the selected application.</summary>
    public ObservableCollection<InfrastructureRecordRowViewModel> Records { get; }

    /// <summary>The selected application, or null.</summary>
    public ApplicationRowViewModel? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                // WHAT: copy the selected record into the edit fields.
                // WHY:  the detail form binds TwoWay to Edit*; without this
                //       copy the form would show the previous selection.
                if (value is null)
                {
                    EditName = string.Empty;
                    EditDescription = string.Empty;
                    EditOwner = string.Empty;
                    EditRepositoryUrl = string.Empty;
                    EditApplicationUrl = string.Empty;
                    EditTags = string.Empty;
                    EditNotes = string.Empty;
                    EditStatus = RecordStatus.Active;
                }
                else
                {
                    EditName = value.Source.Name;
                    EditDescription = value.Source.Description ?? string.Empty;
                    EditOwner = value.Source.Owner ?? string.Empty;
                    EditRepositoryUrl = value.Source.RepositoryUrl ?? string.Empty;
                    EditApplicationUrl = value.Source.ApplicationUrl ?? string.Empty;
                    EditTags = string.Join(", ", value.Source.Tags);
                    EditNotes = value.Source.Notes ?? string.Empty;
                    EditStatus = value.Source.Status;
                }
                OnPropertyChanged(nameof(HasSelectedApplication));
                OnPropertyChanged(nameof(CanEditApplication));
                OnPropertyChanged(nameof(CanDeleteApplication));
                _ = LoadRecordsForSelectedAsync();
            }
        }
    }

    /// <summary>The selected record, or null.</summary>
    public InfrastructureRecordRowViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetProperty(ref _selectedRecord, value))
            {
                if (value is null)
                {
                    EditRecordName = string.Empty;
                    EditRecordProvider = string.Empty;
                    EditRecordCategory = ServiceCategory.Other;
                    EditRecordEnvironment = EnvironmentKind.Other;
                    EditRecordDashboardUrl = string.Empty;
                    EditRecordLoginEmail = string.Empty;
                    EditRecordRegion = string.Empty;
                    EditRecordResourceId = string.Empty;
                    EditRecordNotes = string.Empty;
                    EditRecordTags = string.Empty;
                    EditRecordStatus = RecordStatus.Active;
                }
                else
                {
                    EditRecordName = value.Source.Name;
                    EditRecordProvider = value.Source.Provider ?? string.Empty;
                    EditRecordCategory = value.Source.ServiceCategory;
                    EditRecordEnvironment = value.Source.Environment;
                    EditRecordDashboardUrl = value.Source.DashboardUrl ?? string.Empty;
                    EditRecordLoginEmail = value.Source.LoginEmail ?? string.Empty;
                    EditRecordRegion = value.Source.Region ?? string.Empty;
                    EditRecordResourceId = value.Source.ResourceId ?? string.Empty;
                    EditRecordNotes = value.Source.Notes ?? string.Empty;
                    EditRecordTags = string.Join(", ", value.Source.Tags);
                    EditRecordStatus = value.Source.Status;
                }
                OnPropertyChanged(nameof(HasSelectedRecord));
                OnPropertyChanged(nameof(CanEditRecord));
                OnPropertyChanged(nameof(CanDeleteRecord));
            }
        }
    }

    /// <summary>True when an application is selected.</summary>
    public bool HasSelectedApplication => SelectedApplication is not null;

    /// <summary>True when a record is selected.</summary>
    public bool HasSelectedRecord => SelectedRecord is not null;

    /// <summary>True when the selected application can be edited (not deleted).</summary>
    public bool CanEditApplication => SelectedApplication is not null && !SelectedApplication.IsDeleted;

    /// <summary>True when the selected application can be deleted.</summary>
    public bool CanDeleteApplication => SelectedApplication is not null;

    /// <summary>True when the selected record can be edited (not deleted).</summary>
    public bool CanEditRecord => SelectedRecord is not null && !SelectedRecord.IsDeleted;

    /// <summary>True when the selected record can be deleted.</summary>
    public bool CanDeleteRecord => SelectedRecord is not null;

    /// <summary>True while a load is in progress.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>True while a save is in progress.</summary>
    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    /// <summary>Status text shown at the bottom of the page.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }


    // ---- Editable state for the selected application -------------------
    // WHAT: mutable properties bound to the application detail form.
    // WHY:  the domain Application is an immutable record; the UI needs
    //       mutable text fields; UpdateApplicationCommand rebuilds a fresh
    //       Application from these fields on save.

    /// <summary>Editable: the selected application's name.</summary>
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

    /// <summary>Editable: the selected application's description.</summary>
    public string EditDescription { get => _editDescription; set => SetProperty(ref _editDescription, value); }

    /// <summary>Editable: the selected application's owner.</summary>
    public string EditOwner { get => _editOwner; set => SetProperty(ref _editOwner, value); }

    /// <summary>Editable: the selected application's repository URL.</summary>
    public string EditRepositoryUrl { get => _editRepositoryUrl; set => SetProperty(ref _editRepositoryUrl, value); }

    /// <summary>Editable: the selected application's public URL.</summary>
    public string EditApplicationUrl { get => _editApplicationUrl; set => SetProperty(ref _editApplicationUrl, value); }

    /// <summary>Editable: the selected application's tags, comma-separated.</summary>
    public string EditTags { get => _editTags; set => SetProperty(ref _editTags, value); }

    /// <summary>Editable: the selected application's free-text notes.</summary>
    public string EditNotes { get => _editNotes; set => SetProperty(ref _editNotes, value); }

    /// <summary>Editable: the selected application's lifecycle status.</summary>
    public RecordStatus EditStatus { get => _editStatus; set => SetProperty(ref _editStatus, value); }

    // ---- Editable state for the selected infrastructure record ----------
    // WHAT: mutable properties bound to the record detail form.
    // WHY:  same pattern as the application form — the domain record is
    //       immutable; UpdateRecordCommand rebuilds a fresh
    //       InfrastructureRecord from these fields on save.

    /// <summary>Editable: the selected record's name.</summary>
    public string EditRecordName { get => _editRecordName; set => SetProperty(ref _editRecordName, value); }

    /// <summary>Editable: the selected record's provider (free text).</summary>
    public string EditRecordProvider { get => _editRecordProvider; set => SetProperty(ref _editRecordProvider, value); }

    /// <summary>Editable: the selected record's service category.</summary>
    public ServiceCategory EditRecordCategory { get => _editRecordCategory; set => SetProperty(ref _editRecordCategory, value); }

    /// <summary>Editable: the selected record's environment.</summary>
    public EnvironmentKind EditRecordEnvironment { get => _editRecordEnvironment; set => SetProperty(ref _editRecordEnvironment, value); }

    /// <summary>Editable: the selected record's dashboard URL.</summary>
    public string EditRecordDashboardUrl { get => _editRecordDashboardUrl; set => SetProperty(ref _editRecordDashboardUrl, value); }

    /// <summary>Editable: the selected record's login email (informational).</summary>
    public string EditRecordLoginEmail { get => _editRecordLoginEmail; set => SetProperty(ref _editRecordLoginEmail, value); }

    /// <summary>Editable: the selected record's region.</summary>
    public string EditRecordRegion { get => _editRecordRegion; set => SetProperty(ref _editRecordRegion, value); }

    /// <summary>Editable: the selected record's provider-side resource id.</summary>
    public string EditRecordResourceId { get => _editRecordResourceId; set => SetProperty(ref _editRecordResourceId, value); }

    /// <summary>Editable: the selected record's free-text notes.</summary>
    public string EditRecordNotes { get => _editRecordNotes; set => SetProperty(ref _editRecordNotes, value); }

    /// <summary>Editable: the selected record's tags, comma-separated.</summary>
    public string EditRecordTags { get => _editRecordTags; set => SetProperty(ref _editRecordTags, value); }

    /// <summary>Editable: the selected record's lifecycle status.</summary>
    public RecordStatus EditRecordStatus { get => _editRecordStatus; set => SetProperty(ref _editRecordStatus, value); }

    /// <summary>The format extension chosen for single-record export (".md" default).</summary>
    public string ExportRecordFormat
    {
        get => _exportRecordFormat;
        set => SetProperty(ref _exportRecordFormat, value);
    }

    /// <summary>Free-text search filter for applications and records.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ReloadCurrentView();
            }
        }
    }

    /// <summary>When true, soft-deleted rows are included in the lists.</summary>
    public bool IncludeDeleted
    {
        get => _includeDeleted;
        set
        {
            if (SetProperty(ref _includeDeleted, value))
            {
                ReloadCurrentView();
            }
        }
    }

    /// <summary>Loads applications on page activation.</summary>
    public ICommand LoadCommand { get; }
    public ICommand CreateApplicationCommand { get; }
    public ICommand UpdateApplicationCommand { get; }
    public ICommand DeleteApplicationCommand { get; }
    public ICommand RestoreApplicationCommand { get; }
    public ICommand HardDeleteApplicationCommand { get; }
    public ICommand CreateRecordCommand { get; }
    public ICommand UpdateRecordCommand { get; }
    public ICommand DeleteRecordCommand { get; }
    public ICommand RestoreRecordCommand { get; }
    public ICommand HardDeleteRecordCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenApplicationCommand { get; }
    public ICommand OpenRecordCommand { get; }
    public ICommand BackToApplicationsCommand { get; }
    public ICommand BackToApplicationCommand { get; }
    public ICommand DeleteRecordRowCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ExportApplicationCommand { get; }
    public ICommand ExportRecordCommand { get; }

    /// <summary>Opens the EnvSafe sub-app (hides the vault views).</summary>
    public ICommand OpenEnvSafeCommand { get; }

    /// <summary>Closes the EnvSafe sub-app and returns to the vault views.</summary>
    public ICommand CloseEnvSafeCommand { get; }

    /// <summary>Opens the KeyForge sub-app (hides the vault views).</summary>
    public ICommand OpenKeyForgeCommand { get; }

    /// <summary>Closes the KeyForge sub-app and returns to the vault views.</summary>
    public ICommand CloseKeyForgeCommand { get; }

    /// <summary>Opens the KeyMaster sub-app (hides the vault views).</summary>
    public ICommand OpenKeyMasterCommand { get; }

    /// <summary>Closes the KeyMaster sub-app and returns to the vault views.</summary>
    public ICommand CloseKeyMasterCommand { get; }

    /// <summary>The EnvSafe sub-app view model, or null when unavailable.</summary>
    public EnvSafeViewModel? EnvSafe => _envSafe;

    /// <summary>The KeyForge sub-app view model, or null when unavailable.</summary>
    public KeyForgeViewModel? KeyForge => _keyForge;

    /// <summary>The KeyMaster sub-app view model, or null when unavailable.</summary>
    public KeyMasterViewModel? KeyMaster => _keyMaster;

    /// <summary>True when the EnvSafe sub-app is the active view.</summary>
    public bool IsEnvSafeActive
    {
        get => _isEnvSafeActive;
        private set
        {
            if (SetProperty(ref _isEnvSafeActive, value))
            {
                OnPropertyChanged(nameof(IsAnySubAppActive));
            }
        }
    }

    /// <summary>True when the KeyForge sub-app is the active view.</summary>
    public bool IsKeyForgeActive
    {
        get => _isKeyForgeActive;
        private set
        {
            if (SetProperty(ref _isKeyForgeActive, value))
            {
                OnPropertyChanged(nameof(IsAnySubAppActive));
            }
        }
    }

    /// <summary>True when the KeyMaster sub-app is the active view.</summary>
    public bool IsKeyMasterActive
    {
        get => _isKeyMasterActive;
        private set
        {
            if (SetProperty(ref _isKeyMasterActive, value))
            {
                OnPropertyChanged(nameof(IsAnySubAppActive));
            }
        }
    }

    /// <summary>True when any sub-app (EnvSafe, KeyForge, or KeyMaster) is the active view.</summary>
    public bool IsAnySubAppActive => _isEnvSafeActive || _isKeyForgeActive || _isKeyMasterActive;

    /// <summary>The currently shown view.</summary>
    public VaultView CurrentView
    {
        get => _currentView;
        private set
        {
            if (SetProperty(ref _currentView, value))
            {
                OnPropertyChanged(nameof(IsApplicationsView));
                OnPropertyChanged(nameof(IsApplicationDetailView));
                OnPropertyChanged(nameof(IsRecordDetailView));
            }
        }
    }

    /// <summary>True when the application list view is shown.</summary>
    public bool IsApplicationsView => CurrentView == VaultView.Applications;

    /// <summary>True when the application detail view is shown.</summary>
    public bool IsApplicationDetailView => CurrentView == VaultView.ApplicationDetail;

    /// <summary>True when the record detail view is shown.</summary>
    public bool IsRecordDetailView => CurrentView == VaultView.RecordDetail;

    // ---- command implementations and helpers (part b) ----    // ---- command implementations ----------------------------------------

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Loading…";
        try
        {
            var query = new VaultQuery(
                SearchText: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                StatusFilter: null,
                CategoryFilter: null,
                EnvironmentFilter: null,
                IncludeDeleted: IncludeDeleted,
                Skip: 0,
                Take: 100,
                SortBy: VaultSortField.UpdatedAtDesc);
            var apps = await _store.ListApplicationsAsync(query).ConfigureAwait(true);
            Applications.Clear();
            foreach (var a in apps)
            {
                var count = await _store.CountRecordsAsync(a.Id).ConfigureAwait(true);
                Applications.Add(new ApplicationRowViewModel(a, count));
            }
            StatusText = Applications.Count == 1 ? "1 application." : $"{Applications.Count} applications.";
        }
        catch (Exception ex)
        {
            StatusText = "Load failed: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadRecordsForSelectedAsync()
    {
        Records.Clear();
        if (SelectedApplication is null) return;
        try
        {
            var q = VaultQuery.Default with
            {
                IncludeDeleted = IncludeDeleted,
                SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            };
            var records = await _store.ListRecordsAsync(SelectedApplication.Id, q).ConfigureAwait(true);
            foreach (var r in records) Records.Add(new InfrastructureRecordRowViewModel(r));
        }
        catch (Exception ex)
        {
            StatusText = "Records load failed: " + ex.Message;
        }
    }

    private async Task CreateApplicationAsync()
    {
        var validation = ApplicationNamePolicy.Validate("New Application");
        if (!validation.IsValid)
        {
            StatusText = validation.Reason;
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var app = new Application(
            Id: Guid.NewGuid(),
            Name: "New Application",
            Description: null, Owner: null, RepositoryUrl: null, ApplicationUrl: null,
            Status: RecordStatus.Active,
            Tags: Array.Empty<string>(),
            Notes: null,
            CreatedAtUtc: now, UpdatedAtUtc: now, DeletedAtUtc: null);
        IsSaving = true;
        try
        {
            var created = await _store.CreateApplicationAsync(app).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            SelectedApplication = Applications.FirstOrDefault(a => a.Id == created.Id);
            CurrentView = VaultView.ApplicationDetail;
            StatusText = "Created.";
        }
        catch (Exception ex) { StatusText = "Create failed: " + ex.Message; }
        finally { IsSaving = false; }
    }

    private async Task UpdateApplicationAsync()
    {
        if (SelectedApplication is null) return;
        var validation = ApplicationNamePolicy.Validate(EditName);
        if (!validation.IsValid)
        {
            StatusText = validation.Reason;
            return;
        }
        IsSaving = true;
        try
        {
            // WHAT: rebuild the Application from the edit fields.
            // WHY:  the domain is immutable; a with-expression needs a source.
            var tagList = EditTags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var updated = SelectedApplication.Source with
            {
                Name = EditName,
                Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription,
                Owner = string.IsNullOrWhiteSpace(EditOwner) ? null : EditOwner,
                RepositoryUrl = string.IsNullOrWhiteSpace(EditRepositoryUrl) ? null : EditRepositoryUrl,
                ApplicationUrl = string.IsNullOrWhiteSpace(EditApplicationUrl) ? null : EditApplicationUrl,
                Tags = tagList,
                Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes,
                Status = EditStatus,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await _store.UpdateApplicationAsync(updated).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            SelectedApplication = Applications.FirstOrDefault(a => a.Id == updated.Id);
            StatusText = "Saved.";
        }
        catch (Exception ex) { StatusText = "Save failed: " + ex.Message; }
        finally { IsSaving = false; }
    }

    private async Task DeleteApplicationAsync()
    {
        if (SelectedApplication is null) return;
        var ok = await _confirmDelete("Delete Application", $"Soft-delete '{SelectedApplication.Name}'?").ConfigureAwait(true);
        if (!ok) return;
        IsSaving = true;
        try
        {
            await _store.SoftDeleteApplicationAsync(SelectedApplication.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Deleted.";
        }
        catch (Exception ex) { StatusText = "Delete failed: " + ex.Message; }
        finally { IsSaving = false; }
    }

    private async Task RestoreApplicationAsync()
    {
        if (SelectedApplication is null) return;
        try
        {
            await _store.RestoreApplicationAsync(SelectedApplication.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Restored.";
        }
        catch (Exception ex) { StatusText = "Restore failed: " + ex.Message; }
    }

    private async Task HardDeleteApplicationAsync()
    {
        if (SelectedApplication is null) return;
        var ok = await _confirmDelete("Permanently Delete Application", $"Permanently delete '{SelectedApplication.Name}'? This cannot be undone. All records will be removed.").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _store.HardDeleteApplicationAsync(SelectedApplication.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Permanently deleted.";
        }
        catch (Exception ex) { StatusText = "Permanent delete failed: " + ex.Message; }
    }

    private async Task CreateRecordAsync()
    {
        if (SelectedApplication is null) { StatusText = "Select an application first."; return; }
        var now = DateTimeOffset.UtcNow;
        var rec = new InfrastructureRecord(
            Id: Guid.NewGuid(),
            ApplicationId: SelectedApplication.Id,
            Name: "New Record",
            Provider: null,
            ServiceCategory: ServiceCategory.Other,
            Environment: EnvironmentKind.Other,
            DashboardUrl: null, LoginEmail: null, Region: null, ResourceId: null,
            Status: RecordStatus.Active,
            Notes: null,
            Tags: Array.Empty<string>(),
            CreatedAtUtc: now, UpdatedAtUtc: now, DeletedAtUtc: null);
        try
        {
            var created = await _store.CreateRecordAsync(rec).ConfigureAwait(true);
            await LoadRecordsForSelectedAsync().ConfigureAwait(true);
            SelectedRecord = Records.FirstOrDefault(r => r.Id == created.Id);
            CurrentView = VaultView.RecordDetail;
            StatusText = "Record created.";
        }
        catch (Exception ex) { StatusText = "Record create failed: " + ex.Message; }
    }

    private async Task UpdateRecordAsync()
    {
        if (SelectedRecord is null) return;
        if (string.IsNullOrWhiteSpace(EditRecordName))
        {
            StatusText = "Record name cannot be empty.";
            return;
        }
        IsSaving = true;
        try
        {
            var tagList = EditRecordTags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var updated = SelectedRecord.Source with
            {
                Name = EditRecordName,
                Provider = string.IsNullOrWhiteSpace(EditRecordProvider) ? null : EditRecordProvider,
                ServiceCategory = EditRecordCategory,
                Environment = EditRecordEnvironment,
                DashboardUrl = string.IsNullOrWhiteSpace(EditRecordDashboardUrl) ? null : EditRecordDashboardUrl,
                LoginEmail = string.IsNullOrWhiteSpace(EditRecordLoginEmail) ? null : EditRecordLoginEmail,
                Region = string.IsNullOrWhiteSpace(EditRecordRegion) ? null : EditRecordRegion,
                ResourceId = string.IsNullOrWhiteSpace(EditRecordResourceId) ? null : EditRecordResourceId,
                Status = EditRecordStatus,
                Notes = string.IsNullOrWhiteSpace(EditRecordNotes) ? null : EditRecordNotes,
                Tags = tagList,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await _store.UpdateRecordAsync(updated).ConfigureAwait(true);
            await LoadRecordsForSelectedAsync().ConfigureAwait(true);
            SelectedRecord = Records.FirstOrDefault(r => r.Id == updated.Id);
            StatusText = "Record saved.";
        }
        catch (Exception ex) { StatusText = "Record save failed: " + ex.Message; }
        finally { IsSaving = false; }
    }

    private Task OpenApplicationAsync(ApplicationRowViewModel? row)
    {
        if (row is null) return Task.CompletedTask;
        SelectedApplication = row;
        CurrentView = VaultView.ApplicationDetail;
        return Task.CompletedTask;
    }

    private Task OpenRecordAsync(InfrastructureRecordRowViewModel? row)
    {
        if (row is null) return Task.CompletedTask;
        SelectedRecord = row;
        CurrentView = VaultView.RecordDetail;
        return Task.CompletedTask;
    }

    private Task BackToApplicationsAsync()
    {
        CurrentView = VaultView.Applications;
        SelectedRecord = null;
        SelectedApplication = null;
        return Task.CompletedTask;
    }

    private Task BackToApplicationAsync()
    {
        CurrentView = VaultView.ApplicationDetail;
        SelectedRecord = null;
        return Task.CompletedTask;
    }

    private async Task DeleteRecordRowAsync(InfrastructureRecordRowViewModel? row)
    {
        if (row is null) return;
        var ok = await _confirmDelete("Delete Record", $"Soft-delete '{row.Name}'?").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _store.SoftDeleteRecordAsync(row.Id).ConfigureAwait(true);
            await LoadRecordsForSelectedAsync().ConfigureAwait(true);
            if (SelectedRecord?.Id == row.Id) SelectedRecord = null;
            StatusText = "Record deleted.";
        }
        catch (Exception ex) { StatusText = "Record delete failed: " + ex.Message; }
    }

    private void ReloadCurrentView()
    {
        if (CurrentView == VaultView.Applications)
        {
            _ = LoadAsync();
        }
        else
        {
            _ = LoadRecordsForSelectedAsync();
        }
    }

    private void OpenEnvSafe()
    {
        if (_envSafe is null)
        {
            StatusText = "EnvSafe is not available in this build.";
            return;
        }
        IsEnvSafeActive = true;
    }

    private void CloseEnvSafe()
    {
        IsEnvSafeActive = false;
        StatusText = "Ready.";
    }

    private void OpenKeyForge()
    {
        if (_keyForge is null)
        {
            StatusText = "KeyForge is not available in this build.";
            return;
        }
        IsKeyForgeActive = true;
    }

    private void CloseKeyForge()
    {
        IsKeyForgeActive = false;
        StatusText = "Ready.";
    }

    private void OpenKeyMaster()
    {
        if (_keyMaster is null)
        {
            StatusText = "KeyMaster is not available in this build.";
            return;
        }
        IsKeyMasterActive = true;
    }

    private void CloseKeyMaster()
    {
        IsKeyMasterActive = false;
        StatusText = "Ready.";
    }

    private async Task ExportAsync()
    {
        if (_vaultExporter is null || _saveFile is null)
        {
            StatusText = "Export is not available in this build.";
            return;
        }
        IsSaving = true;
        StatusText = "Exporting…";
        try
        {
            var apps = await ListAllApplicationsAsync().ConfigureAwait(true);
            var payload = new List<VaultApplicationExport>();
            foreach (var app in apps)
            {
                var records = await ListAllRecordsAsync(app.Id).ConfigureAwait(true);
                payload.Add(new VaultApplicationExport(app, records));
            }

            var suggested = "vault-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip";
            var path = await _saveFile("Export Everything", suggested, ".zip").ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Export cancelled.";
                return;
            }

            var result = await _vaultExporter.ExportAllAsync(payload, path).ConfigureAwait(true);
            StatusText = result.Success
                ? $"Exported {payload.Count} application(s) to {result.OutputPath}."
                : "Export failed: " + result.Error;
        }
        catch (Exception ex)
        {
            StatusText = "Export failed: " + ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ExportApplicationAsync()
    {
        if (SelectedApplication is null)
        {
            StatusText = "Select an application first.";
            return;
        }
        if (_vaultExporter is null || _saveFile is null)
        {
            StatusText = "Export is not available in this build.";
            return;
        }

        IsSaving = true;
        StatusText = "Exporting…";
        try
        {
            var records = await ListAllRecordsAsync(SelectedApplication.Id).ConfigureAwait(true);
            var suggested = "application-" + SanitizeFileName(SelectedApplication.Name) + "-"
                + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip";
            var path = await _saveFile("Export Application", suggested, ".zip").ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Export cancelled.";
                return;
            }

            var result = await _vaultExporter.ExportApplicationAsync(
                SelectedApplication.Source, records, path).ConfigureAwait(true);
            StatusText = result.Success
                ? $"Exported to {result.OutputPath}."
                : "Export failed: " + result.Error;
        }
        catch (Exception ex)
        {
            StatusText = "Export failed: " + ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<IReadOnlyList<Application>> ListAllApplicationsAsync()
    {
        var all = new List<Application>();
        var skip = 0;
        const int pageSize = 500;
        while (true)
        {
            var page = await _store.ListApplicationsAsync(
                VaultQuery.Default with { IncludeDeleted = true, Skip = skip, Take = pageSize }).ConfigureAwait(true);
            all.AddRange(page);
            if (page.Count < pageSize) break;
            skip += pageSize;
        }
        return all;
    }

    private async Task<IReadOnlyList<InfrastructureRecord>> ListAllRecordsAsync(Guid applicationId)
    {
        var all = new List<InfrastructureRecord>();
        var skip = 0;
        const int pageSize = 500;
        while (true)
        {
            var page = await _store.ListRecordsAsync(
                applicationId,
                VaultQuery.Default with { IncludeDeleted = true, Skip = skip, Take = pageSize }).ConfigureAwait(true);
            all.AddRange(page);
            if (page.Count < pageSize) break;
            skip += pageSize;
        }
        return all;
    }

    private async Task ExportRecordAsync()
    {
        if (SelectedRecord is null)
        {
            StatusText = "Select a record first.";
            return;
        }
        if (_vaultExporter is null || _saveFile is null)
        {
            StatusText = "Export is not available in this build.";
            return;
        }

        var ext = string.IsNullOrWhiteSpace(ExportRecordFormat) ? ".md" : ExportRecordFormat;
        var suggested = SanitizeFileName(SelectedRecord.Name) + ext;
        IsSaving = true;
        StatusText = "Exporting…";
        try
        {
            var path = await _saveFile("Export Record", suggested, ext).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Export cancelled.";
                return;
            }

            var result = await _vaultExporter.ExportRecordAsync(
                SelectedRecord.Source, ext, path).ConfigureAwait(true);
            StatusText = result.Success
                ? $"Exported to {result.OutputPath}."
                : "Export failed: " + result.Error;
        }
        catch (Exception ex)
        {
            StatusText = "Export failed: " + ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "Untitled" : result;
    }

    private async Task DeleteRecordAsync()
    {
        if (SelectedRecord is null) return;
        var ok = await _confirmDelete("Delete Record", $"Soft-delete '{SelectedRecord.Name}'?").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _store.SoftDeleteRecordAsync(SelectedRecord.Id).ConfigureAwait(true);
            await LoadRecordsForSelectedAsync().ConfigureAwait(true);
            StatusText = "Record deleted.";
        }
        catch (Exception ex) { StatusText = "Record delete failed: " + ex.Message; }
    }

    private async Task RestoreRecordAsync()
    {
        if (SelectedRecord is null) return;
        try
        {
            await _store.RestoreRecordAsync(SelectedRecord.Id).ConfigureAwait(true);
            await LoadRecordsForSelectedAsync().ConfigureAwait(true);
            StatusText = "Record restored.";
        }
        catch (Exception ex) { StatusText = "Record restore failed: " + ex.Message; }
    }

    private async Task HardDeleteRecordAsync()
    {
        if (SelectedRecord is null) return;
        var ok = await _confirmDelete("Permanently Delete Record", $"Permanently delete '{SelectedRecord.Name}'? This cannot be undone.").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _store.HardDeleteRecordAsync(SelectedRecord.Id).ConfigureAwait(true);
            await LoadRecordsForSelectedAsync().ConfigureAwait(true);
            StatusText = "Record permanently deleted.";
        }
        catch (Exception ex) { StatusText = "Record permanent delete failed: " + ex.Message; }
    }
}