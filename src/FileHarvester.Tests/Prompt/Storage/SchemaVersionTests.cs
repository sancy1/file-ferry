// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/SchemaVersionTests.cs
// PURPOSE:  Three facts about PromptSchemaBootstrap: a fresh database
//           creates all seven tables and the FTS5 virtual table plus its
//           three triggers; a second EnsureInitializedAsync is a no-op;
//           a forward schema version throws.
// RELATION: Guards PromptSchemaBootstrap (P1.1). Consumes Microsoft.Data.Sqlite
//           and Portfolio.Resilience.Abstractions (IResilienceExecutor).
// NOTES:    Each test uses a fresh temp-file database, not in-memory, so
//           FTS5 and PRAGMA user_version behave as they do in production.
//           The temp file is deleted in the test's Dispose. The fake
//           IResilienceExecutor just runs the operation; schema creation
//           is idempotent so the fake is safe.
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Platform.Storage;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Schema bootstrap facts: fresh creation, idempotence, forward-version throw.
/// </summary>
public sealed class SchemaVersionTests : IDisposable
{
    private readonly string _dbPath;

    public SchemaVersionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-schema-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* cleanup best-effort */ }
    }

    /// <summary>
    /// A fresh database gains all seven tables and the FTS5 virtual table.
    /// </summary>
    [Fact]
    public async Task FreshDatabaseCreatesAllTablesAndFts()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        await bootstrap.EnsureInitializedAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%';";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Contains("Categories", tables);
        Assert.Contains("SubCategories", tables);
        Assert.Contains("Prompts", tables);
        Assert.Contains("Tags", tables);
        Assert.Contains("PromptTags", tables);
        Assert.Contains("PromptVersions", tables);
        Assert.Contains("PromptDrafts", tables);
        Assert.Contains("PromptsFts", tables);
    }

    /// <summary>
    /// Calling EnsureInitializedAsync twice is a no-op the second time.
    /// </summary>
    [Fact]
    public async Task SecondEnsureInitializedIsIdempotent()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        await bootstrap.EnsureInitializedAsync();
        await bootstrap.EnsureInitializedAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        Assert.Equal(1, version);
    }

    /// <summary>
    /// A forward schema version throws rather than silently opening.
    /// </summary>
    [Fact]
    public async Task ForwardSchemaVersionThrows()
    {
        await using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 99;";
            await cmd.ExecuteNonQueryAsync();
        }

        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.EnsureInitializedAsync());
    }

    private sealed class StubExecutor : IResilienceExecutor
    {
        public async Task<T> ExecuteAsync<T>(
            string policyName,
            Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task<T>>? fallback,
            string? idempotencyKey,
            int? timeBudgetMs,
            CancellationToken ct)
        {
            return await operation(ct).ConfigureAwait(false);
        }

        public Task ExecuteAsync(
            string policyName,
            Func<CancellationToken, Task> operation,
            Func<CancellationToken, Task>? fallback,
            string? idempotencyKey,
            int? timeBudgetMs,
            CancellationToken ct)
        {
            return operation(ct);
        }
    }
}