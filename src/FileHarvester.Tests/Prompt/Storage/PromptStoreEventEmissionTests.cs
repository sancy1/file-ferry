// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/PromptStoreEventEmissionTests.cs
// PURPOSE:  End-to-end proof that SqlitePromptStore emits the P5.1 operation
//           events through a real PromptEventEmitter (not the no-op fallback).
//           Each fact constructs the store with a CapturingLogSink-backed
//           emitter, performs one operation, and asserts the captured
//           ResilienceEvent carries the exact Metadata["operation"] name.
// RELATION: Consumes SqlitePromptStore (Prompt.Platform.Storage),
//           PromptEventEmitter and PromptEventNames (Prompt.Platform.Observability
//           and Prompt.Core.Domain), and CapturingLogSink (test infrastructure).
//           Uses the same temp-file SQLite + StubExecutor + StubVersionStore
//           pattern as PromptStoreBehaviorTests. No production code is
//           modified by this file.
// NOTES:   - Two-sided assertion: the expected operation name is present AND
//             no other prompt operation name is present. A one-sided assertion
//             passes vacuously when the sink is empty and would stop protecting
//             the vocabulary.
//           - CapturingLogSink exposes Snapshot() (verified; F-289). It has no
//             Events property.
//           - SearchAsync is not a single-prompt operation, so its event must
//             NOT carry a promptId metadata key.
//           - The Prompt record alias is PromptRecord (F-084).
// ============================================================================

using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Observability;
using Prompt.Platform.Storage;
using FileHarvester.Tests.Infrastructure;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Proves <see cref="SqlitePromptStore"/> emits each P5.1 operation event
/// through a real <see cref="PromptEventEmitter"/>.
/// </summary>
/// <remarks>
/// The store is constructed with the emitter as its fourth constructor
/// argument. Without it, the store falls back to <c>NullPromptEventEmitter</c>
/// and emits nothing; the tests here prove the real path works.
/// </remarks>
public sealed class PromptStoreEventEmissionTests : IDisposable
{
    private readonly string _dbPath;

    public PromptStoreEventEmissionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-store-events-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static readonly string[] AllOperationNames = new[]
    {
        PromptEventNames.Created,
        PromptEventNames.Updated,
        PromptEventNames.Deleted,
        PromptEventNames.Restored,
        PromptEventNames.Exported,
        PromptEventNames.Imported,
        PromptEventNames.SearchExecuted,
        PromptEventNames.RenderFailed,
    };

    private static PromptRecord NewPrompt(string title = "T") =>
        new(Guid.NewGuid(), title, "B", BodyFormat.PlainText, null, null, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>
    /// Asserts the captured events contain the expected operation name AND no
    /// other prompt operation name. Two-sided by design (see the file NOTES).
    /// </summary>
    private static IReadOnlyList<Portfolio.Resilience.Events.ResilienceEvent> AssertOnlyOperation(
        CapturingLogSink sink, string expected)
    {
        var snapshot = sink.Snapshot();
        var ops = snapshot
            .Select(e => e.Metadata.TryGetValue("operation", out var v) ? v as string : null)
            .Where(v => v is not null)
            .ToArray();

        Assert.Contains(expected, ops);
        var unexpected = ops
            .Where(v => AllOperationNames.Contains(v) && !string.Equals(v, expected, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(unexpected);
        return snapshot;
    }

    /// <summary>T1: <c>CreateAsync</c> emits <c>prompt_created</c> with the prompt id.</summary>
    [Fact]
    public async Task CreateAsyncEmitsPromptCreated()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore(), emitter);

        var p = NewPrompt();
        await store.CreateAsync(p);

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Created);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(p.Id, id);
    }

    /// <summary>T2: <c>UpdateAsync</c> emits <c>prompt_updated</c>.</summary>
    [Fact]
    public async Task UpdateAsyncEmitsPromptUpdated()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore(), emitter);

        var p = NewPrompt();
        await store.CreateAsync(p);
        sink.Clear();

        await store.UpdateAsync(p with { Title = "T2" });

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Updated);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(p.Id, id);
    }

    /// <summary>T3: <c>PatchAsync</c> emits <c>prompt_updated</c> (a patch is an update).</summary>
    [Fact]
    public async Task PatchAsyncEmitsPromptUpdated()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore(), emitter);

        var p = NewPrompt();
        await store.CreateAsync(p);
        sink.Clear();

        var patch = new PromptPatch(
            new PatchField<string>(true, "PatchedTitle"), new PatchField<string>(false, default),
            new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(false, default), new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null), new PatchField<bool>(false, default),
            new PatchField<IReadOnlyList<string>>(false, null));
        await store.PatchAsync(p.Id, patch);

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Updated);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(p.Id, id);
    }

    /// <summary>T4: <c>SoftDeleteAsync</c> emits <c>prompt_deleted</c>.</summary>
    [Fact]
    public async Task SoftDeleteAsyncEmitsPromptDeleted()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore(), emitter);

        var p = NewPrompt();
        await store.CreateAsync(p);
        sink.Clear();

        await store.SoftDeleteAsync(p.Id);

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Deleted);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(p.Id, id);
    }

    /// <summary>T5: <c>RestoreAsync</c> emits <c>prompt_restored</c>.</summary>
    [Fact]
    public async Task RestoreAsyncEmitsPromptRestored()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore(), emitter);

        var p = NewPrompt();
        await store.CreateAsync(p);
        await store.SoftDeleteAsync(p.Id);
        sink.Clear();

        await store.RestoreAsync(p.Id);

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Restored);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(p.Id, id);
    }

    /// <summary>T6: <c>SearchAsync</c> emits <c>prompt_search_executed</c> and
    /// does NOT carry a promptId (search is not single-prompt).</summary>
    [Fact]
    public async Task SearchAsyncEmitsPromptSearchExecutedWithoutPromptId()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore(), emitter);

        await store.CreateAsync(NewPrompt("Searchable"));
        sink.Clear();

        await store.SearchAsync(new PromptQuery(
            "Searchable", PromptScope.All, null, null,
            Array.Empty<string>(), false, false, 0, 10));

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.SearchExecuted);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.False(evt.Metadata.ContainsKey("promptId"));
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