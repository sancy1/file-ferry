// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/SqlitePromptSearchTests.cs
// PURPOSE:  Behavior tests for SqlitePromptStore.SearchAsync: text search
//           over title and body (FTS5), category scope, sub-category scope,
//           text+tag combination, soft-delete exclusion, and pagination.
// RELATION: Guards SqlitePromptStore.SearchAsync (P1.5). Uses
//           PromptSchemaBootstrap (P1.1).
// NOTES:    Temp-file database per test. Deletes in Dispose. Every prompt
//           with a non-null CategoryId or SubCategoryId has the referenced
//           row inserted first (F-088). The StubVersionStore is not exercised
//           by these tests.
// ============================================================================

using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Behavior tests for <see cref="SqlitePromptStore.SearchAsync"/>.
/// </summary>
public sealed class SqlitePromptSearchTests : IDisposable
{
    private readonly string _dbPath;

    public SqlitePromptSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-search-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private static PromptRecord NewPrompt(string title, string body, Guid? categoryId = null, Guid? subCategoryId = null) =>
        new(Guid.NewGuid(), title, body, BodyFormat.PlainText, categoryId, subCategoryId, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static PromptQuery Query(string? text = null, Guid? categoryId = null, Guid? subCategoryId = null, IReadOnlyList<string>? tags = null, bool favoritesOnly = false, bool includeDeleted = false, int skip = 0, int take = 0) =>
        new(text, PromptScope.All, categoryId, subCategoryId, tags, favoritesOnly, includeDeleted, skip, take);

    private static SqlitePromptStore NewStore(string dbPath, out PromptSchemaBootstrap bootstrap)
    {
        bootstrap = new PromptSchemaBootstrap(dbPath, new StubExecutor());
        return new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());
    }

    [Fact]
    public async Task SearchByTextFindsByTitleAndBody()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        await store.CreateAsync(NewPrompt("Shakespeare Sonnets", "A study of the sonnets."));
        await store.CreateAsync(NewPrompt("Elizabethan Drama", "Includes shakespeare and Marlowe."));
        await store.CreateAsync(NewPrompt("Unrelated", "Nothing here."));

        var results = await store.SearchAsync(Query("shakespeare"));

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchByCategoryScopesResults()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        var catA = Guid.NewGuid();
        var catB = Guid.NewGuid();
        await InsertCategoryAsync(bootstrap, catA, "A");
        await InsertCategoryAsync(bootstrap, catB, "B");

        await store.CreateAsync(NewPrompt("A1", "in A", catA));
        await store.CreateAsync(NewPrompt("A2", "in A", catA));
        await store.CreateAsync(NewPrompt("B1", "in B", catB));

        var results = await store.SearchAsync(Query(categoryId: catA));

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchBySubCategoryScopesResults()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        var cat = Guid.NewGuid();
        var sub1 = Guid.NewGuid();
        var sub2 = Guid.NewGuid();
        await InsertCategoryAsync(bootstrap, cat, "C");
        await InsertSubCategoryAsync(bootstrap, sub1, cat, "S1");
        await InsertSubCategoryAsync(bootstrap, sub2, cat, "S2");

        await store.CreateAsync(NewPrompt("p1", "b1", cat, sub1));
        await store.CreateAsync(NewPrompt("p2", "b2", cat, sub2));

        var results = await store.SearchAsync(Query(subCategoryId: sub1));

        Assert.Single(results);
    }

    private static readonly string[] TagsHistory = new[] { "history" };

    private static async Task InsertCategoryAsync(PromptSchemaBootstrap bootstrap, Guid id, string name)
    {
        await using var conn = await bootstrap.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Categories (Id, Name, Description, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, $name, NULL, 0, $created, $updated);";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertSubCategoryAsync(PromptSchemaBootstrap bootstrap, Guid id, Guid categoryId, string name)
    {
        await using var conn = await bootstrap.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO SubCategories (Id, CategoryId, Name, Description, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, $cat, $name, NULL, 0, $created, $updated);";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$cat", categoryId.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class StubVersionStore : IPromptVersionStore, IPromptVersionStoreTransactional
    {
        public Task<PromptVersion> SnapshotAsync(PromptRecord prompt, string? note = null, CancellationToken ct = default)
            => Task.FromResult(new PromptVersion(Guid.NewGuid(), prompt.Id, prompt.Title, prompt.Body, DateTimeOffset.UtcNow, note));
        public Task<PromptVersion> SnapshotAsync(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx, PromptRecord prompt, string? note, CancellationToken ct)
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
    [Fact]
    public async Task SearchCombinesTextAndTag()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        var p1 = NewPrompt("Shakespeare A", "sonnets") with { Tags = new[] { "history" } };
        var p2 = NewPrompt("Shakespeare B", "sonnets") with { Tags = new[] { "drama" } };
        await store.CreateAsync(p1);
        await store.CreateAsync(p2);

        var results = await store.SearchAsync(Query(text: "shakespeare", tags: TagsHistory));

        Assert.Single(results);
        Assert.Equal("Shakespeare A", results[0].Title);
    }

    [Fact]
    public async Task SearchExcludesSoftDeletedByDefault()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        var p = NewPrompt("to be or not", "to be or not");
        await store.CreateAsync(p);
        await store.SoftDeleteAsync(p.Id);

        var withoutDeleted = await store.SearchAsync(Query(text: "to be"));
        Assert.Empty(withoutDeleted);

        var withDeleted = await store.SearchAsync(Query(text: "to be", includeDeleted: true));
        Assert.Single(withDeleted);
    }

    [Fact]
    public async Task SearchRespectsSkipTake()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        for (int i = 0; i < 5; i++)
        {
            await store.CreateAsync(NewPrompt($"p{i}", "content"));
        }

        var page = await store.SearchAsync(Query(skip: 1, take: 2));

        Assert.Equal(2, page.Count);
    }

    /// <summary>
    /// Proves the FTS5 prefix match (F-266): a single-character query "S"
    /// matches "Shakespeare" because the MATCH term carries the "*" suffix.
    /// Pre-fix, the full-token MATCH required the whole token; the prefix
    /// makes the search incremental (type "S" → S-starting titles; type "Sh"
    /// → narrower).
    /// </summary>
    [Fact]
    public async Task SingleCharacterQueryMatchesPrefix()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqlitePromptStore(bootstrap, new StubExecutor(), new StubVersionStore());

        await store.CreateAsync(NewPrompt("Shakespeare Sonnets", "A study."));
        await store.CreateAsync(NewPrompt("Elizabethan Drama", "Marlowe."));
        await store.CreateAsync(NewPrompt("Xylophone", "A musical instrument."));

        // Single character "S" matches the "Shakespeare" title's prefix
        var s = await store.SearchAsync(Query(text: "S"));
        Assert.Single(s);
        Assert.Equal("Shakespeare Sonnets", s[0].Title);

        // "El" matches "Elizabethan"
        var el = await store.SearchAsync(Query(text: "El"));
        Assert.Single(el);
        Assert.Equal("Elizabethan Drama", el[0].Title);

        // "X" matches "Xylophone"
        var x = await store.SearchAsync(Query(text: "X"));
        Assert.Single(x);
        Assert.Equal("Xylophone", x[0].Title);

        // "Q" matches nothing
        var q = await store.SearchAsync(Query(text: "Q"));
        Assert.Empty(q);
    }}