// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/DeploymentVaultViewModelTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault
// since:    Phase A (Deployment Vault registry)
// purpose:  Unit tests for DeploymentVaultViewModel using a fake
//           IDeploymentVaultStore. No Avalonia, no database.
//
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : FileHarvester.App.ViewModels.DeploymentVaultViewModel,
//                DeploymentVault.Core.Abstractions.IDeploymentVaultStore,
//                DeploymentVault.Core.Domain.*
//   Used by    : test runner
//   See also   : DeploymentVaultViewModel, FakeDeploymentVaultStore (nested)
//
// NOTES
//   - The fake store is a nested private class. It holds in-memory lists
//     and implements all 15 interface methods. Its ThrowOnNext flag lets
//     error paths be tested without a real database.
//   - Confirm-delete is a simple lambda: `(_, _) => Task.FromResult(true/false)`.
//   - No underscore test method names (CA1707). No `new[]` literals in
//     helpers (CA1861) — tags use static readonly fields.
// ============================================================================

using DeploymentVault.Core.Abstractions;
using DeploymentVault.Core.Domain;
using DeploymentVault.Platform.Export;
using FileHarvester.App.ViewModels;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault;

/// <summary>
/// Unit tests for <see cref="DeploymentVaultViewModel"/>.
/// </summary>
public sealed class DeploymentVaultViewModelTests
{
    private static readonly string[] SampleTags = new[] { "prod" };

    private static Application NewApp(string name) => new(
        Id: Guid.NewGuid(),
        Name: name,
        Description: null,
        Owner: null,
        RepositoryUrl: null,
        ApplicationUrl: null,
        Status: RecordStatus.Active,
        Tags: SampleTags,
        Notes: null,
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        DeletedAtUtc: null);

    private static Func<string, string, Task<bool>> AlwaysTrue => (_, _) => Task.FromResult(true);
    private static Func<string, string, Task<bool>> AlwaysFalse => (_, _) => Task.FromResult(false);

    private static InfrastructureRecord NewRecord(Guid applicationId, string name) => new(
        Id: Guid.NewGuid(),
        ApplicationId: applicationId,
        Name: name,
        Provider: "Render",
        ServiceCategory: ServiceCategory.Hosting,
        Environment: EnvironmentKind.Production,
        DashboardUrl: "https://dash",
        LoginEmail: "ops@example.com",
        Region: "eu-west-1",
        ResourceId: "srv-123",
        Status: RecordStatus.Active,
        Notes: "notes",
        Tags: SampleTags,
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        DeletedAtUtc: null);

    [Fact]
    public void ConstructorRejectsNullStore()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentVaultViewModel(null!, AlwaysTrue));
    }

    [Fact]
    public void ConstructorRejectsNullConfirmDelete()
    {
        var store = new FakeDeploymentVaultStore();
        Assert.Throws<ArgumentNullException>(() =>
            new DeploymentVaultViewModel(store, null!));
    }

    [Fact]
    public async Task LoadCommandPopulatesApplications()
    {
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(NewApp("Alpha"));
        store.Applications.Add(NewApp("Beta"));
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);

        var cmd = (AsyncRelayCommand<object>)vm.LoadCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Equal(2, vm.Applications.Count);
    }

    [Fact]
    public async Task LoadCommandSetsStatusText()
    {
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(NewApp("Alpha"));
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);

        var cmd = (AsyncRelayCommand<object>)vm.LoadCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Contains("1 application", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Second half (remaining tests + class close) ----
    [Fact]
    public async Task LoadCommandHandlesStoreError()
    {
        var store = new FakeDeploymentVaultStore { ThrowOnList = true };
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var cmd = (AsyncRelayCommand<object>)vm.LoadCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.StartsWith("Load failed:", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateApplicationCommandPersistsAndReloads()
    {
        var store = new FakeDeploymentVaultStore();
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var cmd = (AsyncRelayCommand<object>)vm.CreateApplicationCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Single(store.Applications);
        Assert.Single(vm.Applications);
    }

    [Fact]
    public async Task DeleteApplicationCommandCancelsWhenConfirmReturnsFalse()
    {
        var store = new FakeDeploymentVaultStore();
        var app = NewApp("ToDelete");
        store.Applications.Add(app);
        var vm = new DeploymentVaultViewModel(store, AlwaysFalse);
        vm.SelectedApplication = new ApplicationRowViewModel(app, 0);
        var cmd = (AsyncRelayCommand<object>)vm.DeleteApplicationCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Null(store.Applications[0].DeletedAtUtc);
    }

    [Fact]
    public async Task DeleteApplicationCommandProceedsWhenConfirmReturnsTrue()
    {
        var store = new FakeDeploymentVaultStore();
        var app = NewApp("ToDelete");
        store.Applications.Add(app);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        vm.SelectedApplication = new ApplicationRowViewModel(app, 0);
        var cmd = (AsyncRelayCommand<object>)vm.DeleteApplicationCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.NotNull(store.Applications[0].DeletedAtUtc);
    }

    [Fact]
    public void SelectedApplicationPopulatesEditFields()
    {
        var app = new Application(
            Id: Guid.NewGuid(),
            Name: "Alpha",
            Description: "desc",
            Owner: "owner",
            RepositoryUrl: "https://repo",
            ApplicationUrl: "https://app",
            Status: RecordStatus.Archived,
            Tags: SampleTags,
            Notes: "notes",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            DeletedAtUtc: null);
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);

        vm.SelectedApplication = new ApplicationRowViewModel(app, 0);

        Assert.Equal("Alpha", vm.EditName);
        Assert.Equal("desc", vm.EditDescription);
        Assert.Equal("owner", vm.EditOwner);
        Assert.Equal("https://repo", vm.EditRepositoryUrl);
        Assert.Equal("https://app", vm.EditApplicationUrl);
        Assert.Equal("prod", vm.EditTags);
        Assert.Equal("notes", vm.EditNotes);
        Assert.Equal(RecordStatus.Archived, vm.EditStatus);
    }

    [Fact]
    public void SelectedApplicationNullClearsEditFields()
    {
        var app = NewApp("Alpha");
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);
        vm.SelectedApplication = new ApplicationRowViewModel(app, 0);
        Assert.Equal("Alpha", vm.EditName);

        vm.SelectedApplication = null;

        Assert.Equal(string.Empty, vm.EditName);
        Assert.Equal(string.Empty, vm.EditDescription);
        Assert.Equal(string.Empty, vm.EditOwner);
        Assert.Equal(string.Empty, vm.EditRepositoryUrl);
        Assert.Equal(string.Empty, vm.EditApplicationUrl);
        Assert.Equal(string.Empty, vm.EditTags);
        Assert.Equal(string.Empty, vm.EditNotes);
        Assert.Equal(RecordStatus.Active, vm.EditStatus);
    }

    [Fact]
    public async Task UpdateApplicationCommandSavesEditedName()
    {
        var store = new FakeDeploymentVaultStore();
        var app = NewApp("Original");
        store.Applications.Add(app);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        vm.SelectedApplication = new ApplicationRowViewModel(app, 0);
        vm.EditName = "Renamed";
        var cmd = (AsyncRelayCommand<object>)vm.UpdateApplicationCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Equal("Renamed", store.Applications[0].Name);
    }

    [Fact]
    public void SelectedRecordPopulatesEditFields()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Render API");
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);

        vm.SelectedRecord = new InfrastructureRecordRowViewModel(rec);

        Assert.Equal("Render API", vm.EditRecordName);
        Assert.Equal("Render", vm.EditRecordProvider);
        Assert.Equal(ServiceCategory.Hosting, vm.EditRecordCategory);
        Assert.Equal(EnvironmentKind.Production, vm.EditRecordEnvironment);
        Assert.Equal("https://dash", vm.EditRecordDashboardUrl);
        Assert.Equal("ops@example.com", vm.EditRecordLoginEmail);
        Assert.Equal("eu-west-1", vm.EditRecordRegion);
        Assert.Equal("srv-123", vm.EditRecordResourceId);
        Assert.Equal("notes", vm.EditRecordNotes);
        Assert.Equal("prod", vm.EditRecordTags);
        Assert.Equal(RecordStatus.Active, vm.EditRecordStatus);
    }

    [Fact]
    public void SelectedRecordNullClearsEditFields()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Render API");
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);
        vm.SelectedRecord = new InfrastructureRecordRowViewModel(rec);
        Assert.Equal("Render API", vm.EditRecordName);

        vm.SelectedRecord = null;

        Assert.Equal(string.Empty, vm.EditRecordName);
        Assert.Equal(string.Empty, vm.EditRecordProvider);
        Assert.Equal(ServiceCategory.Other, vm.EditRecordCategory);
        Assert.Equal(EnvironmentKind.Other, vm.EditRecordEnvironment);
        Assert.Equal(string.Empty, vm.EditRecordDashboardUrl);
        Assert.Equal(string.Empty, vm.EditRecordLoginEmail);
        Assert.Equal(string.Empty, vm.EditRecordRegion);
        Assert.Equal(string.Empty, vm.EditRecordResourceId);
        Assert.Equal(string.Empty, vm.EditRecordNotes);
        Assert.Equal(string.Empty, vm.EditRecordTags);
        Assert.Equal(RecordStatus.Active, vm.EditRecordStatus);
    }

    [Fact]
    public async Task UpdateRecordCommandSavesEditedName()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Original");
        var store = new FakeDeploymentVaultStore();
        store.Records.Add(rec);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        vm.SelectedRecord = new InfrastructureRecordRowViewModel(rec);
        vm.EditRecordName = "Renamed Record";
        var cmd = (AsyncRelayCommand<object>)vm.UpdateRecordCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Equal("Renamed Record", store.Records[0].Name);
    }

    // ---- Three-view navigation (LAYOUT v8) ----

    [Fact]
    public void DefaultViewIsApplications()
    {
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);
        Assert.Equal(VaultView.Applications, vm.CurrentView);
        Assert.True(vm.IsApplicationsView);
        Assert.False(vm.IsApplicationDetailView);
        Assert.False(vm.IsRecordDetailView);
    }

    [Fact]
    public async Task OpenApplicationCommandSwitchesToDetailView()
    {
        var app = NewApp("Alpha");
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var row = new ApplicationRowViewModel(app, 0);
        var cmd = (AsyncRelayCommand<object>)vm.OpenApplicationCommand;

        cmd.Execute(row);
        await cmd.ExecutionTask;

        Assert.Equal(VaultView.ApplicationDetail, vm.CurrentView);
        Assert.Same(row, vm.SelectedApplication);
    }

    [Fact]
    public async Task BackToApplicationsCommandReturnsToListAndClearsSelection()
    {
        var app = NewApp("Alpha");
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var row = new ApplicationRowViewModel(app, 0);
        var open = (AsyncRelayCommand<object>)vm.OpenApplicationCommand;
        open.Execute(row);
        await open.ExecutionTask;

        var back = (AsyncRelayCommand<object>)vm.BackToApplicationsCommand;
        back.Execute(null);
        await back.ExecutionTask;

        Assert.Equal(VaultView.Applications, vm.CurrentView);
        Assert.Null(vm.SelectedApplication);
    }

    [Fact]
    public async Task OpenRecordCommandSwitchesToRecordDetailView()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Render API");
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);
        var row = new InfrastructureRecordRowViewModel(rec);
        var cmd = (AsyncRelayCommand<object>)vm.OpenRecordCommand;

        cmd.Execute(row);
        await cmd.ExecutionTask;

        Assert.Equal(VaultView.RecordDetail, vm.CurrentView);
        Assert.Same(row, vm.SelectedRecord);
    }

    [Fact]
    public async Task BackToApplicationCommandReturnsToApplicationDetail()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Render API");
        var vm = new DeploymentVaultViewModel(new FakeDeploymentVaultStore(), AlwaysTrue);
        var recRow = new InfrastructureRecordRowViewModel(rec);
        var open = (AsyncRelayCommand<object>)vm.OpenRecordCommand;
        open.Execute(recRow);
        await open.ExecutionTask;

        var back = (AsyncRelayCommand<object>)vm.BackToApplicationCommand;
        back.Execute(null);
        await back.ExecutionTask;

        Assert.Equal(VaultView.ApplicationDetail, vm.CurrentView);
        Assert.Null(vm.SelectedRecord);
    }

    [Fact]
    public async Task DeleteRecordRowCommandSoftDeletesWithoutTouchingApplication()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Empty");
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        store.Records.Add(rec);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var row = new InfrastructureRecordRowViewModel(rec);
        var cmd = (AsyncRelayCommand<object>)vm.DeleteRecordRowCommand;

        cmd.Execute(row);
        await cmd.ExecutionTask;

        Assert.NotNull(store.Records[0].DeletedAtUtc);
        Assert.Null(store.Applications[0].DeletedAtUtc);
    }

    // ---- Search/filter auto-reload and export ----

    [Fact]
    public async Task SearchTextChangeReloadsApplications()
    {
        var store = new FakeDeploymentVaultStore();
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var load = (AsyncRelayCommand<object>)vm.LoadCommand;
        load.Execute(null);
        await load.ExecutionTask;
        Assert.Empty(vm.Applications);

        store.Applications.Add(NewApp("Late Arrival"));
        vm.SearchText = "late";
        await Task.Delay(50);

        Assert.Single(vm.Applications);
        Assert.Equal("Late Arrival", vm.Applications[0].Name);
    }

    [Fact]
    public async Task IncludeDeletedChangeReloadsApplications()
    {
        var store = new FakeDeploymentVaultStore();
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        var load = (AsyncRelayCommand<object>)vm.LoadCommand;
        load.Execute(null);
        await load.ExecutionTask;

        store.Applications.Add(NewApp("Toggled"));
        vm.IncludeDeleted = true;
        await Task.Delay(50);

        Assert.Single(vm.Applications);
    }

    private static VaultExporter NewExporter() =>
        new VaultExporter(new FormatWriterRegistry(new IFormatWriter[]
        {
            new MarkdownFormatWriter(),
            new TxtFormatWriter(),
            new JsonFormatWriter(),
            new CsvFormatWriter(),
            new PdfFormatWriter(),
            new ZipFormatWriter(),
        }));

    [Fact]
    public async Task ExportCommandWritesZipWithAllApplications()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Render API");
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        store.Records.Add(rec);
        var tempPath = Path.Combine(Path.GetTempPath(), "vault-export-" + Guid.NewGuid().ToString("N") + ".zip");
        Func<string, string, string, Task<string?>> saveFile = (_, _, _) => Task.FromResult<string?>(tempPath);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue, saveFile, NewExporter());
        var cmd = (AsyncRelayCommand<object>)vm.ExportCommand;

        try
        {
            cmd.Execute(null);
            await cmd.ExecutionTask;

            Assert.True(File.Exists(tempPath));
            using var archive = System.IO.Compression.ZipFile.OpenRead(tempPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains(names, n => n.Contains("applications/Alpha/application.md", StringComparison.Ordinal));
            Assert.Contains(names, n => n == "summary.csv");
            Assert.Contains(names, n => n == "records.csv");
            Assert.Contains(names, n => n == "README.txt");
            Assert.Contains("Exported 1 application", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ExportApplicationCommandWritesApplicationZip()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Render API");
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        store.Records.Add(rec);
        var tempPath = Path.Combine(Path.GetTempPath(), "vault-app-" + Guid.NewGuid().ToString("N") + ".zip");
        Func<string, string, string, Task<string?>> saveFile = (_, _, _) => Task.FromResult<string?>(tempPath);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue, saveFile, NewExporter());
        vm.SelectedApplication = new ApplicationRowViewModel(app, 1);
        var cmd = (AsyncRelayCommand<object>)vm.ExportApplicationCommand;

        try
        {
            cmd.Execute(null);
            await cmd.ExecutionTask;

            Assert.True(File.Exists(tempPath));
            using var archive = System.IO.Compression.ZipFile.OpenRead(tempPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains(names, n => n == "application.md");
            Assert.Contains(names, n => n == "records.csv");
            Assert.DoesNotContain(names, n => n.StartsWith("applications/", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ExportCommandCancelledWritesNothing()
    {
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(NewApp("Alpha"));
        Func<string, string, string, Task<string?>> saveFile = (_, _, _) => Task.FromResult<string?>(null);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue, saveFile, NewExporter());
        var cmd = (AsyncRelayCommand<object>)vm.ExportCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Contains("cancelled", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreApplicationCommandClearsTombstone()
    {
        var app = NewApp("Deleted App") with { DeletedAtUtc = DateTimeOffset.UtcNow };
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        vm.SelectedApplication = new ApplicationRowViewModel(app, 0);
        var cmd = (AsyncRelayCommand<object>)vm.RestoreApplicationCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Null(store.Applications[0].DeletedAtUtc);
    }

    [Fact]
    public async Task RestoreRecordCommandClearsTombstone()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord(app.Id, "Gone") with { DeletedAtUtc = DateTimeOffset.UtcNow };
        var store = new FakeDeploymentVaultStore();
        store.Applications.Add(app);
        store.Records.Add(rec);
        var vm = new DeploymentVaultViewModel(store, AlwaysTrue);
        vm.SelectedRecord = new InfrastructureRecordRowViewModel(rec);
        var cmd = (AsyncRelayCommand<object>)vm.RestoreRecordCommand;

        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.Null(store.Records[0].DeletedAtUtc);
    }

    // ---- Fake store (4d-2b) ----
    /// <summary>
    /// In-memory fake store for VM tests. No database, no I/O.
    /// </summary>
    /// <remarks>
    /// ThrowOnList/ThrowOnCreate/ThrowOnDelete flags let each test
    /// trigger one error path. Records methods throw NotSupportedException;
    /// the VM tests do not exercise record paths in Phase A's VM test file.
    /// </remarks>
    private sealed class FakeDeploymentVaultStore : IDeploymentVaultStore
    {
        public List<Application> Applications { get; } = new();
        public List<InfrastructureRecord> Records { get; } = new();
        public bool ThrowOnList { get; set; }
        public bool ThrowOnCreate { get; set; }
        public bool ThrowOnDelete { get; set; }

        public Task<IReadOnlyList<Application>> ListApplicationsAsync(VaultQuery query, CancellationToken cancellationToken = default)
        {
            if (ThrowOnList) throw new InvalidOperationException("Simulated store failure.");
            return Task.FromResult<IReadOnlyList<Application>>(Applications);
        }

        public Task<Application?> GetApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var app = Applications.FirstOrDefault(a => a.Id == id);
            return Task.FromResult(app);
        }

        public Task<Application> CreateApplicationAsync(Application application, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCreate) throw new InvalidOperationException("Simulated create failure.");
            Applications.Add(application);
            return Task.FromResult(application);
        }

        public Task<Application> UpdateApplicationAsync(Application application, CancellationToken cancellationToken = default)
        {
            var idx = Applications.FindIndex(a => a.Id == application.Id);
            if (idx >= 0) Applications[idx] = application;
            return Task.FromResult(application);
        }

        public Task SoftDeleteApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDelete) throw new InvalidOperationException("Simulated delete failure.");
            var idx = Applications.FindIndex(a => a.Id == id);
            if (idx >= 0) Applications[idx] = Applications[idx] with { DeletedAtUtc = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        public Task RestoreApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var idx = Applications.FindIndex(a => a.Id == id);
            if (idx >= 0) Applications[idx] = Applications[idx] with { DeletedAtUtc = null };
            return Task.CompletedTask;
        }

        public Task HardDeleteApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Applications.RemoveAll(a => a.Id == id);
            return Task.CompletedTask;
        }

        public Task<int> CountRecordsAsync(Guid applicationId, CancellationToken cancellationToken = default)
        {
            var count = Records.Count(r => r.ApplicationId == applicationId && r.DeletedAtUtc is null);
            return Task.FromResult(count);
        }

        // ---- Records methods (4d-2b-ii) ----
        public Task<IReadOnlyList<InfrastructureRecord>> ListRecordsAsync(Guid applicationId, VaultQuery query, CancellationToken cancellationToken = default)
        {
            var list = Records.Where(r => r.ApplicationId == applicationId).ToList();
            return Task.FromResult<IReadOnlyList<InfrastructureRecord>>(list);
        }

        public Task<InfrastructureRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var rec = Records.FirstOrDefault(r => r.Id == id);
            return Task.FromResult(rec);
        }

        public Task<InfrastructureRecord> CreateRecordAsync(InfrastructureRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<InfrastructureRecord> UpdateRecordAsync(InfrastructureRecord record, CancellationToken cancellationToken = default)
        {
            var idx = Records.FindIndex(r => r.Id == record.Id);
            if (idx >= 0) Records[idx] = record;
            return Task.FromResult(record);
        }

        public Task SoftDeleteRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var idx = Records.FindIndex(r => r.Id == id);
            if (idx >= 0) Records[idx] = Records[idx] with { DeletedAtUtc = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        public Task RestoreRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var idx = Records.FindIndex(r => r.Id == id);
            if (idx >= 0) Records[idx] = Records[idx] with { DeletedAtUtc = null };
            return Task.CompletedTask;
        }

        public Task HardDeleteRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Records.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }
    }
}