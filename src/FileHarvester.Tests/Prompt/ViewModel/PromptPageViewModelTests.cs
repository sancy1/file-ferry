// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/ViewModel/PromptPageViewModelTests.cs
// PURPOSE:  Guards PromptPageViewModel's observable behavior: the constructor
//           builds List and Viewer from the three stores; List.PromptSelected
//           calls Viewer.SetPrompt; Viewer.DeleteSucceeded triggers List's
//           reload; List.ManageCategoriesRequested raises the page's dialog
//           event; NewPromptCommand raises NewPromptRequested;
//           ToggleSidebarCommand flips IsSidebarVisible and the width.
// RELATION: Added at the P2.7.x ViewModel test backfill (Path 3). Consumes
//           PromptPageViewModel, PromptListViewModel, PromptViewerViewModel
//           (FileHarvester.App.ViewModels); substitutes the three stores via
//           NSubstitute. No DB, no UI, no file I/O.
// NOTES:   - The page constructor calls _ = List.LoadAsync() fire-and-forget.
//             Only the fact that depends on the reload (the DeleteSucceeded
//             reload) waits via the WaitReloadAsync TCS; the other facts
//             assert construction, events, or synchronous state.
//           - List and Viewer are constructed by the page, not injected.
//             The tests observe them through the page's public surface.
//             Only the stores are substituted.
//           - The Prompt record shape is (Id, Title, Body, Format, CategoryId,
//             SubCategoryId, Description, IsFavorite, IsDeleted, Tags,
//             CreatedAt, UpdatedAt). Verified by the CS7036 error from the
//             P2.7.x build of PromptListViewModelTests.
//           - AsyncRelayCommand's ICommand.Execute is void; the tests invoke
//             Execute(null) and assert on the effect, not on a task.
// ============================================================================

using FileHarvester.App.ViewModels;
using FluentAssertions;
using NSubstitute;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.ViewModel;

/// <summary>
/// Guards PromptPageViewModel's observable behavior. Six facts: construction,
/// PromptSelected wiring, DeleteSucceeded reload, ManageCategories event,
/// NewPrompt event, sidebar toggle.
/// </summary>
public sealed class PromptPageViewModelTests
{
    private static Category MakeCategory(string name, int sortOrder = 0) =>
        new(Guid.NewGuid(), name, null, sortOrder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static PromptRecord MakePrompt(Guid? categoryId, Guid? subCategoryId, string title) =>
        new(Guid.NewGuid(), title, "Body of " + title, BodyFormat.PlainText,
            categoryId, subCategoryId, null,
            false, false, Array.Empty<string>(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static (IPromptStore prompts, ICategoryStore cats, ISubCategoryStore subs)
        MakeStores(IReadOnlyList<Category> categories, IReadOnlyList<SubCategory> subs, IReadOnlyList<PromptRecord> prompts)
    {
        var promptsStore = Substitute.For<IPromptStore>();
        promptsStore.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(prompts));
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(categories));
        var subsStore = Substitute.For<ISubCategoryStore>();
        subsStore.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(subs));
        return (promptsStore, cats, subsStore);
    }

    /// <summary>
    /// Proves the constructor builds List and Viewer from the three stores.
    /// Both are non-null, distinct instances.
    /// </summary>
    [Fact]
    public void ConstructorConstructsListAndViewerFromStores()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());

        // ACT
        var page = new PromptPageViewModel(prompts, cats, subs);

        // ASSERT
        page.List.Should().NotBeNull();
        page.Viewer.Should().NotBeNull();
        page.List.Should().NotBeSameAs(page.Viewer);
    }

    /// <summary>
    /// Proves List.PromptSelected calls Viewer.SetPrompt: the viewer's
    /// SelectedTitle, SelectedBody, HasSelection, and IsFavorite reflect the
    /// raised prompt.
    /// </summary>
    [Fact]
    public void ListPromptSelectedCallsViewerSetPrompt()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var page = new PromptPageViewModel(prompts, cats, subs);
        var prompt = MakePrompt(null, null, "Wired prompt");
        page.Viewer.HasSelection.Should().BeFalse();

        // ACT — the page's constructor subscribed to List.PromptSelected; raise it
        var listType = page.List.GetType();
        var promptSelectedField = listType.GetField("PromptSelected",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        promptSelectedField.Should().NotBeNull();
        var handler = (EventHandler<PromptRecord>?)promptSelectedField!.GetValue(page.List);
        handler.Should().NotBeNull();
        handler!.Invoke(page.List, prompt);

        // ASSERT
        page.Viewer.SelectedPrompt.Should().Be(prompt);
        page.Viewer.SelectedTitle.Should().Be("Wired prompt");
        page.Viewer.SelectedBody.Should().Be("Body of Wired prompt");
        page.Viewer.HasSelection.Should().BeTrue();
    }

    /// <summary>
    /// Proves Viewer.DeleteSucceeded triggers List's reload: the category
    /// store's ListAsync is called again after the event fires. The page's
    /// handler is _ = List.LoadAsync() (fire-and-forget); the WaitReloadAsync
    /// TCS signals on the SearchAsync's second call.
    /// </summary>
    [Fact]
    public async Task ViewerDeleteSucceededTriggersListReload()
    {
        // ARRANGE
        var reloadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = Substitute.For<IPromptStore>();
        var searchCount = 0;
        prompts.SearchAsync(Arg.Any<PromptQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref searchCount) >= 2) { reloadTcs.TrySetResult(); }
                return Task.FromResult<IReadOnlyList<PromptRecord>>(Array.Empty<PromptRecord>());
            });
        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));
        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));
        var page = new PromptPageViewModel(prompts, cats, subs);

        // ACT — raise the viewer's DeleteSucceeded; the page's handler reloads List
        var viewerType = page.Viewer.GetType();
        var deleteSucceededField = viewerType.GetField("DeleteSucceeded",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        deleteSucceededField.Should().NotBeNull();
        var handler = (EventHandler?)deleteSucceededField!.GetValue(page.Viewer);
        handler.Should().NotBeNull();
        handler!.Invoke(page.Viewer, EventArgs.Empty);
        await WaitReloadAsync(reloadTcs);

        // ASSERT
        await cats.Received(2).ListAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves List.ManageCategoriesRequested raises the page's
    /// ManageCategoriesDialogRequested. The page's handler is the synchronous
    /// OnManageCategoriesRequested.
    /// </summary>
    [Fact]
    public void ListManageCategoriesRequestedRaisesDialogEvent()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var page = new PromptPageViewModel(prompts, cats, subs);
        var raised = 0;
        page.ManageCategoriesDialogRequested += (_, _) => raised++;

        // ACT — raise the list's ManageCategoriesRequested
        var listType = page.List.GetType();
        var eventField = listType.GetField("ManageCategoriesRequested",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        eventField.Should().NotBeNull();
        var handler = (EventHandler?)eventField!.GetValue(page.List);
        handler.Should().NotBeNull();
        handler!.Invoke(page.List, EventArgs.Empty);

        // ASSERT
        raised.Should().Be(1);
    }

    /// <summary>
    /// Proves NewPromptCommand raises NewPromptRequested. The command is a
    /// synchronous RelayCommand; Execute(null) is enough.
    /// </summary>
    [Fact]
    public void NewPromptCommandRaisesNewPromptRequested()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var page = new PromptPageViewModel(prompts, cats, subs);
        var raised = 0;
        page.NewPromptRequested += (_, _) => raised++;

        // ACT
        page.NewPromptCommand.Execute(null);

        // ASSERT
        raised.Should().Be(1);
    }

    /// <summary>
    /// Proves ToggleSidebarCommand flips IsSidebarVisible and the
    /// SidebarColumnWidth (350 visible, 0 hidden).
    /// </summary>
    [Fact]
    public void ToggleSidebarCommandFlipsSidebarState()
    {
        // ARRANGE
        var (prompts, cats, subs) = MakeStores(
            Array.Empty<Category>(), Array.Empty<SubCategory>(), Array.Empty<PromptRecord>());
        var page = new PromptPageViewModel(prompts, cats, subs);
        page.IsSidebarVisible.Should().BeTrue();
        page.SidebarColumnWidth.Value.Should().Be(350);

        // ACT + ASSERT
        page.ToggleSidebarCommand.Execute(null);
        page.IsSidebarVisible.Should().BeFalse();
        page.SidebarColumnWidth.Value.Should().Be(0);

        page.ToggleSidebarCommand.Execute(null);
        page.IsSidebarVisible.Should().BeTrue();
        page.SidebarColumnWidth.Value.Should().Be(350);
    }

    /// <summary>
    /// Awaits the substitute's reload signal. Same helper as the prior test
    /// files. Deterministic; the timeout is a diagnostic guard.
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