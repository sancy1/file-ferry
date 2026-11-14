// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/PromptStoreBehaviorTests.cs
// PURPOSE:  Behavior tests for SqlitePromptStore that are not part of the
//           IPromptStore contract: tag replacement semantics on PATCH when
//           Tags is unspecified vs specified, and the hard-delete cascade
//           to PromptTags.
// RELATION: Guards SqlitePromptStore (P1.3). Uses PromptSchemaBootstrap (P1.1).
// NOTES:    Temp-file database per test. Deletes in Dispose. The cascade
//           test opens through bootstrap.OpenAsync so PRAGMA foreign_keys = ON
//           is set. The record type Prompt is aliased to PromptRecord (F-084).
// ============================================================================

using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Behavior tests for <see cref="SqlitePromptStore"/> beyond the contract.
/// </summary>
public sealed class PromptStoreBehaviorTests : IDisposable
{
    private readonly string _dbPath;

    public PromptStoreBehaviorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-store-beh-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static readonly string[] TagsKeepAB = new[] { "keep-a", "keep-b" };
    private static readonly string[] TagsNewX = new[] { "new-x" };


    private static PromptRecord NewPrompt(string title = "T") =>
        new(Guid.NewGuid(), title, "B", BodyFormat.PlainText, null, null, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task PatchWithUnspecifiedTagsLeavesTagsUnchanged()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt() with { Tags = TagsKeepAB };
        await store.CreateAsync(p);
        var patch = new PromptPatch(
            new PatchField<string>(false, default), new PatchField<string>(false, default), new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(false, default), new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null), new PatchField<bool>(false, default),
            new PatchField<IReadOnlyList<string>>(false, null));
        await store.PatchAsync(p.Id, patch);
        var back = await store.GetAsync(p.Id);
        Assert.NotNull(back);
        Assert.Equal(2, back!.Tags.Count);
        Assert.Contains("keep-a", back.Tags);
        Assert.Contains("keep-b", back.Tags);
    }

    [Fact]
    public async Task PatchWithSpecifiedTagsReplacesTagSet()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt() with { Tags = new[] { "old-a", "old-b" } };
        await store.CreateAsync(p);
        var patch = new PromptPatch(
            new PatchField<string>(false, default), new PatchField<string>(false, default), new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(false, default), new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null), new PatchField<bool>(false, default),
            new PatchField<IReadOnlyList<string>>(true, TagsNewX));
        await store.PatchAsync(p.Id, patch);
        var back = await store.GetAsync(p.Id);
        Assert.NotNull(back);
        Assert.Single(back!.Tags);
        Assert.Equal("new-x", back.Tags[0]);
    }
    [Fact]
    public async Task HardDeleteCascadesToPromptTags()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
        var p = NewPrompt() with { Tags = new[] { "cascade-a", "cascade-b" } };
        await store.CreateAsync(p);
        await store.HardDeleteAsync(p.Id);
        await using var conn = await bootstrap.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM PromptTags WHERE PromptId = $pid;";
        cmd.Parameters.AddWithValue("$pid", p.Id.ToString());
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0, count);
    }

    private sealed class StubVersionStore : IPromptVersionStore, IPromptVersionStoreTransactional
    {
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