// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/SqliteDeploymentVaultStoreTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault
// since:    Phase A (Deployment Vault registry)
// purpose:  Behavior and integration tests for SqliteDeploymentVaultStore.
//           Each test uses a real temp-file SQLite database created by the
//           schema bootstrap; the file is deleted in Dispose.
//
// RELATIONSHIPS
//   Implements : n/a (test class), IDisposable
//   Depends on : DeploymentVault.Platform.Storage.SqliteDeploymentVaultStore,
//                DeploymentVault.Platform.Storage.DeploymentVaultSchemaBootstrap,
//                DeploymentVault.Core.Domain.*,
//                Microsoft.Data.Sqlite
//   Used by    : test runner
//   See also   : SqliteDeploymentVaultStore, DeploymentVaultSchemaBootstrap,
//                PromptStoreBehaviorTests (pattern reference)
//
// NOTES
//   - One temp DB file per test instance, deleted in Dispose. A real SQLite
//     file proves the schema bootstrap, the SQL, and the PRAGMA foreign_keys
//     cascade; an in-memory database would not.
//   - No Portfolio.Resilience stub. The store does not take an executor
//     (per F-014); it throws directly.
//   - Tests are behavior-focused: they assert what the store DOES, not just
//     what an interface says it must do.
// ============================================================================

using DeploymentVault.Core.Abstractions;
using DeploymentVault.Core.Domain;
using DeploymentVault.Platform.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault;

/// <summary>
/// Tests for <see cref="SqliteDeploymentVaultStore"/> against a real
/// temp-file database.
/// </summary>
public sealed class SqliteDeploymentVaultStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DeploymentVaultSchemaBootstrap _bootstrap;
    private readonly SqliteDeploymentVaultStore _store;

    public SqliteDeploymentVaultStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"vault-store-beh-{Guid.NewGuid():N}.db");
        _bootstrap = new DeploymentVaultSchemaBootstrap(_dbPath);
        _store = new SqliteDeploymentVaultStore(_bootstrap);
    }

    public void Dispose()
    {
        _bootstrap.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static readonly string[] AppDefaultTags = new[] { "prod", "eu" };
    private static readonly string[] RecDefaultTags = new[] { "api" };

    private static Application NewApp(string name) => new(
        Id: Guid.NewGuid(),
        Name: name,
        Description: "desc",
        Owner: "owner",
        RepositoryUrl: "https://example.com/repo",
        ApplicationUrl: "https://example.com",
        Status: RecordStatus.Active,
        Tags: AppDefaultTags,
        Notes: "notes",
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        DeletedAtUtc: null);

    private static InfrastructureRecord NewRec(Guid appId, string name) => new(
        Id: Guid.NewGuid(),
        ApplicationId: appId,
        Name: name,
        Provider: "Render",
        ServiceCategory: ServiceCategory.Hosting,
        Environment: EnvironmentKind.Production,
        DashboardUrl: "https://dashboard.example.com",
        LoginEmail: "ops@example.com",
        Region: "eu-west-1",
        ResourceId: "res-123",
        Status: RecordStatus.Active,
        Notes: "record notes",
        Tags: RecDefaultTags,
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        DeletedAtUtc: null);

    [Fact]
    public async Task CreateApplicationPersistsAllFields()
    {
        var app = NewApp("MyApp");
        await _store.CreateApplicationAsync(app);
        var back = await _store.GetApplicationAsync(app.Id);
        Assert.NotNull(back);
        Assert.Equal("MyApp", back!.Name);
        Assert.Equal("desc", back.Description);
        Assert.Equal("owner", back.Owner);
        Assert.Equal("https://example.com/repo", back.RepositoryUrl);
        Assert.Equal("https://example.com", back.ApplicationUrl);
        Assert.Equal(RecordStatus.Active, back.Status);
        Assert.Equal(2, back.Tags.Count);
        Assert.Null(back.DeletedAtUtc);
    }

    [Fact]
    public async Task GetApplicationByIdReturnsNullWhenMissing()
    {
        var result = await _store.GetApplicationAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateApplicationChangesFields()
    {
        var app = NewApp("Original");
        await _store.CreateApplicationAsync(app);
        var updated = app with { Name = "Renamed", Notes = "new notes", UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) };
        await _store.UpdateApplicationAsync(updated);
        var back = await _store.GetApplicationAsync(app.Id);
        Assert.NotNull(back);
        Assert.Equal("Renamed", back!.Name);
        Assert.Equal("new notes", back.Notes);
    }

    [Fact]
    public async Task SoftDeleteSetsTombstone()
    {
        var app = NewApp("ToDelete");
        await _store.CreateApplicationAsync(app);
        await _store.SoftDeleteApplicationAsync(app.Id);
        var back = await _store.GetApplicationAsync(app.Id);
        Assert.NotNull(back);
        Assert.NotNull(back!.DeletedAtUtc);
    }

    [Fact]
    public async Task RestoreClearsTombstone()
    {
        var app = NewApp("ToRestore");
        await _store.CreateApplicationAsync(app);
        await _store.SoftDeleteApplicationAsync(app.Id);
        await _store.RestoreApplicationAsync(app.Id);
        var back = await _store.GetApplicationAsync(app.Id);
        Assert.NotNull(back);
        Assert.Null(back!.DeletedAtUtc);
    }

    [Fact]
    public async Task HardDeleteRemovesRow()
    {
        var app = NewApp("ToPurge");
        await _store.CreateApplicationAsync(app);
        await _store.HardDeleteApplicationAsync(app.Id);
        var back = await _store.GetApplicationAsync(app.Id);
        Assert.Null(back);
    }

    [Fact]
    public async Task ListApplicationsFiltersBySearchText()
    {
        await _store.CreateApplicationAsync(NewApp("Alpha"));
        await _store.CreateApplicationAsync(NewApp("Beta"));
        await _store.CreateApplicationAsync(NewApp("Gamma"));
        var query = VaultQuery.Default with { SearchText = "alp" };
        var result = await _store.ListApplicationsAsync(query);
        Assert.Single(result);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task ListApplicationsFiltersByStatus()
    {
        var a1 = NewApp("ActiveOne");
        var a2 = NewApp("ArchivedOne") with { Status = RecordStatus.Archived };
        await _store.CreateApplicationAsync(a1);
        await _store.CreateApplicationAsync(a2);
        var query = VaultQuery.Default with { StatusFilter = RecordStatus.Archived };
        var result = await _store.ListApplicationsAsync(query);
        Assert.Single(result);
        Assert.Equal("ArchivedOne", result[0].Name);
    }

    // ---- Second half (records tests + class close) ----

    [Fact]
    public async Task DuplicateNameCaseInsensitiveThrows()
    {
        await _store.CreateApplicationAsync(NewApp("Foo"));
        var dup = NewApp("foo");
        var ex = await Assert.ThrowsAsync<SqliteException>(async () =>
            await _store.CreateApplicationAsync(dup));
        Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameNameAfterSoftDeleteAllowed()
    {
        var first = NewApp("Foo");
        await _store.CreateApplicationAsync(first);
        await _store.SoftDeleteApplicationAsync(first.Id);
        var second = NewApp("foo");
        await _store.CreateApplicationAsync(second);
        var back = await _store.GetApplicationAsync(second.Id);
        Assert.NotNull(back);
        Assert.Equal("foo", back!.Name);
    }

    [Fact]
    public async Task CreateRecordPersistsAndLinksToApplication()
    {
        var app = NewApp("Parent");
        await _store.CreateApplicationAsync(app);
        var rec = NewRec(app.Id, "Render API");
        await _store.CreateRecordAsync(rec);
        var back = await _store.GetRecordAsync(rec.Id);
        Assert.NotNull(back);
        Assert.Equal(app.Id, back!.ApplicationId);
        Assert.Equal("Render API", back.Name);
        Assert.Equal("Render", back.Provider);
        Assert.Equal(ServiceCategory.Hosting, back.ServiceCategory);
        Assert.Equal(EnvironmentKind.Production, back.Environment);
    }

    [Fact]
    public async Task CountRecordsReturnsActiveOnly()
    {
        var app = NewApp("Counter");
        await _store.CreateApplicationAsync(app);
        var r1 = NewRec(app.Id, "one");
        var r2 = NewRec(app.Id, "two");
        await _store.CreateRecordAsync(r1);
        await _store.CreateRecordAsync(r2);
        await _store.SoftDeleteRecordAsync(r2.Id);
        var count = await _store.CountRecordsAsync(app.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HardDeleteApplicationCascadesRecords()
    {
        var app = NewApp("Cascader");
        await _store.CreateApplicationAsync(app);
        var rec = NewRec(app.Id, "child");
        await _store.CreateRecordAsync(rec);
        await _store.HardDeleteApplicationAsync(app.Id);
        var back = await _store.GetRecordAsync(rec.Id);
        Assert.Null(back);
    }

    [Fact]
    public async Task CreateRecordWithoutApplicationThrows()
    {
        var orphan = NewRec(Guid.NewGuid(), "orphan");
        var ex = await Assert.ThrowsAsync<SqliteException>(async () =>
            await _store.CreateRecordAsync(orphan));
        Assert.Contains("FOREIGN KEY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}