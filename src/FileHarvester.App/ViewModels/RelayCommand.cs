// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/RelayCommand.cs
// PURPOSE:  Standard MVVM ICommand implementations. Wraps an Action and
//           an optional can-execute predicate. Used by every view model
//           that exposes commands bound to XAML Button.Click.
// RELATION: Consumed by InputPanelViewModel and every future view model
//           with commands. No other dependencies.
// ============================================================================

using System.Windows.Input;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// An ICommand that invokes a parameterless Action.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Creates a command with the given execute action and optional
    /// can-execute predicate.
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Raises CanExecuteChanged so bound buttons re-query their state.
    /// </summary>
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// An ICommand that invokes an Action with a typed parameter.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    /// <summary>
    /// Creates a command with the given execute action and optional
    /// can-execute predicate.
    /// </summary>
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(Cast(parameter)) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute(Cast(parameter));

    /// <summary>
    /// Raises CanExecuteChanged so bound buttons re-query their state.
    /// </summary>
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Cast(object? parameter) =>
        parameter is T typed ? typed : default;
}
