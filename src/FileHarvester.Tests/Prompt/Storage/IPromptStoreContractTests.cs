// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/IPromptStoreContractTests.cs
// PURPOSE:  Contract tests for IPromptStore. Every store method is exercised
//           at least once; the two F-001 tests (specified-null overrides,
//           unspecified unchanged) prove the PromptPatch semantics.
// RELATION: Guards SqlitePromptStore (P1.3). Uses PromptSchemaBootstrap (P1.1).
// NOTES:    Temp-file database per test. Deletes in Dispose. The bootstrap
//           is IDisposable; the store is not. The record type Prompt is
//           aliased to PromptRecord to avoid the Prompt.Core.Domain.Prompt /
//           Prompt (root namespace) collision (F-084).
// ============================================================================

using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Contract tests for <see cref="IPromptStore"/>.
/// </summary>
public sealed class IPromptStoreContractTests : IDisposable
{
    private readonly string _dbPath;

    public IPromptStoreContractTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-store-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static readonly string[] TagsAB = new[] { "tag-a", "tag-b" };
    private static readonly string[] TagsT1 = new[] { "t1" };

    private static PromptRecord NewPrompt(string title = "T", string body = "B", IReadOnlyList<string>? tags = null) =>
        new(Guid.NewGuid(), title, body, BodyFormat.PlainText, null, null, null, false, false,
            tags ?? Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task CreateThenGetRoundTrips()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt("Hello", "World", TagsAB);
        await store.CreateAsync(p);
        var back = await store.GetAsync(p.Id);
        Assert.NotNull(back);
        Assert.Equal("Hello", back!.Title);
        Assert.Equal("World", back.Body);
        Assert.Equal(2, back.Tags.Count);
    }

    [Fact]
    public async Task UpdateReplacesEveryField()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt("Old", "OldBody", TagsT1);
        await store.CreateAsync(p);
        var replaced = p with { Title = "New", Body = "NewBody", Format = BodyFormat.Markdown, Tags = new[] { "t2", "t3" } };
        await store.UpdateAsync(replaced);
        var back = await store.GetAsync(p.Id);
        Assert.NotNull(back);
        Assert.Equal("New", back!.Title);
        Assert.Equal("NewBody", back.Body);
        Assert.Equal(BodyFormat.Markdown, back.Format);
        Assert.Equal(2, back.Tags.Count);
    }

    [Fact]
    public async Task PatchSpecifiedNullOverridesValue()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var catId = Guid.NewGuid();
        await using (var conn = await bootstrap.OpenAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Categories (Id, Name, Description, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, $name, NULL, 0, $created, $updated);";
            cmd.Parameters.AddWithValue("$id", catId.ToString());
            cmd.Parameters.AddWithValue("$name", "TestCategory");
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }
        var p = NewPrompt() with { CategoryId = catId };
        await store.CreateAsync(p);
        var patch = new PromptPatch(
            new PatchField<string>(false, default), new PatchField<string>(false, default), new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(true, null), new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null), new PatchField<bool>(false, default), new PatchField<IReadOnlyList<string>>(false, null));
        await store.PatchAsync(p.Id, patch);
        var back = await store.GetAsync(p.Id);
        Assert.NotNull(back);
        Assert.Null(back!.CategoryId);
    }

    [Fact]
    public async Task PatchUnspecifiedFieldIsUnchanged()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var catId = Guid.NewGuid();
        await using (var conn = await bootstrap.OpenAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Categories (Id, Name, Description, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, $name, NULL, 0, $created, $updated);";
            cmd.Parameters.AddWithValue("$id", catId.ToString());
            cmd.Parameters.AddWithValue("$name", "TestCategory");
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }
        var p = NewPrompt() with { CategoryId = catId };
        await store.CreateAsync(p);
        var patch = new PromptPatch(
            new PatchField<string>(false, default), new PatchField<string>(false, default), new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(false, default), new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null), new PatchField<bool>(false, default), new PatchField<IReadOnlyList<string>>(false, null));
        await store.PatchAsync(p.Id, patch);
        var back = await store.GetAsync(p.Id);
        Assert.NotNull(back);
        Assert.Equal(catId, back!.CategoryId);
    }
    [Fact]
    public async Task SoftDeleteThenRestoreThenHardDelete()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt();
        await store.CreateAsync(p);
        await store.SoftDeleteAsync(p.Id);
        var deleted = await store.GetAsync(p.Id);
        Assert.NotNull(deleted);
        Assert.True(deleted!.IsDeleted);
        await store.RestoreAsync(p.Id);
        var restored = await store.GetAsync(p.Id);
        Assert.NotNull(restored);
        Assert.False(restored!.IsDeleted);
        await store.HardDeleteAsync(p.Id);
        Assert.Null(await store.GetAsync(p.Id));
    }

    [Fact]
    public async Task AttachAndDetachTagIsIdempotent()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt();
        await store.CreateAsync(p);
        await store.AttachTagAsync(p.Id, "alpha");
        await store.AttachTagAsync(p.Id, "alpha");
        var afterAttach = await store.GetAsync(p.Id);
        Assert.NotNull(afterAttach);
        Assert.Single(afterAttach!.Tags);
        await store.DetachTagAsync(p.Id, "alpha");
        await store.DetachTagAsync(p.Id, "alpha");
        var afterDetach = await store.GetAsync(p.Id);
        Assert.NotNull(afterDetach);
        Assert.Empty(afterDetach!.Tags);
    }

    private sealed class StubVersionStore : IPromptVersionStore, IPromptVersionStoreTransactional
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Interface method must be instance.")]
        public Task<PromptVersion> SnapshotAsync(PromptRecord prompt, string? note = null, CancellationToken ct = default)
            => Task.FromResult(new PromptVersion(Guid.NewGuid(), prompt.Id, prompt.Title, prompt.Body, DateTimeOffset.UtcNow, note));
        public Task<PromptVersion> SnapshotAsync(SqliteConnection conn, SqliteTransaction tx, PromptRecord prompt, string? note, CancellationToken ct)
            => Task.FromResult(new PromptVersion(Guid.NewGuid(), prompt.Id, prompt.Title, prompt.Body, DateTimeOffset.UtcNow, note));
        public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid promptId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PromptVersion>>(Array.Empty<PromptVersion>());
        public Task<PromptRecord> RestoreVersionAsync(Guid promptId, Guid versionId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteAllVersionsAsync(Guid promptId, CancellationToken ct = default)
            => throw new NotSupportedException();
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
}