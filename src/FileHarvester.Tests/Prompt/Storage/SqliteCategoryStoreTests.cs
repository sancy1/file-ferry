// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/SqliteCategoryStoreTests.cs
// PURPOSE:  Contract and behavior tests for SqliteCategoryStore's
//           ICategoryStore implementation. Covers create/list/get/update/
//           delete plus the cascade that preserves prompts on category
//           deletion.
// RELATION: Guards SqliteCategoryStore (P1.2). Uses PromptSchemaBootstrap
//           (P1.1) for schema creation.
// NOTES:    Temp-file database per test. Deletes in Dispose. Cascade test
//           inserts a Prompt row directly (no SqlitePromptStore yet, that
//           arrives at P1.3) to prove ON DELETE SET NULL works.
// ============================================================================

using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Contract and behavior tests for <see cref="SqliteCategoryStore"/>'s
/// category operations.
/// </summary>
public sealed class SqliteCategoryStoreTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteCategoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-cat-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task CreateThenGetRoundTrips()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteCategoryStore(bootstrap, new StubExecutor());
        var cat = new Category(Guid.NewGuid(), "Work", "Work prompts", 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        await store.CreateAsync(cat);
        var back = await store.GetAsync(cat.Id);

        Assert.NotNull(back);
        Assert.Equal(cat.Id, back!.Id);
        Assert.Equal("Work", back.Name);
    }

    [Fact]
    public async Task ListOrdersBySortOrderThenName()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteCategoryStore(bootstrap, new StubExecutor());
        await store.CreateAsync(new Category(Guid.NewGuid(), "B", null, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await store.CreateAsync(new Category(Guid.NewGuid(), "A", null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await store.CreateAsync(new Category(Guid.NewGuid(), "C", null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var list = await store.ListAsync();

        Assert.Equal(3, list.Count);
        Assert.Equal("A", list[0].Name);
        Assert.Equal("C", list[1].Name);
        Assert.Equal("B", list[2].Name);
    }

    [Fact]
    public async Task UpdateReplacesFields()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteCategoryStore(bootstrap, new StubExecutor());
        var id = Guid.NewGuid();
        await store.CreateAsync(new Category(id, "Old", "old", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await store.UpdateAsync(new Category(id, "New", "new", 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)));

        var back = await store.GetAsync(id);
        Assert.NotNull(back);
        Assert.Equal("New", back!.Name);
        Assert.Equal(5, back.SortOrder);
    }

    [Fact]
    public async Task DeleteDoesNotDeletePrompts()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteCategoryStore(bootstrap, new StubExecutor());
        var cat = new Category(Guid.NewGuid(), "Work", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.CreateAsync(cat);

        // Insert a prompt that references this category, directly in SQL
        // (SqlitePromptStore arrives at P1.3; the cascade is the schema's
        // concern, not the store's).
        await using (var conn = await bootstrap.OpenAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Prompts (Id, Title, Body, Format, CategoryId, SubCategoryId, TargetModel,
                                     IsFavorite, IsDeleted, CreatedAt, UpdatedAt)
                VALUES ($id, 'T', 'B', 0, $cat, NULL, NULL, 0, 0, $created, $updated);
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$cat", cat.Id.ToString());
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        await store.DeleteAsync(cat.Id);

        await using (var conn = await bootstrap.OpenAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CategoryId FROM Prompts;";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.IsDBNull(0));
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
}