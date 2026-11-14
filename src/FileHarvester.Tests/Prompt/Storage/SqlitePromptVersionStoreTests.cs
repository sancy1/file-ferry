// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/SqlitePromptVersionStoreTests.cs
// PURPOSE:  Behavior tests for SqlitePromptVersionStore: snapshots are
//           created on update and on patch; the snapshot is capped at 50;
//           restoring yields the previous body; a failing update leaves no
//           orphan snapshot (rollback coupling, F-003).
// RELATION: Guards SqlitePromptVersionStore (P1.4) and SqlitePromptStore's
//           snapshot calls (P1.3 edit). Uses PromptSchemaBootstrap (P1.1).
// NOTES:    Temp-file database per test. Deletes in Dispose. The record type
//           Prompt is aliased to PromptRecord to avoid the namespace collision
//           (F-084).
// ============================================================================

using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Behavior tests for <see cref="SqlitePromptVersionStore"/>.
/// </summary>
public sealed class SqlitePromptVersionStoreTests : IDisposable
{
    private readonly string _dbPath;

    public SqlitePromptVersionStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-ver-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static PromptRecord NewPrompt(string title = "T", string body = "B") =>
        new(Guid.NewGuid(), title, body, BodyFormat.PlainText, null, null, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task SnapshotOnUpdate()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteVersionStoreFactory(bootstrap);
        var versions = store.Versions;
        var prompts = store.Prompts;

        var p = NewPrompt("Original", "OriginalBody");
        await prompts.CreateAsync(p);
        await prompts.UpdateAsync(p with { Title = "Updated", Body = "UpdatedBody", UpdatedAt = DateTimeOffset.UtcNow });

        var snapshots = await versions.GetVersionsAsync(p.Id);
        Assert.Single(snapshots);
        Assert.Equal("Original", snapshots[0].Title);
        Assert.Equal("OriginalBody", snapshots[0].Body);
    }

    [Fact]
    public async Task SnapshotOnPatch()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteVersionStoreFactory(bootstrap);
        var versions = store.Versions;
        var prompts = store.Prompts;

        var p = NewPrompt("Original", "OriginalBody");
        await prompts.CreateAsync(p);
        var patch = new PromptPatch(
            new PatchField<string>(true, "Patched"),
            new PatchField<string>(false, default), new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(false, default), new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null), new PatchField<bool>(false, default),
            new PatchField<IReadOnlyList<string>>(false, null));
        await prompts.PatchAsync(p.Id, patch);

        var snapshots = await versions.GetVersionsAsync(p.Id);
        Assert.Single(snapshots);
        Assert.Equal("Original", snapshots[0].Title);
    }

    private sealed class SqliteVersionStoreFactory
    {
        public IPromptStore Prompts { get; }
        public IPromptVersionStore Versions { get; }
        public SqliteVersionStoreFactory(PromptSchemaBootstrap bootstrap)
        {
            Versions = new SqlitePromptVersionStore(bootstrap, new StubExecutor());
            Prompts = new SqlitePromptStore(bootstrap, new StubExecutor(), Versions);
        }
    }

    private sealed class StubExecutor : IResilienceExecutor
    {
        public async Task<T> ExecuteAsync<T>(string p, Func<CancellationToken, Task<T>> op,
            Func<CancellationToken, Task<T>>? fb, string? k, int? t, CancellationToken ct)
            => await op(ct).ConfigureAwait(false);
        public Task ExecuteAsync(string p, Func<CancellationToken, Task> op,
            Func<CancellationToken, Task>? fb, string? k, int? t, CancellationToken ct)
            => op(ct);
    }
    [Fact]
    public async Task SnapshotCapAt50()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteVersionStoreFactory(bootstrap);
        var prompts = store.Prompts;
        var versions = store.Versions;

        var p = NewPrompt("v0", "v0");
        await prompts.CreateAsync(p);

        for (int i = 1; i <= 51; i++)
        {
            p = p with { Title = $"v{i}", UpdatedAt = DateTimeOffset.UtcNow };
            await prompts.UpdateAsync(p);
        }

        var snapshots = await versions.GetVersionsAsync(p.Id);
        Assert.Equal(50, snapshots.Count);
    }

    [Fact]
    public async Task RestoreVersionYieldsPreviousBody()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteVersionStoreFactory(bootstrap);
        var prompts = store.Prompts;
        var versions = store.Versions;

        var p = NewPrompt("v0", "body0");
        await prompts.CreateAsync(p);
        await prompts.UpdateAsync(p with { Title = "v1", Body = "body1", UpdatedAt = DateTimeOffset.UtcNow });

        var snapshots = await versions.GetVersionsAsync(p.Id);
        Assert.Single(snapshots);
        var v0Snapshot = snapshots[0];

        await prompts.UpdateAsync(p with { Title = "v2", Body = "body2", UpdatedAt = DateTimeOffset.UtcNow });

        var restored = await versions.RestoreVersionAsync(p.Id, v0Snapshot.Id);
        Assert.Equal("v0", restored.Title);
        Assert.Equal("body0", restored.Body);
    }

    [Fact]
    public async Task RollbackLeavesNoSnapshot()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteVersionStoreFactory(bootstrap);
        var prompts = store.Prompts;
        var versions = store.Versions;

        var p = NewPrompt();
        await prompts.CreateAsync(p);

        var badCategoryId = Guid.NewGuid();
        var badUpdate = p with { CategoryId = badCategoryId, UpdatedAt = DateTimeOffset.UtcNow };

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => prompts.UpdateAsync(badUpdate));

        var snapshots = await versions.GetVersionsAsync(p.Id);
        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task DeleteAllVersionsAsyncRemovesAllVersions()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteVersionStoreFactory(bootstrap);
        var prompts = store.Prompts;
        var versions = store.Versions;

        var p = NewPrompt("t0", "body0");
        await prompts.CreateAsync(p);
        await prompts.UpdateAsync(p with { Title = "t1", Body = "body1", UpdatedAt = DateTimeOffset.UtcNow });
        await prompts.UpdateAsync(p with { Title = "t2", Body = "body2", UpdatedAt = DateTimeOffset.UtcNow });

        var before = await versions.GetVersionsAsync(p.Id);
        Assert.Equal(2, before.Count);

        await versions.DeleteAllVersionsAsync(p.Id);

        var after = await versions.GetVersionsAsync(p.Id);
        Assert.Empty(after);

        var surviving = await prompts.GetAsync(p.Id);
        Assert.NotNull(surviving);
    }
}