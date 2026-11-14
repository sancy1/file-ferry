// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/PromptVersionStoreEventEmissionTests.cs
// PURPOSE:  Proves SqlitePromptVersionStore emits prompt_updated on a version
//           restore (a restore changes the prompt body) and does NOT emit any
//           prompt operation event on a snapshot (a snapshot is a version row,
//           not a prompt body operation).
// RELATION: Consumes SqlitePromptVersionStore, PromptEventEmitter, PromptEventNames,
//           CapturingLogSink. Uses the same temp-DB + StubExecutor pattern as
//           PromptStoreBehaviorTests.
// NOTES:   - Two-sided: expected operation present AND no other prompt operation
//             name present. Prevents a vacuous pass.
//           - CapturingLogSink exposes Snapshot() (verified; F-289).
//           - Snapshot must not emit — this is the negative case that proves the
//             eight prompt operation names are prompt-scoped, not version-scoped.
// ============================================================================

using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;
using Prompt.Core.Domain;
using Prompt.Platform.Observability;
using Prompt.Platform.Storage;
using FileHarvester.Tests.Infrastructure;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Event-emission tests for <see cref="SqlitePromptVersionStore"/>.
/// </summary>
public sealed class PromptVersionStoreEventEmissionTests : IDisposable
{
    private readonly string _dbPath;

    public PromptVersionStoreEventEmissionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-version-events-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static readonly string[] AllOperationNames = new[]
    {
        PromptEventNames.Created, PromptEventNames.Updated, PromptEventNames.Deleted,
        PromptEventNames.Restored, PromptEventNames.Exported, PromptEventNames.Imported,
        PromptEventNames.SearchExecuted, PromptEventNames.RenderFailed,
    };

    private static PromptRecord NewPrompt(string title = "T") =>
        new(Guid.NewGuid(), title, "B", BodyFormat.PlainText, null, null, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static IReadOnlyList<ResilienceEvent> AssertOnlyOperation(CapturingLogSink sink, string expected)
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

    [Fact]
    public async Task RestoreVersionEmitsPromptUpdatedWithPromptId()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptVersionStore(bootstrap, new StubExecutor(), emitter);

        var p = NewPrompt();
        await using (var conn = await bootstrap.OpenAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO Prompts (Id, Title, Body, Format, IsFavorite, IsDeleted, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $body, $fmt, 0, 0, $created, $updated);
                """;
            cmd.Parameters.AddWithValue("$id", p.Id.ToString());
            cmd.Parameters.AddWithValue("$title", p.Title);
            cmd.Parameters.AddWithValue("$body", p.Body);
            cmd.Parameters.AddWithValue("$fmt", (int)p.Format);
            cmd.Parameters.AddWithValue("$created", p.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$updated", p.UpdatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }
        var version = await store.SnapshotAsync(p);
        sink.Clear();

        await store.RestoreVersionAsync(p.Id, version.Id);

        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Updated);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(p.Id, id);
    }

    [Fact]
    public async Task SnapshotDoesNotEmitAnyPromptOperation()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptVersionStore(bootstrap, new StubExecutor(), emitter);

        var p = NewPrompt();
        // Insert the prompt row so the PromptVersions.PromptId FK is satisfied.
        // The version store correctly requires the parent prompt to exist
        // (P1.1 PRAGMA foreign_keys = ON; F-295).
        await using (var conn = await bootstrap.OpenAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO Prompts (Id, Title, Body, Format, IsFavorite, IsDeleted, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $body, $fmt, 0, 0, $created, $updated);
                """;
            cmd.Parameters.AddWithValue("$id", p.Id.ToString());
            cmd.Parameters.AddWithValue("$title", p.Title);
            cmd.Parameters.AddWithValue("$body", p.Body);
            cmd.Parameters.AddWithValue("$fmt", (int)p.Format);
            cmd.Parameters.AddWithValue("$created", p.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$updated", p.UpdatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }

        await store.SnapshotAsync(p);

        var operations = sink.Snapshot()
            .Select(e => e.Metadata.TryGetValue("operation", out var v) ? v as string : null)
            .Where(v => v is not null)
            .ToArray();
        Assert.DoesNotContain(operations, v => AllOperationNames.Contains(v));
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