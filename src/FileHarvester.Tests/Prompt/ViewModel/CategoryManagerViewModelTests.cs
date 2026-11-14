// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/ViewModel/CategoryManagerViewModelTests.cs
// PURPOSE:  Guards CategoryManagerViewModel's observable behavior: the
//           constructor's load populates Categories; create calls
//           ICategoryStore.CreateAsync and reloads; the rename's two-step
//           begin/commit flow sets RenameName + IsRenaming on the first
//           call and commits via UpdateAsync on the second; Cancel clears
//           the rename state without a store call; delete calls
//           ICategoryStore.DeleteAsync and reloads; the Changed event fires
//           on every mutation.
// RELATION: Added at the P2.7.x ViewModel test backfill (Path 3). Consumes
//           CategoryManagerViewModel (FileHarvester.App.ViewModels);
//           substitutes ICategoryStore and ISubCategoryStore via
//           NSubstitute. No DB, no UI, no file I/O.
// NOTES:   - The constructor calls _ = LoadAsync() fire-and-forget. The
//             tests explicitly set ListAsync's return via
//             Task.FromResult(...) so the completed task's continuation
//             runs synchronously; without the explicit return, the bare
//             NSubstitute default is a completed task with default values,
//             which is the same shape but less obvious in the test.
//           - CategoryManagerViewModel's Category / SubCategory construction
//             is verified against the VM's Write 1 content: the record
//             constructor is (Id, Name, Description, SortOrder, CreatedAt,
//             UpdatedAt) and (Id, CategoryId, Name, Description, SortOrder,
//             CreatedAt, UpdatedAt).
//           - F-260 closed: the non-generic AsyncRelayCommand's ICommand.Execute is
//             void and ExecuteAsync is private; the tests invoke Execute(null)
//             and await WaitReloadAsync (a TCS the substitute's second ListAsync
//             signals). No production code changed; AsyncRelayCommand.cs is
//             untouched. The begin branch has no await, so its state is set
//             synchronously before Execute returns.
//           - The tests do not exercise reorder's two UpdateAsync calls
//             beyond the single-call assertion in the delete/commit paths;
//             the reorder's atomicity is F-248 (deferred to P4.x). The
//             reorder's two-call sequence is left to that phase's fix.
// ============================================================================

using FileHarvester.App.ViewModels;
using FluentAssertions;
using NSubstitute;
using Prompt.Core.Domain;

namespace FileHarvester.Tests.Prompt.ViewModel;

/// <summary>
/// Guards CategoryManagerViewModel's observable behavior. Six facts: load on
/// construction, create, rename begin, rename commit, rename cancel, delete.
/// </summary>
public sealed class CategoryManagerViewModelTests
{
    private static Category MakeCategory(string name, int sortOrder = 0) =>
        new(Guid.NewGuid(), name, null, sortOrder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static SubCategory MakeSubCategory(Guid categoryId, string name, int sortOrder = 0) =>
        new(Guid.NewGuid(), categoryId, name, null, sortOrder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>
    /// Proves the constructor's LoadAsync populates Categories from
    /// ICategoryStore.ListAsync. The VM's fire-and-forget load must complete
    /// before Categories is observed; the test's ListAsync returns a
    /// completed task, so the continuation runs synchronously.
    /// </summary>
    [Fact]
    public async Task ConstructorLoadsCategories()
    {
        // ARRANGE
        var categories = Substitute.For<ICategoryStore>();
        var subCategories = Substitute.For<ISubCategoryStore>();
        var seed = new List<Category> { MakeCategory("Literature"), MakeCategory("Poetry") };
        categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(seed));
        subCategories.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(new List<SubCategory>()));

        // ACT
        var vm = new CategoryManagerViewModel(categories, subCategories);
        await Task.Yield();

        // ASSERT
        vm.Categories.Should().HaveCount(2);
        vm.Categories.Select(c => c.Name).Should().BeEquivalentTo("Literature", "Poetry");
    }

    /// <summary>
    /// Proves CreateCategoryAsync calls ICategoryStore.CreateAsync with the
    /// trimmed name and reloads the Categories list. The reload is the
    /// second ListAsync call; the first is the constructor's.
    /// </summary>
    [Fact]
    public async Task CreateCategoryCallsStoreAndReloads()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var categories = Substitute.For<ICategoryStore>();
        var subCategories = Substitute.For<ISubCategoryStore>();
        var callCount = 0;
        categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 2) { reloadTcs.TrySetResult(); }
                return Task.FromResult<IReadOnlyList<Category>>(new List<Category>());
            });
        subCategories.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(new List<SubCategory>()));
        var vm = new CategoryManagerViewModel(categories, subCategories);
        await Task.Yield();
        vm.NewCategoryName = "  RenameTest  ";

        // ACT
        vm.CreateCategoryCommand.Execute(null);
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await categories.Received(1).CreateAsync(
            Arg.Is<Category>(c => c.Name == "RenameTest"),
            Arg.Any<CancellationToken>());
        // The constructor's ListAsync is call 1; the reload is call 2.
        await categories.Received(2).ListAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves the rename's first call only begins the edit: RenameName is
    /// set to the selected category's name, IsRenaming becomes true, and
    /// UpdateAsync is NOT called yet. The commit is the second call.
    /// </summary>
    [Fact]
    public async Task RenameFirstCallBeginsEditWithoutUpdate()
    {
        // ARRANGE
        var categories = Substitute.For<ICategoryStore>();
        var subCategories = Substitute.For<ISubCategoryStore>();
        var existing = MakeCategory("OriginalName");
        categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(new List<Category> { existing }));
        subCategories.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(new List<SubCategory>()));
        var vm = new CategoryManagerViewModel(categories, subCategories);
        await Task.Yield();
        vm.SelectedCategory = vm.Categories[0];

        // ACT — the begin branch has no await, so the state is set before Execute returns
        vm.RenameCategoryCommand.Execute(null);

        // ASSERT
        vm.IsRenaming.Should().BeTrue();
        vm.RenameName.Should().Be("OriginalName");
        await categories.DidNotReceive().UpdateAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves the rename's second call commits: UpdateAsync is called with
    /// the trimmed RenameName; IsRenaming and RenameName are cleared; the
    /// Changed event fires. The commit reloads Categories, so the selected
    /// category is re-found by Id after the reload.
    /// </summary>
    [Fact]
    public async Task RenameSecondCallCommitsAndRaisesChanged()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var categories = Substitute.For<ICategoryStore>();
        var subCategories = Substitute.For<ISubCategoryStore>();
        var existing = MakeCategory("OriginalName");
        var callCount = 0;
        categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 2) { reloadTcs.TrySetResult(); }
                return Task.FromResult<IReadOnlyList<Category>>(new List<Category> { existing });
            });
        subCategories.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(new List<SubCategory>()));
        var vm = new CategoryManagerViewModel(categories, subCategories);
        await Task.Yield();
        vm.SelectedCategory = vm.Categories[0];
        var changed = 0;
        vm.Changed += (_, _) => changed++;

        // ACT — first call begins (synchronous state), second commits and reloads
        vm.RenameCategoryCommand.Execute(null);
        vm.RenameName = "Renamed";
        vm.RenameCategoryCommand.Execute(null);
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await categories.Received(1).UpdateAsync(
            Arg.Is<Category>(c => c.Name == "Renamed" && c.Id == existing.Id),
            Arg.Any<CancellationToken>());
        vm.IsRenaming.Should().BeFalse();
        vm.RenameName.Should().BeNull();
        changed.Should().Be(1);
    }

    /// <summary>
    /// Proves CancelRename clears the in-progress rename state without a
    /// store call: RenameName null, IsRenaming false, UpdateAsync not called.
    /// </summary>
    [Fact]
    public async Task CancelRenameClearsStateWithoutStoreCall()
    {
        // ARRANGE
        var categories = Substitute.For<ICategoryStore>();
        var subCategories = Substitute.For<ISubCategoryStore>();
        var existing = MakeCategory("OriginalName");
        categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(new List<Category> { existing }));
        subCategories.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(new List<SubCategory>()));
        var vm = new CategoryManagerViewModel(categories, subCategories);
        await Task.Yield();
        vm.SelectedCategory = vm.Categories[0];
        vm.RenameCategoryCommand.Execute(null);

        // ACT
        vm.CancelRenameCommand.Execute(null);

        // ASSERT
        vm.IsRenaming.Should().BeFalse();
        vm.RenameName.Should().BeNull();
        await categories.DidNotReceive().UpdateAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves DeleteCategoryAsync calls ICategoryStore.DeleteAsync with the
    /// selected category's Id, clears SelectedCategory, and reloads. The
    /// cascade (F-257 — verified in the live DB) is the store's behavior,
    /// not the VM's; the VM's contract is the call and the reload.
    /// </summary>
    [Fact]
    public async Task DeleteCategoryCallsStoreAndClearsSelection()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var categories = Substitute.For<ICategoryStore>();
        var subCategories = Substitute.For<ISubCategoryStore>();
        var existing = MakeCategory("ToDelete");
        var callCount = 0;
        categories.ListAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 2) { reloadTcs.TrySetResult(); }
                return Task.FromResult<IReadOnlyList<Category>>(new List<Category> { existing });
            });
        subCategories.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(new List<SubCategory>()));
        var vm = new CategoryManagerViewModel(categories, subCategories);
        await Task.Yield();
        vm.SelectedCategory = vm.Categories[0];

        // ACT
        vm.DeleteCategoryCommand.Execute(null);
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await categories.Received(1).DeleteAsync(existing.Id, Arg.Any<CancellationToken>());
        vm.SelectedCategory.Should().BeNull();
    }

    /// <summary>
    /// Awaits the substitute's second ListAsync (the reload that follows the
    /// mutation). The VM's async command has no public ExecuteAsync (the
    /// non-generic AsyncRelayCommand's ICommand.Execute is void); the test
    /// invokes Execute(null) and awaits this TCS, which the substitute's
    /// ListAsync signals on its second call. Deterministic: no delay, no poll.
    /// The timeout is a diagnostic guard, not a synchronization primitive.
    /// </summary>
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
}