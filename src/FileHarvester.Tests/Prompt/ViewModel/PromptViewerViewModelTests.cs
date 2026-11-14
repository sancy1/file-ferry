// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/ViewModel/PromptViewerViewModelTests.cs
// PURPOSE:  Guards PromptViewerViewModel's observable behavior: SetPrompt
//           populates the display fields; Clear resets to the empty state;
//           CopyCommand raises CopyRequested with the body; EditCommand
//           raises EditRequested with the prompt; DeleteCommand calls
//           SoftDeleteAsync and raises DeleteSucceeded; FavoriteCommand
//           calls PatchAsync and flips IsFavorite.
// RELATION: Added at the P2.7.x ViewModel test backfill (Path 3). Consumes
//           PromptViewerViewModel (FileHarvester.App.ViewModels); substitutes
//           IPromptStore via NSubstitute. Fact 1 constructs a real
//           PromptListViewModel (sealed, not substitutable) for the
//           SetPrompt's category-label lookup; the list's stores are
//           substitutes so no I/O runs.
// NOTES:   - The viewer's commands are AsyncRelayCommand (void Execute) for
//             Delete and Favorite, and RelayCommand (void Execute) for Copy
//             and Edit. The tests invoke Execute(null) and wait via a
//             TaskCompletionSource that the substitute's awaited method sets.
//           - Fact 1's SelectedCategoryLabel is "Uncategorized" because the
//             prompt has null CategoryId and the list's Categories collection
//             is empty (no LoadAsync called).
//           - Fact 6 asserts PatchAsync was called once with Arg.Any<
//             PromptPatch>(); the precise patch-field inspection is a
//             separate concern (the PatchField<T> shape is the store's
//             contract, not the VM's).
//           - The Prompt record shape is (Id, Title, Body, Format, CategoryId,
//             SubCategoryId, Description, IsFavorite, IsDeleted, Tags,
//             CreatedAt, UpdatedAt). Verified by the CS7036 error from the
//             P2.7.x build of PromptListViewModelTests.
// ============================================================================

using FileHarvester.App.ViewModels;
using FluentAssertions;
using NSubstitute;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.ViewModel;

/// <summary>
/// Guards PromptViewerViewModel's observable behavior. Six facts: SetPrompt
/// populates, Clear resets, Copy raises, Edit raises, Delete soft-deletes and
/// raises, Favorite patches and flips.
/// </summary>
public sealed class PromptViewerViewModelTests
{
    private static PromptRecord MakePrompt(Guid? categoryId, Guid? subCategoryId, string title, bool isFavorite = false) =>
        new(Guid.NewGuid(), title, "Body of " + title, BodyFormat.PlainText,
            categoryId, subCategoryId, null,
            isFavorite, false, Array.Empty<string>(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static PromptListViewModel MakeEmptyList()
    {
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var prompts = Substitute.For<IPromptStore>();
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>()));
        return new PromptListViewModel(cats, subs, prompts);
    }

    /// <summary>
    /// Proves SetPrompt populates SelectedPrompt, SelectedTitle,
    /// SelectedBody, HasSelection, and IsFavorite; SelectedCategoryLabel is
    /// "Uncategorized" for a null-category prompt.
    /// </summary>
    [Fact]
    public void SetPromptPopulatesDisplayFields()
    {
        // ARRANGE
        var prompts = Substitute.For<IPromptStore>();
        var vm = new PromptViewerViewModel(prompts);
        var list = MakeEmptyList();
        var prompt = MakePrompt(null, null, "Hello", isFavorite: true);
        vm.HasSelection.Should().BeFalse();

        // ACT
        vm.SetPrompt(prompt, list);

        // ASSERT
        vm.SelectedPrompt.Should().Be(prompt);
        vm.SelectedTitle.Should().Be("Hello");
        vm.SelectedBody.Should().Be("Body of Hello");
        vm.HasSelection.Should().BeTrue();
        vm.IsFavorite.Should().BeTrue();
        vm.SelectedCategoryLabel.Should().Be("Uncategorized");
    }

    /// <summary>
    /// Proves Clear resets the display fields to the empty state:
    /// SelectedPrompt null, SelectedTitle/SelectedBody null, HasSelection
    /// false, IsFavorite false, SelectedCategoryLabel null.
    /// </summary>
    [Fact]
    public void ClearResetsToEmptyState()
    {
        // ARRANGE
        var prompts = Substitute.For<IPromptStore>();
        var vm = new PromptViewerViewModel(prompts);
        var list = MakeEmptyList();
        vm.SetPrompt(MakePrompt(null, null, "Hello", isFavorite: true), list);

        // ACT
        vm.Clear();

        // ASSERT
        vm.SelectedPrompt.Should().BeNull();
        vm.SelectedTitle.Should().BeNull();
        vm.SelectedBody.Should().BeNull();
        vm.HasSelection.Should().BeFalse();
        vm.IsFavorite.Should().BeFalse();
        vm.SelectedCategoryLabel.Should().BeNull();
    }

    /// <summary>
    /// Proves CopyCommand raises CopyRequested with the selected body.
    /// </summary>
    [Fact]
    public void CopyCommandRaisesCopyRequestedWithBody()
    {
        // ARRANGE
        var prompts = Substitute.For<IPromptStore>();
        var vm = new PromptViewerViewModel(prompts);
        var list = MakeEmptyList();
        vm.SetPrompt(MakePrompt(null, null, "Copy me"), list);
        string? captured = null;
        vm.CopyRequested += (_, body) => captured = body;

        // ACT
        vm.CopyCommand.Execute(null);

        // ASSERT
        captured.Should().Be("Body of Copy me");
    }

    /// <summary>
    /// Proves EditCommand raises EditRequested with the selected prompt.
    /// </summary>
    [Fact]
    public void EditCommandRaisesEditRequestedWithPrompt()
    {
        // ARRANGE
        var prompts = Substitute.For<IPromptStore>();
        var vm = new PromptViewerViewModel(prompts);
        var list = MakeEmptyList();
        var prompt = MakePrompt(null, null, "Edit me");
        vm.SetPrompt(prompt, list);
        PromptRecord? captured = null;
        vm.EditRequested += (_, p) => captured = p;

        // ACT
        vm.EditCommand.Execute(null);

        // ASSERT
        captured.Should().Be(prompt);
    }

    /// <summary>
    /// Proves DeleteCommand calls SoftDeleteAsync with the selected prompt's
    /// Id, clears the viewer, and raises DeleteSucceeded. The
    /// AsyncRelayCommand's Execute is void; the substitute's
    /// SoftDeleteAsync signals a TCS the test awaits.
    /// </summary>
    [Fact]
    public async Task DeleteCommandCallsSoftDeleteAndRaisesDeleteSucceeded()
    {
        // ARRANGE
        var prompts = Substitute.For<IPromptStore>();
        var deleteTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.SoftDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleteTcs.TrySetResult();
                return Task.CompletedTask;
            });
        var vm = new PromptViewerViewModel(prompts);
        var list = MakeEmptyList();
        var prompt = MakePrompt(null, null, "Delete me");
        vm.SetPrompt(prompt, list);
        var deleteSucceededFired = 0;
        vm.DeleteSucceeded += (_, _) => deleteSucceededFired++;

        // ACT
        vm.DeleteCommand.Execute(null);
        await deleteTcs.Task;
        await Task.Yield();

        // ASSERT
        await prompts.Received(1).SoftDeleteAsync(prompt.Id, Arg.Any<CancellationToken>());
        vm.HasSelection.Should().BeFalse();
        vm.SelectedPrompt.Should().BeNull();
        deleteSucceededFired.Should().Be(1);
    }

    /// <summary>
    /// Proves FavoriteCommand calls PatchAsync with the selected prompt's Id
    /// and flips IsFavorite. The substitute's PatchAsync signals a TCS the
    /// test awaits; the patch's exact field shape is asserted only as
    /// Arg.Any<PromptPatch>() — the wire-level shape is the store's contract.
    /// </summary>
    [Fact]
    public async Task FavoriteCommandPatchesIsFavorite()
    {
        // ARRANGE
        var prompts = Substitute.For<IPromptStore>();
        var patchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.PatchAsync(Arg.Any<Guid>(), Arg.Any<PromptPatch>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                patchTcs.TrySetResult();
                return Task.FromResult(MakePrompt(null, null, "Patched"));
            });
        var vm = new PromptViewerViewModel(prompts);
        var list = MakeEmptyList();
        var prompt = MakePrompt(null, null, "Fav me", isFavorite: false);
        vm.SetPrompt(prompt, list);
        vm.IsFavorite.Should().BeFalse();

        // ACT
        vm.FavoriteCommand.Execute(null);
        await patchTcs.Task;
        await Task.Yield();

        // ASSERT
        await prompts.Received(1).PatchAsync(prompt.Id, Arg.Any<PromptPatch>(), Arg.Any<CancellationToken>());
        vm.IsFavorite.Should().BeTrue();
    }
}