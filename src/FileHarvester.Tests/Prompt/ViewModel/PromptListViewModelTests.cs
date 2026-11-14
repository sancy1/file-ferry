// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/ViewModel/PromptListViewModelTests.cs
// PURPOSE:  Guards PromptListViewModel's observable behavior: LoadAsync builds
//           the category tree and loads prompts; a null-CategoryId prompt adds
//           the Uncategorized pseudo-node; ShowAllCommand clears the tree
//           selection and restores ALL PROMPTS; ManageCategoriesCommand raises
//           ManageCategoriesRequested; SelectedPrompt raises PromptSelected
//           with the Prompt record; GetCategoryLabel returns the breadcrumb.
// RELATION: Added at the P2.7.x ViewModel test backfill (Path 3). Consumes
//           PromptListViewModel (FileHarvester.App.ViewModels); substitutes
//           ICategoryStore, ISubCategoryStore, IPromptStore via NSubstitute.
//           No DB, no UI, no file I/O.
// NOTES:   - Unlike CategoryManagerViewModel, PromptListViewModel does NOT
//             call LoadAsync in its constructor; the caller does. The tests
//             call LoadAsync explicitly and await it. Its internal awaits
//             complete synchronously with the substitutes' Task.FromResult.
//           - The ONLY fire-and-forget path is SelectedCategory's setter
//             (_ = ReloadPromptsAsync()). The ShowAll test uses the
//             WaitReloadAsync helper (a TCS the substitute's SearchAsync
//             signals) to await that reload deterministically.
//           - The test never constructs a PromptQuery; it uses
//             Arg.Any<PromptQuery>() for SearchAsync. The query's shape is
//             the VM's concern, not the test's.
// ============================================================================

using FileHarvester.App.ViewModels;
using FluentAssertions;
using NSubstitute;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.ViewModel;

/// <summary>
/// Guards PromptListViewModel's observable behavior. Six facts: load builds
/// the tree, the Uncategorized pseudo-node, ShowAll clears selection,
/// ManageCategories raises the event, SelectedPrompt raises PromptSelected,
/// GetCategoryLabel returns the breadcrumb.
/// </summary>
public sealed class PromptListViewModelTests
{
    private static Category MakeCategory(string name, int sortOrder = 0) =>
        new(Guid.NewGuid(), name, null, sortOrder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static SubCategory MakeSubCategory(Guid categoryId, string name, int sortOrder = 0) =>
        new(Guid.NewGuid(), categoryId, name, null, sortOrder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static PromptRecord MakePrompt(Guid? categoryId, Guid? subCategoryId, string title) =>
        new(Guid.NewGuid(), title, "Body of " + title, BodyFormat.PlainText,
            categoryId, subCategoryId, null,
            false, false, Array.Empty<string>(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static (ICategoryStore cats, ISubCategoryStore subs, IPromptStore prompts)
        MakeStores(IReadOnlyList<Category> categories, IReadOnlyList<SubCategory> subs, IReadOnlyList<PromptRecord> prompts)
    {
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(categories));
        var subsStore = Substitute.For<ISubCategoryStore>();
        subsStore.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<SubCategory>>(
                subs.Where(s => s.CategoryId == callInfo.Arg<Guid>()).ToList()));
        var promptsStore = Substitute.For<IPromptStore>();
        promptsStore.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(prompts));
        return (cats, subsStore, promptsStore);
    }

    /// <summary>
    /// Proves LoadAsync populates Categories from the tree builder and
    /// VisiblePrompts from the search. One category, one sub-category, two
    /// prompts under the category.
    /// </summary>
    [Fact]
    public async Task LoadAsyncBuildsCategoriesAndPrompts()
    {
        // ARRANGE
        var cat = MakeCategory("Literature");
        var sub = MakeSubCategory(cat.Id, "Sonnets");
        var prompts = new List<PromptRecord> { MakePrompt(cat.Id, sub.Id, "Sonnet 18") };
        var (cats, subs, promptsStore) = MakeStores(new[] { cat }, new[] { sub }, prompts);
        var vm = new PromptListViewModel(cats, subs, promptsStore);

        // ACT
        await vm.LoadAsync();

        // ASSERT
        vm.Categories.Should().HaveCount(1);
        vm.Categories[0].Category.Should().NotBeNull();
        vm.Categories[0].Category!.Name.Should().Be("Literature");
        vm.VisiblePrompts.Should().HaveCount(1);
        vm.VisiblePrompts[0].Prompt.Title.Should().Be("Sonnet 18");
    }

    /// <summary>
    /// Proves LoadAsync adds the Uncategorized pseudo-node when a prompt has
    /// a null CategoryId. The pseudo-node's PromptCount equals the number of
    /// null-category prompts.
    /// </summary>
    [Fact]
    public async Task LoadAsyncAddsUncategorizedPseudoNode()
    {
        // ARRANGE
        var prompts = new List<PromptRecord>
        {
            MakePrompt(null, null, "Loose prompt"),
            MakePrompt(null, null, "Another loose"),
        };
        var (cats, subs, promptsStore) = MakeStores(Array.Empty<Category>(), Array.Empty<SubCategory>(), prompts);
        var vm = new PromptListViewModel(cats, subs, promptsStore);

        // ACT
        await vm.LoadAsync();

        // ASSERT
        vm.Categories.Should().HaveCount(1);
        vm.Categories[0].IsPseudo.Should().BeTrue();
        vm.Categories[0].PromptCount.Should().Be(2);
    }

    /// <summary>
    /// Proves ShowAllCommand clears SelectedCategory and restores ALL
    /// PROMPTS. The SelectedCategory setter fires a fire-and-forget reload;
    /// the WaitReloadAsync TCS signals when the substitute's SearchAsync
    /// returns, so the assertion sees the post-reload state.
    /// </summary>
    [Fact]
    public async Task ShowAllCommandClearsSelection()
    {
        // ARRANGE
        var cat = MakeCategory("Literature");
        var (cats, subs, promptsStore) = MakeStores(
            new[] { cat }, Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searchCount = 0;
        promptsStore.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref searchCount) >= 2) { reloadTcs.TrySetResult(); }
                return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>());
            });
        var vm = new PromptListViewModel(cats, subs, promptsStore);
        await vm.LoadAsync();
        vm.SelectedCategory = vm.Categories[0];

        // ACT
        vm.ShowAllCommand.Execute(null);
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        vm.SelectedCategory.Should().BeNull();
        vm.ProspectsHeader.Should().Be("ALL PROMPTS");
    }

    /// <summary>
    /// Proves ManageCategoriesCommand raises ManageCategoriesRequested
    /// exactly once. The command is a synchronous RelayCommand.
    /// </summary>
    [Fact]
    public void ManageCategoriesCommandRaisesEvent()
    {
        // ARRANGE
        var (cats, subs, promptsStore) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, promptsStore);
        var raised = 0;
        vm.ManageCategoriesRequested += (_, _) => raised++;

        // ACT
        vm.ManageCategoriesCommand.Execute(null);

        // ASSERT
        raised.Should().Be(1);
    }

    /// <summary>
    /// Proves SelectedPrompt's setter raises PromptSelected with the row's
    /// Prompt record. The event carries the Prompt, not the row.
    /// </summary>
    [Fact]
    public async Task SelectedPromptRaisesPromptSelectedEvent()
    {
        // ARRANGE
        var cat = MakeCategory("Literature");
        var prompt = MakePrompt(cat.Id, null, "Selected prompt");
        var (cats, subs, promptsStore) = MakeStores(
            new[] { cat }, Array.Empty<SubCategory>(), new[] { prompt });
        var vm = new PromptListViewModel(cats, subs, promptsStore);
        await vm.LoadAsync();
        PromptRecord? captured = null;
        vm.PromptSelected += (_, p) => captured = p;
        vm.VisiblePrompts.Should().HaveCount(1);

        // ACT
        vm.SelectedPrompt = vm.VisiblePrompts[0];

        // ASSERT
        captured.Should().NotBeNull();
        captured!.Title.Should().Be("Selected prompt");
    }

    /// <summary>
    /// Proves GetCategoryLabel returns "Category / SubCategory" for a pair,
    /// "Category" for a category-only lookup, and "Uncategorized" for a
    /// null category id.
    /// </summary>
    [Fact]
    public async Task GetCategoryLabelReturnsBreadcrumb()
    {
        // ARRANGE
        var cat = MakeCategory("Literature");
        var sub = MakeSubCategory(cat.Id, "Sonnets");
        var (cats, subs, promptsStore) = MakeStores(
            new[] { cat }, new[] { sub }, Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, promptsStore);
        await vm.LoadAsync();

        // ACT + ASSERT
        vm.GetCategoryLabel(cat.Id, sub.Id).Should().Be("Literature / Sonnets");
        vm.GetCategoryLabel(cat.Id, null).Should().Be("Literature");
        vm.GetCategoryLabel(null, null).Should().Be("Uncategorized");
    }

    /// <summary>
    /// Awaits the substitute's reload signal. The TCS is set by the
    /// substitute's SearchAsync when it is called again after the initial
    /// load. Deterministic: no delay, no poll. The timeout is a diagnostic
    /// guard, not a synchronization primitive.
    /// </summary>
    // ---- P2.8 search-bar facts ----

    /// <summary>
    /// Proves SearchText flows into BuildQuery's PromptQuery.Text. The
    /// SearchText setter fires the 250 ms debounce; the TCS signals on the
    /// debounced SearchAsync, and the test asserts the query's Text.
    /// </summary>
    [Fact]
    public async Task SearchTextFiltersPrompts()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reloadTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        var vm = new PromptListViewModel(cats, subs, prompts);

        // ACT
        vm.SearchText = "  shakespeare  ";
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await prompts.Received(1).SearchAsync(
            Arg.Is<PromptQuery>(q => q.Text == "shakespeare"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves FavoritesOnly flows into BuildQuery's PromptQuery.FavoritesOnly.
    /// </summary>
    [Fact]
    public async Task FavoritesOnlyFiltersPrompts()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reloadTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        var vm = new PromptListViewModel(cats, subs, prompts);

        // ACT
        vm.FavoritesOnly = true;
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await prompts.Received(1).SearchAsync(
            Arg.Is<PromptQuery>(q => q.FavoritesOnly == true),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves SearchTags parses comma-separated text into the query's Tags:
    /// "a, b, a" -> ["a", "b"] (split, trim, drop empties, distinct
    /// case-insensitive).
    /// </summary>
    [Fact]
    public async Task SearchTagsParsesCommaSeparated()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reloadTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        var vm = new PromptListViewModel(cats, subs, prompts);

        // ACT
        vm.SearchTags = "a, b, a";
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await prompts.Received(1).SearchAsync(
            Arg.Is<PromptQuery>(q => q.Tags != null && q.Tags.Count == 2 && q.Tags.Contains("a") && q.Tags.Contains("b")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves the debounce cancels the previous reload: two synchronous
    /// SearchText assignments produce exactly one SearchAsync call. The
    /// 250 ms delay has not elapsed between the assignments, so the first
    /// debounce is cancelled; the second fires.
    /// </summary>
    [Fact]
    public async Task DebounceCancelsPreviousReload()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reloadTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        var vm = new PromptListViewModel(cats, subs, prompts);

        // ACT — two synchronous assignments; the first is cancelled
        vm.SearchText = "a";
        vm.SearchText = "ab";
        await WaitReloadAsync(reloadTcs);
        // Diagnostic window: give any late SearchAsync a chance to fire
        await Task.Delay(200);

        // ASSERT — exactly one call, and its Text is the second's value
        await prompts.Received(1).SearchAsync(
            Arg.Is<PromptQuery>(q => q.Text == "ab"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves SearchScope "All" overrides the tree's selected category:
    /// with a category selected, the query's Scope is All and CategoryId is
    /// null (the F-264 patch).
    /// </summary>
    [Fact]
    public async Task SearchScopeOverridesSelectedCategory()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cat = new Category(Guid.NewGuid(), "Lit", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(new[] { cat }));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reloadTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        var vm = new PromptListViewModel(cats, subs, prompts);
        await vm.LoadAsync();
        vm.SelectedCategory = vm.Categories[0];
        prompts.ClearReceivedCalls();

        // ACT — two-step transition; the default SearchScope is "All", so
        // the first transition to "Current Category" fires the setter; the
        // second transition back to "All" is the override under test.
        vm.SearchScope = "Current Category";
        await WaitReloadAsync(reloadTcs);
        prompts.ClearReceivedCalls();
        var allTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { allTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        vm.SearchScope = "All";
        await WaitReloadAsync(allTcs);

        // ASSERT
        await prompts.Received(1).SearchAsync(
            Arg.Is<PromptQuery>(q => q.Scope == PromptScope.All && q.CategoryId == null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves empty SearchText produces a query with Text == null (no
    /// text filter). The previous assignment was non-empty; clearing it
    /// resets the filter.
    /// </summary>
    [Fact]
    public async Task EmptySearchReturnsAllPrompts()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reloadTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });
        var vm = new PromptListViewModel(cats, subs, prompts);
        vm.SearchText = "first";
        await Task.Delay(350); // let the first debounce fire and settle

        // Re-arm the TCS for the clearing assignment
        var clearTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => { clearTcs.TrySetResult(); return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()); });

        // ACT
        vm.SearchText = "";
        await WaitReloadAsync(clearTcs);

        // ASSERT
        await prompts.Received(1).SearchAsync(
            Arg.Is<PromptQuery>(q => q.Text == null),
            Arg.Any<CancellationToken>());
    }
    /// <summary>
    /// Proves ShowAllCommand's full reset (F-270): SearchText, SearchTags,
    /// FavoritesOnly, and SelectedCategory are all cleared together.
    /// </summary>
    [Fact]
    public void ShowAllClearsSearchFilters()
    {
        // ARRANGE
        var (cats, subs, promptsStore) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, promptsStore);
        vm.SearchText = "x";
        vm.SearchTags = "a";
        vm.FavoritesOnly = true;

        // ACT
        vm.ShowAllCommand.Execute(null);

        // ASSERT
        vm.SearchText.Should().Be("");
        vm.SearchTags.Should().Be("");
        vm.FavoritesOnly.Should().BeFalse();
        vm.SelectedCategory.Should().BeNull();
    }
    private static async Task WaitReloadAsync(TaskCompletionSource tcs, int timeoutMs = 2000)
    {
        var timeout = Task.Delay(timeoutMs);
        var winner = await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(false);
        if (winner == timeout)
        {
            throw new TimeoutException("The reload did not complete within " + timeoutMs + " ms.");
        }
        await tcs.Task.ConfigureAwait(false);
    }

    [Fact]
    public async Task BulkDeleteAsyncCallsSoftDeletePerPrompt()
    {
        // ARRANGE
        var (cats, subs, prompts) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, prompts);
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "A")));
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "B")));

        // ACT
        await vm.BulkDeleteAsync();

        // ASSERT
        await prompts.Received(2).SoftDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkMoveAsyncCallsPatchPerPrompt()
    {
        // ARRANGE
        var (cats, subs, prompts) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, prompts);
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "A")));
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "B")));

        // ACT
        await vm.BulkMoveAsync(null, null);

        // ASSERT
        await prompts.Received(2).PatchAsync(
            Arg.Any<Guid>(),
            Arg.Any<PromptPatch>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkAddTagAsyncCallsAttachTagPerPrompt()
    {
        // ARRANGE
        var (cats, subs, prompts) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, prompts);
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "A")));
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "B")));

        // ACT
        await vm.BulkAddTagAsync("history");

        // ASSERT
        await prompts.Received(2).AttachTagAsync(
            Arg.Any<Guid>(),
            "history",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HasSelectionReflectsSelectedPrompts()
    {
        // ARRANGE
        var (cats, subs, prompts) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var vm = new PromptListViewModel(cats, subs, prompts);
        vm.HasSelection.Should().BeFalse();

        // ACT + ASSERT — add
        vm.SelectedPrompts.Add(new PromptListRowViewModel(MakePrompt(null, null, "A")));
        vm.HasSelection.Should().BeTrue();

        // ACT + ASSERT — clear
        vm.SelectedPrompts.Clear();
        vm.HasSelection.Should().BeFalse();
    }
}