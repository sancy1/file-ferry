// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/ViewModel/PromptEditorViewModelTests.cs
// PURPOSE:  Guards PromptEditorViewModel's observable behavior: constructor
//           create mode initializes empty; constructor edit mode populates
//           from the existing prompt; CanSave gating on Title and Body;
//           SaveCommand calls CreateAsync in create mode with parsed tags;
//           SaveCommand calls UpdateAsync in edit mode (PUT); CancelCommand
//           raises Cancelled.
// RELATION: Added at the P2.7.x ViewModel test backfill (Path 3). Consumes
//           PromptEditorViewModel (FileHarvester.App.ViewModels); substitutes
//           IPromptStore, ICategoryStore, ISubCategoryStore via NSubstitute.
//           No DB, no UI, no file I/O.
// NOTES:   - The constructor fires _ = LoadCategoriesAsync() fire-and-forget.
//             None of the asserted state depends on the load (IsNew,
//             DialogTitle, Title, Body, CanSave are set synchronously). The
//             tests assert without waiting for the load.
//           - CreateAsync and UpdateAsync return Task<Prompt> (non-nullable);
//             the substitutes return Task.FromResult(...) with a fresh
//             Prompt.
//           - No ConfigureAwait(false) in the test methods (xUnit1030). The
//             TCS await is plain `await tcs.Task;` with a diagnostic
//             `await Task.Delay(50)` to let the continuation after the
//             CreateAsync / UpdateAsync await run before the Saved assertion.
//           - Facts 4 and 5 leave SelectedCategory null; the save path is
//             still exercised, and the created/updated prompt's
//             CategoryId/SubCategoryId are null. TagsText is set in fact 4
//             to prove ParseTags: "a, b, a" -> ["a", "b"].
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
/// Guards PromptEditorViewModel's observable behavior. Six facts: create-mode
/// init, edit-mode populate, CanSave gating, CreateAsync routing, UpdateAsync
/// routing, Cancel event.
/// </summary>
public sealed class PromptEditorViewModelTests
{
    private static PromptRecord MakePrompt(Guid? categoryId, Guid? subCategoryId, string title, bool isFavorite = false, bool isDeleted = false) =>
        new(Guid.NewGuid(), title, "Body of " + title, BodyFormat.PlainText,
            categoryId, subCategoryId, null,
            isFavorite, isDeleted, Array.Empty<string>(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static (IPromptStore prompts, ICategoryStore cats, ISubCategoryStore subs) MakeStores()
    {
        var prompts = Substitute.For<IPromptStore>();
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        return (prompts, cats, subs);
    }

    /// <summary>
    /// Proves the constructor's create mode: IsNew true, DialogTitle
    /// "Create Prompt", empty Title/Body, CanSave false.
    /// </summary>
    [Fact]
    public void ConstructorCreateModeInitializesEmpty()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();

        // ACT
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);

        // ASSERT
        vm.IsNew.Should().BeTrue();
        vm.DialogTitle.Should().Be("Create Prompt");
        vm.Title.Should().BeEmpty();
        vm.Body.Should().BeEmpty();
        vm.CanSave.Should().BeFalse();
    }

    /// <summary>
    /// Proves the constructor's edit mode: IsNew false, DialogTitle
    /// "Edit Prompt", Title/Body populated from the existing prompt,
    /// CanSave true.
    /// </summary>
    [Fact]
    public void ConstructorEditModePopulatesFromExisting()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var existing = MakePrompt(null, null, "Original");

        // ACT
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing);

        // ASSERT
        vm.IsNew.Should().BeFalse();
        vm.DialogTitle.Should().Be("Edit Prompt");
        vm.Title.Should().Be("Original");
        vm.Body.Should().Be("Body of Original");
        vm.CanSave.Should().BeTrue();
    }

    /// <summary>
    /// Proves CanSave gating: false when either Title or Body is empty;
    /// true when both are non-empty.
    /// </summary>
    [Fact]
    public void CanSaveReflectsTitleAndBody()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);

        // ACT + ASSERT — title only
        vm.Title = "x";
        vm.Body = "";
        vm.CanSave.Should().BeFalse();

        // ACT + ASSERT — body only
        vm.Title = "";
        vm.Body = "y";
        vm.CanSave.Should().BeFalse();

        // ACT + ASSERT — both
        vm.Title = "x";
        vm.Body = "y";
        vm.CanSave.Should().BeTrue();
    }

    /// <summary>
    /// Proves SaveCommand in create mode calls CreateAsync once with the
    /// trimmed title, the body, and the parsed tags ("a, b, a" -> ["a", "b"]),
    /// and raises Saved with the returned prompt. The CreateAsync TCS signals
    /// the substitute's completion; the diagnostic Task.Delay lets the
    /// continuation after the await run.
    /// </summary>
    [Fact]
    public async Task SaveCommandCallsCreateAsyncInCreateMode()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var createTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = MakePrompt(null, null, "New prompt");
        prompts.CreateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                createTcs.TrySetResult();
                return Task.FromResult(created);
            });
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);
        vm.Title = "  New prompt  ";
        vm.Body = "Body of new";
        vm.TagsText = "a, b, a";
        PromptRecord? saved = null;
        vm.Saved += (_, p) => saved = p;

        // ACT
        vm.SaveCommand.Execute(null);
        await createTcs.Task;
        await Task.Delay(50);

        // ASSERT
        await prompts.Received(1).CreateAsync(
            Arg.Is<PromptRecord>(p =>
                p.Title == "New prompt" &&
                p.Body == "Body of new" &&
                p.Tags.Count == 2 &&
                p.Tags.Contains("a") &&
                p.Tags.Contains("b")),
            Arg.Any<CancellationToken>());
        saved.Should().NotBeNull();
        saved!.Id.Should().Be(created.Id);
    }

    /// <summary>
    /// Proves SaveCommand in edit mode calls UpdateAsync once with the
    /// updated prompt (PUT — new Title, new UpdatedAt) and raises Saved with
    /// the returned prompt.
    /// </summary>
    [Fact]
    public async Task SaveCommandCallsUpdateAsyncInEditMode()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var existing = MakePrompt(null, null, "Original");
        var updateTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = existing with { Title = "Renamed" };
        prompts.UpdateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                updateTcs.TrySetResult();
                return Task.FromResult(updated);
            });
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing);
        vm.Title = "Renamed";
        PromptRecord? saved = null;
        vm.Saved += (_, p) => saved = p;

        // ACT
        vm.SaveCommand.Execute(null);
        await updateTcs.Task;
        await Task.Delay(50);

        // ASSERT
        await prompts.Received(1).UpdateAsync(
            Arg.Is<PromptRecord>(p => p.Id == existing.Id && p.Title == "Renamed"),
            Arg.Any<CancellationToken>());
        saved.Should().NotBeNull();
        saved!.Title.Should().Be("Renamed");
    }

    /// <summary>
    /// Proves CancelCommand raises Cancelled exactly once. The command is a
    /// synchronous RelayCommand.
    /// </summary>
    [Fact]
    public void CancelCommandRaisesCancelled()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);
        var raised = 0;
        vm.Cancelled += (_, _) => raised++;

        // ACT
        vm.CancelCommand.Execute(null);

        // ASSERT
        raised.Should().Be(1);
    }
    /// <summary>
    /// Proves the editor's IsFavorite flag reaches CreateAsync (F-269): a
    /// new prompt with IsFavorite = true is created with IsFavorite = true.
    /// </summary>
    [Fact]
    public async Task CreatePreservesFavoriteFromVM()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var createTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.CreateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ => { createTcs.TrySetResult(); return Task.FromResult(MakePrompt(null, null, "New")); });
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);
        vm.Title = "New";
        vm.Body = "Body";
        vm.IsFavorite = true;

        // ACT
        vm.SaveCommand.Execute(null);
        await createTcs.Task;
        await Task.Delay(50);

        // ASSERT
        await prompts.Received(1).CreateAsync(
            Arg.Is<PromptRecord>(p => p.IsFavorite == true),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves the editor's IsFavorite flag is preserved on edit (F-269): an
    /// existing favorited prompt stays favorited after a save.
    /// </summary>
    [Fact]
    public async Task EditPreservesFavorite()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var existing = MakePrompt(null, null, "Original", isFavorite: true);
        var updateTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.UpdateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ => { updateTcs.TrySetResult(); return Task.FromResult(existing); });
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing);
        vm.Title = "Renamed";

        // ACT
        vm.SaveCommand.Execute(null);
        await updateTcs.Task;
        await Task.Delay(50);

        // ASSERT
        await prompts.Received(1).UpdateAsync(
            Arg.Is<PromptRecord>(p => p.IsFavorite == true && p.Title == "Renamed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditorSavesDraftAfterDebounce()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);

        // ACT
        vm.Title = "Draft title";
        vm.Body = "Draft body";
        await Task.Delay(2100);

        // ASSERT
        await prompts.Received(1).SaveDraftAsync(
            Arg.Any<PromptDraft>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditorClearsDraftOnSave()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var createTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        prompts.CreateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ => { createTcs.TrySetResult(); return Task.FromResult(MakePrompt(null, null, "Draft clear")); });
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);
        vm.Title = "Draft clear";
        vm.Body = "body";

        // ACT
        vm.SaveCommand.Execute(null);
        await createTcs.Task;
        await Task.Delay(50);

        // ASSERT
        await prompts.Received(1).ClearDraftAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditorLoadsDraftOnOpen()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores();
        var draft = new PromptDraft(Guid.NewGuid(), "Draft title", "Draft body", DateTimeOffset.UtcNow);
        prompts.GetDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PromptDraft?>(draft));

        // ACT
        var vm = new PromptEditorViewModel(prompts, cats, subs, existing: null);
        await Task.Yield();

        // ASSERT
        vm.Title.Should().Be("Draft title");
        vm.Body.Should().Be("Draft body");
    }
}