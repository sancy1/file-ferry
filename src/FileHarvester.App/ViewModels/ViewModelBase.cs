// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ViewModelBase.cs
// PURPOSE:  Base class for every view model in File-Ferry. Provides
//           INotifyPropertyChanged plus a SetProperty helper so subclasses
//           can raise change notifications in one line.
// RELATION: Inherited by MainWindowViewModel and every future view model
//           added in Phase 3.1 onward. No other dependencies.
// ============================================================================

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// Base class for view models. Implements INotifyPropertyChanged and
/// provides a SetProperty helper.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> and
    /// raises <see cref="PropertyChanged"/> if the value changed.
    /// </summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">
    /// Auto-populated by the compiler. Callers never pass this.
    /// </param>
    /// <returns><c>true</c> if the value changed; otherwise <c>false</c>.</returns>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises <see cref="PropertyChanged"/>. Callable from subclasses that
    /// need to notify on a computed property.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
