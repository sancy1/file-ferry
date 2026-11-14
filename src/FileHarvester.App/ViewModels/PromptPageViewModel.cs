// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptPageViewModel.cs
// PURPOSE:  The Prompt App's page view model. Holds the sidebar state
//           (IsSidebarVisible, SidebarColumnWidth, the toggle command), the
//           two child VMs (List, Viewer), and the NewPromptCommand. At P2.3
//           the List loads the categories tree and prompt list; the Viewer
//           is a stub that receives the selection event.
// RELATION: Bound to MainWindowViewModel.Prompt and to PromptPage.axaml.
//           Constructed by MainWindowViewModel with the three stores.
// NOTES:    - The constructor takes IPromptStore, ICategoryStore, and
//             ISubCategoryStore. It creates List (PromptListViewModel),
//             Viewer (PromptViewerViewModel), wires List.PromptSelected to
//             Viewer.SetPrompt, and calls List.LoadAsync fire-and-forget
//             (LoadAsync has its own catch).
//           - SidebarColumnWidth is bound to the sidebar ColumnDefinition's
//             Width. It is 350 when IsSidebarVisible, 0 when hidden.
//           - NewPromptCommand is a no-op at P2.3 (the editor arrives at
//             P2.5). Clicking it sets List.StatusText to a message naming
//             the phase.
//           - The page's own StatusText was removed at P2.3; the sidebar
//             displays List.StatusText. A page-level StatusText returns at
//             P2.4 if the viewer's operations need one.
// ============================================================================

using Avalonia.Controls;
using System.Windows.Input;
using Prompt.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The Prompt App's page view model. Sidebar state, the two child VMs, and
/// placeholder commands at P2.3.
/// </summary>
public sealed class PromptPageViewModel : ViewModelBase
{
    private const double SidebarVisibleWidth = 350;

    private bool _isSidebarVisible = true;

    /// <summary>
    /// Creates the page view model. Takes the three stores; constructs the
    /// child VMs; wires the selection event.
    /// </summary>
    /// <param name="prompts">The prompt store. Never null.</param>
    /// <param name="categories">The category store. Never null.</param>
    /// <param name="subCategories">The sub-category store. Never null.</param>
    public PromptPageViewModel(
        IPromptStore prompts,
        ICategoryStore categories,
        ISubCategoryStore subCategories)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCategories);

        List = new PromptListViewModel(categories, subCategories, prompts);
        Viewer = new PromptViewerViewModel(prompts);

        List.PromptSelected += (_, prompt) => Viewer.SetPrompt(prompt, List);
        Viewer.DeleteSucceeded += (_, _) => _ = List.LoadAsync();
        List.ManageCategoriesRequested += OnManageCategoriesRequested;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarVisible = !IsSidebarVisible);
        NewPromptCommand = new RelayCommand(() => NewPromptRequested?.Invoke(this, EventArgs.Empty));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        TrashCommand = new RelayCommand(() => TrashRequested?.Invoke(this, EventArgs.Empty));
        FocusSearchCommand = new RelayCommand(() => FocusSearchRequested?.Invoke(this, EventArgs.Empty));

        _ = List.LoadAsync();
    }

    /// <summary>The sidebar's view model.</summary>
    public PromptListViewModel List { get; }

    /// <summary>The viewer's view model.</summary>
    public PromptViewerViewModel Viewer { get; }

    /// <summary>
    /// True when the sidebar is shown. Toggled by the open/close buttons.
    /// </summary>
    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set
        {
            if (SetProperty(ref _isSidebarVisible, value))
            {
                OnPropertyChanged(nameof(SidebarColumnWidth));
            }
        }
    }

    /// <summary>
    /// The sidebar column's width. 350 when visible, 0 when hidden. Bound to
    /// the sidebar ColumnDefinition's Width.
    /// </summary>
    public GridLength SidebarColumnWidth =>
        IsSidebarVisible ? new GridLength(SidebarVisibleWidth) : new GridLength(0);

    /// <summary>
    /// Toggles IsSidebarVisible. Called by the sidebar toggle buttons.
    /// </summary>
    public ICommand ToggleSidebarCommand { get; }

    /// <summary>
    /// Opens the editor to create a new prompt. No-op at P2.3 (the editor
    /// arrives at P2.5). Sets List.StatusText as feedback.
    /// </summary>
    /// <summary>Raised when + New Prompt is clicked. The page opens the editor.</summary>
    public event EventHandler? NewPromptRequested;

    public ICommand NewPromptCommand { get; }

    /// <summary>Raised when + Import is clicked. The page opens the import dialog.</summary>
    public event EventHandler? ImportRequested;

    public ICommand ImportCommand { get; }

    /// <summary>Raised when Trash is clicked. The page opens the trash dialog.</summary>
    public event EventHandler? TrashRequested;

    public ICommand TrashCommand { get; }
    /// <summary>
    /// Raised when the sidebar's Manage button is clicked. The page's
    /// code-behind opens the CategoryManagerDialog.
    /// </summary>
    public event EventHandler? ManageCategoriesDialogRequested;

    /// <summary>
    /// Raised when Ctrl+F is pressed. The page's code-behind focuses the
    /// search TextBox. Routed through a command so the shortcut is testable.
    /// </summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Raises <see cref="FocusSearchRequested"/> when Ctrl+F is pressed.</summary>
    public ICommand FocusSearchCommand { get; }

    private void OnManageCategoriesRequested(object? sender, EventArgs e)
    {
        ManageCategoriesDialogRequested?.Invoke(this, EventArgs.Empty);
    }
}