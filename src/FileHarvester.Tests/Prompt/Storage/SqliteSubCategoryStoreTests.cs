// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/SqliteSubCategoryStoreTests.cs
// PURPOSE:  Contract and behavior tests for SqliteCategoryStore's
//           ISubCategoryStore implementation.
// RELATION: Guards SqliteCategoryStore (P1.2).
// NOTES:    Temp-file database per test. Deletes in Dispose.
// ============================================================================

using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// Contract and behavior tests for <see cref="SqliteCategoryStore"/>'s
/// sub-category operations.
/// </summary>
public sealed class SqliteSubCategoryStoreTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteSubCategoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-subtest-{Guid.NewGuid():N}.db");
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
        var catId = Guid.NewGuid();
        await ((ICategoryStore)store).CreateAsync(new Category(catId, "Work", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var sub = new SubCategory(Guid.NewGuid(), catId, "Email", null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await ((ISubCategoryStore)store).CreateAsync(sub);
        var back = await ((ISubCategoryStore)store).GetAsync(sub.Id);

        Assert.NotNull(back);
        Assert.Equal("Email", back!.Name);
        Assert.Equal(catId, back.CategoryId);
    }

    [Fact]
    public async Task ListFiltersByCategoryWhenProvided()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteCategoryStore(bootstrap, new StubExecutor());
        var cat1 = Guid.NewGuid();
        var cat2 = Guid.NewGuid();
        await ((ICategoryStore)store).CreateAsync(new Category(cat1, "C1", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await ((ICategoryStore)store).CreateAsync(new Category(cat2, "C2", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await ((ISubCategoryStore)store).CreateAsync(new SubCategory(Guid.NewGuid(), cat1, "S1", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await ((ISubCategoryStore)store).CreateAsync(new SubCategory(Guid.NewGuid(), cat2, "S2", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var inCat1 = await ((ISubCategoryStore)store).ListAsync(cat1);

        Assert.Single(inCat1);
        Assert.Equal("S1", inCat1[0].Name);
    }

    [Fact]
    public async Task DeleteRemovesSubCategoryOnly()
    {
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var store = new SqliteCategoryStore(bootstrap, new StubExecutor());
        var catId = Guid.NewGuid();
        await ((ICategoryStore)store).CreateAsync(new Category(catId, "C", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var sub = new SubCategory(Guid.NewGuid(), catId, "S", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await ((ISubCategoryStore)store).CreateAsync(sub);

        await ((ISubCategoryStore)store).DeleteAsync(sub.Id);

        Assert.Null(await ((ISubCategoryStore)store).GetAsync(sub.Id));
        Assert.NotNull(await ((ICategoryStore)store).GetAsync(catId));
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