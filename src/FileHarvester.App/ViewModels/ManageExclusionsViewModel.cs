// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ManageExclusionsViewModel.cs
// PURPOSE:  View model for the modal Manage Exclusions dialog. Holds a working
//           copy of the global exclusion rules. Cancel discards changes; Save
//           returns the working set. Folder and file pickers are invoked by
//           the code-behind (views own Avalonia storage types).
// RELATION: Instantiated by CodeViewerPage.OnManageExclusionsClicked.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using CodeViewer.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// View model for the Manage Exclusions dialog.
/// </summary>
public sealed class ManageExclusionsViewModel : ViewModelBase
{
    private string _newName = string.Empty;
    private ExclusionKind _newKind = ExclusionKind.Folder;
    private string _statusText = string.Empty;

    /// <summary>Creates the dialog view model bound to the given current rules.</summary>
    public ManageExclusionsViewModel(GlobalExclusions initial)
    {
        ArgumentNullException.ThrowIfNull(initial);

        foreach (var rule in initial.Rules
                     .OrderBy(r => r.Kind)
                     .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            Rules.Add(rule);
        }

        AddManualCommand = new RelayCommand(AddManual, () => !string.IsNullOrWhiteSpace(_newName));
        RemoveCommand = new RelayCommand<ExclusionRule>(Remove, r => r is not null);
    }

    /// <summary>The working copy of the rules, live-bound to the list.</summary>
    public ObservableCollection<ExclusionRule> Rules { get; } = new();

    /// <summary>All available rule kinds for the kind selector.</summary>
    public IReadOnlyList<ExclusionKind> Kinds { get; } = new[] { ExclusionKind.Folder, ExclusionKind.File };

    /// <summary>Bound to the name TextBox in Section A.</summary>
    public string NewName
    {
        get => _newName;
        set
        {
            if (SetProperty(ref _newName, value))
            {
                (AddManualCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Bound to the kind selector in Section A.</summary>
    public ExclusionKind NewKind
    {
        get => _newKind;
        set => SetProperty(ref _newKind, value);
    }

    /// <summary>Transient status.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand AddManualCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Adds a name (used by picker code-behind).</summary>
    public bool TryAddFromPicker(string name, ExclusionKind kind)
    {
        if (string.IsNullOrWhiteSpace(name)) { return false; }
        ExclusionRule rule;
        try { rule = new ExclusionRule(name, kind); }
        catch (ArgumentException) { return false; }
        if (Rules.Contains(rule))
        {
            StatusText = $"'{rule.Name}' ({rule.Kind}) is already excluded.";
            return false;
        }
        Rules.Add(rule);
        StatusText = $"Added {rule.Kind} exclusion '{rule.Name}'.";
        return true;
    }

    /// <summary>Yields the working set as an immutable GlobalExclusions.</summary>
    public GlobalExclusions ToGlobalExclusions() => GlobalExclusions.FromRules(Rules);

    private void AddManual()
    {
        var name = _newName.Trim();
        if (name.Length == 0) { return; }
        try
        {
            var rule = new ExclusionRule(name, _newKind);
            if (Rules.Contains(rule))
            {
                StatusText = $"'{rule.Name}' ({rule.Kind}) is already excluded.";
                return;
            }
            Rules.Add(rule);
            StatusText = $"Added {rule.Kind} exclusion '{rule.Name}'.";
            _newName = string.Empty;
            OnPropertyChanged(nameof(NewName));
            (AddManualCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (ArgumentException ex)
        {
            StatusText = ex.Message;
        }
    }

    private void Remove(ExclusionRule? rule)
    {
        if (rule is null) { return; }
        if (Rules.Remove(rule))
        {
            StatusText = $"Removed {rule.Kind} exclusion '{rule.Name}'.";
        }
    }
}