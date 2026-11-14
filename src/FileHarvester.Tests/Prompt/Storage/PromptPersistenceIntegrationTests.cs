// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Storage/PromptPersistenceIntegrationTests.cs
// PURPOSE:  End-to-end integration test for the four stores: category,
//           sub-category, prompt, and version. Exercises create, update,
//           search (FTS5), soft-delete, restore, version list, version
//           restore, hard-delete, and the cascade behavior on category
//           deletion.
// RELATION: Guards the four stores together (P1.2, P1.3, P1.4, P1.5).
//           Uses PromptSchemaBootstrap (P1.1) with a real temp-file db.
// NOTES:    One [Fact]. Temp-file database. Deletes in Dispose.
//           The StubVersionStore is not used here; the real
//           SqlitePromptVersionStore is used to prove the snapshot flow
//           inside SqlitePromptStore's transaction (F-003).
// ============================================================================

using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Storage;
using PromptRecord = Prompt.Core.Domain.Prompt;
using Xunit;

namespace FileHarvester.Tests.Prompt.Storage;

/// <summary>
/// End-to-end integration test wiring the four Prompt stores together.
/// </summary>
public sealed class PromptPersistenceIntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public PromptPersistenceIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"prompt-integ-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task FullLifecycleAcrossAllStores()
    {
        // ARRANGE
        using var bootstrap = new PromptSchemaBootstrap(_dbPath, new StubExecutor());
        var executor = new StubExecutor();
        var versions = new SqlitePromptVersionStore(bootstrap, executor);
        var prompts = new SqlitePromptStore(bootstrap, executor, versions);
        var categories = new SqliteCategoryStore(bootstrap, executor);


        var catId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        await ((ICategoryStore)categories).CreateAsync(new Category(
            catId, "Literature", "Literary prompts", 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await ((ISubCategoryStore)categories).CreateAsync(new SubCategory(
            subId, catId, "Sonnets", null, 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // ACT - create
        await prompts.CreateAsync(new PromptRecord(
            promptId,
            "Shall I compare thee",
            "A sonnet prompt body",
            BodyFormat.PlainText,
            catId,
            subId,
            null,
            false,
            false,
            TagsShakespeareSonnets,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        // ASSERT - create
        var created = await prompts.GetAsync(promptId);
        Assert.NotNull(created);
        Assert.Equal(catId, created!.CategoryId);
        Assert.Equal(subId, created.SubCategoryId);
        Assert.Equal(2, created.Tags.Count);

        // ACT - search by text
        var byText = await prompts.SearchAsync(
            new PromptQuery("compare", PromptScope.All, null, null, null, false, false, 0, 0));
        Assert.Contains(byText, p => p.Id == promptId);

        // ACT - search by tag
        var byTag = await prompts.SearchAsync(
            new PromptQuery(null, PromptScope.All, null, null, TagsShakespeare, false, false, 0, 0));
        Assert.Contains(byTag, p => p.Id == promptId);

        // ACT - update (triggers version snapshot)
        var updated = created with
        {
            Title = "Shall I compare thee (revised)",
            Body = "A revised sonnet prompt body",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await prompts.UpdateAsync(updated);

        // ASSERT - version snapshot
        var versionsForPrompt = await versions.GetVersionsAsync(promptId);
        Assert.Single(versionsForPrompt);
        Assert.Equal("Shall I compare thee", versionsForPrompt[0].Title);
        Assert.Equal("A sonnet prompt body", versionsForPrompt[0].Body);

        // ACT - soft delete
        await prompts.SoftDeleteAsync(promptId);
        var afterSoftDelete = await prompts.GetAsync(promptId);
        Assert.NotNull(afterSoftDelete);
        Assert.True(afterSoftDelete!.IsDeleted);

        // ASSERT - excluded from default search
        var defaultSearch = await prompts.SearchAsync(
            new PromptQuery("compare", PromptScope.All, null, null, null, false, false, 0, 0));
        Assert.DoesNotContain(defaultSearch, p => p.Id == promptId);

        // ACT - restore
        await prompts.RestoreAsync(promptId);
        var afterRestore = await prompts.GetAsync(promptId);
        Assert.NotNull(afterRestore);
        Assert.False(afterRestore!.IsDeleted);

        // ACT - restore version
        var restored = await versions.RestoreVersionAsync(promptId, versionsForPrompt[0].Id);
        Assert.Equal("Shall I compare thee", restored.Title);
        Assert.Equal("A sonnet prompt body", restored.Body);

        // ACT - delete sub-category and category, assert prompts preserved
        await ((ISubCategoryStore)categories).DeleteAsync(subId);
        var afterSubDelete = await prompts.GetAsync(promptId);
        Assert.NotNull(afterSubDelete);
        Assert.Null(afterSubDelete!.SubCategoryId);

        await ((ICategoryStore)categories).DeleteAsync(catId);
        var afterCatDelete = await prompts.GetAsync(promptId);
        Assert.NotNull(afterCatDelete);
        Assert.Null(afterCatDelete!.CategoryId);

        // ACT - hard delete
        await prompts.HardDeleteAsync(promptId);
        Assert.Null(await prompts.GetAsync(promptId));
    }

    private static readonly string[] TagsShakespeareSonnets = new[] { "shakespeare", "sonnets" };
    private static readonly string[] TagsShakespeare = new[] { "shakespeare" };

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