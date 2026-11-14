// ============================================================================
// FILE:     src/Prompt.Platform/Storage/SqlitePromptVersionStore.cs
// PURPOSE:  SQLite implementation of IPromptVersionStore. Writes snapshots
//           to PromptVersions; reads them newest-first; restores a prompt's
//           title and body from a snapshot.
// RELATION: Implements IPromptVersionStore. Consumed by SqlitePromptStore
//           (P1.3) for the in-transaction snapshot, and by
//           PromptHistoryViewModel (P4.2). Uses PromptSchemaBootstrap (P1.1)
//           for connections.
// NOTES:    - The in-transaction overload accepts the caller's SqliteConnection
//             and SqliteTransaction so the snapshot commits or rolls back
//             with the update (F-003, Mechanism B - F-089).
//           - After each insert, the oldest snapshot beyond the 50-cap is
//             deleted. The cap is a constant here, not a constructor argument,
//             because the plan fixes 50 as the default; a later phase can
//             promote it to a field.
//           - GetVersionsAsync returns at most 50 (the enforced cap).
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Observability;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace Prompt.Platform.Storage;

/// <summary>
/// SQLite implementation of <see cref="IPromptVersionStore"/>.
/// </summary>
public sealed class SqlitePromptVersionStore : IPromptVersionStore, IPromptVersionStoreTransactional
{
    private const int MaxVersionsPerPrompt = 50;

    private readonly PromptSchemaBootstrap _bootstrap;
    private readonly IResilienceExecutor _executor;
    private readonly IPromptEventEmitter _events;

    /// <summary>
    /// Builds a store against the given bootstrap and executor.
    /// </summary>
    /// <param name="bootstrap">The schema bootstrap. Never null.</param>
    /// <param name="executor">The resilience executor. Never null.</param>
    /// <param name="events">
    /// The operation-event emitter. When null, <see cref="NullPromptEventEmitter.Instance"/>
    /// is used so the store can emit unconditionally without null checks.
    /// </param>
    public SqlitePromptVersionStore(
        PromptSchemaBootstrap bootstrap,
        IResilienceExecutor executor,
        IPromptEventEmitter? events = null)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(executor);
        _bootstrap = bootstrap;
        _executor = executor;
        _events = events ?? NullPromptEventEmitter.Instance;
    }

    /// <inheritdoc/>
    public Task<PromptVersion> SnapshotAsync(PromptRecord prompt, string? note = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                var v = await SnapshotInternalAsync(conn, (SqliteTransaction)tx, prompt, note, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return v;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<PromptVersion> SnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, PromptRecord prompt, string? note, CancellationToken cancellationToken)
        => SnapshotInternalAsync(connection, transaction, prompt, note, cancellationToken);
    /// <inheritdoc/>
    public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, PromptId, Title, Body, SavedAt, Note
                    FROM PromptVersions
                    WHERE PromptId = $pid
                    ORDER BY SavedAt DESC
                    LIMIT $limit;
                    """;
                cmd.Parameters.AddWithValue("$pid", promptId.ToString());
                cmd.Parameters.AddWithValue("$limit", MaxVersionsPerPrompt);
                var list = new List<PromptVersion>();
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new PromptVersion(
                        Guid.Parse(reader.GetString(0)),
                        Guid.Parse(reader.GetString(1)),
                        reader.GetString(2),
                        reader.GetString(3),
                        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                        reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
                return (IReadOnlyList<PromptVersion>)list;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PromptRecord> RestoreVersionAsync(Guid promptId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var restored = await _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                PromptVersion? version = null;
                await using (var readCmd = conn.CreateCommand())
                {
                    readCmd.Transaction = (SqliteTransaction)tx;
                    readCmd.CommandText = "SELECT Id, PromptId, Title, Body, SavedAt, Note FROM PromptVersions WHERE Id = $id AND PromptId = $pid;";
                    readCmd.Parameters.AddWithValue("$id", versionId.ToString());
                    readCmd.Parameters.AddWithValue("$pid", promptId.ToString());
                    await using var reader = await readCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        version = new PromptVersion(
                            Guid.Parse(reader.GetString(0)),
                            Guid.Parse(reader.GetString(1)),
                            reader.GetString(2),
                            reader.GetString(3),
                            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                            reader.IsDBNull(5) ? null : reader.GetString(5));
                    }
                }
                if (version is null)
                {
                    throw new InvalidOperationException($"Version {versionId} not found for prompt {promptId}.");
                }
                await using (var updCmd = conn.CreateCommand())
                {
                    updCmd.Transaction = (SqliteTransaction)tx;
                    updCmd.CommandText = "UPDATE Prompts SET Title = $title, Body = $body, UpdatedAt = $updated WHERE Id = $id;";
                    updCmd.Parameters.AddWithValue("$title", version.Title);
                    updCmd.Parameters.AddWithValue("$body", version.Body);
                    updCmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    updCmd.Parameters.AddWithValue("$id", promptId.ToString());
                    await updCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await tx.CommitAsync(ct).ConfigureAwait(false);
                var current = await ReadPromptForRestoreAsync(conn, promptId, ct).ConfigureAwait(false);
                return current;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken).ConfigureAwait(false);
        // A version restore updates the prompt body: emit prompt_updated.
        _events.Emit(PromptEventNames.Updated, promptId);
        return restored;
    }

    /// <inheritdoc/>
    public Task DeleteAllVersionsAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM PromptVersions WHERE PromptId = $pid;";
                cmd.Parameters.AddWithValue("$pid", promptId.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }
    private static async Task<PromptVersion> SnapshotInternalAsync(
        SqliteConnection connection, SqliteTransaction transaction, PromptRecord prompt, string? note, CancellationToken ct)
    {
        var version = new PromptVersion(
            Guid.NewGuid(),
            prompt.Id,
            prompt.Title,
            prompt.Body,
            DateTimeOffset.UtcNow,
            note);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO PromptVersions (Id, PromptId, Title, Body, SavedAt, Note)
                VALUES ($id, $pid, $title, $body, $saved, $note);
                """;
            cmd.Parameters.AddWithValue("$id", version.Id.ToString());
            cmd.Parameters.AddWithValue("$pid", version.PromptId.ToString());
            cmd.Parameters.AddWithValue("$title", version.Title);
            cmd.Parameters.AddWithValue("$body", version.Body);
            cmd.Parameters.AddWithValue("$saved", version.SavedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$note", (object?)version.Note ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var capCmd = connection.CreateCommand())
        {
            capCmd.Transaction = transaction;
            capCmd.CommandText = """
                DELETE FROM PromptVersions
                WHERE PromptId = $pid
                  AND Id NOT IN (
                    SELECT Id FROM PromptVersions
                    WHERE PromptId = $pid
                    ORDER BY SavedAt DESC
                    LIMIT $limit
                  );
                """;
            capCmd.Parameters.AddWithValue("$pid", prompt.Id.ToString());
            capCmd.Parameters.AddWithValue("$limit", MaxVersionsPerPrompt);
            await capCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return version;
    }

    private static async Task<PromptRecord> ReadPromptForRestoreAsync(SqliteConnection conn, Guid id, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Body, Format, CategoryId, SubCategoryId, TargetModel,
                   IsFavorite, IsDeleted, CreatedAt, UpdatedAt
            FROM Prompts WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Prompt {id} not found after restore.");
        }
        return new PromptRecord(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            (BodyFormat)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7) != 0,
            reader.GetInt32(8) != 0,
            Array.Empty<string>(),
            DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture));
    }
}