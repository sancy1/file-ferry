// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/IUiDispatcher.cs
// PURPOSE:  Abstracts UI-thread marshalling for view models. Production
//           uses the real Avalonia dispatcher; tests use an inline
//           implementation that runs actions immediately, so async view-
//           model methods can be awaited without a running dispatcher.
// RELATION: Consumed by ProjectTabViewModel. The default is
//           AvaloniaUiDispatcher; tests pass InlineUiDispatcher.
// ============================================================================

using Avalonia.Threading;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// Marshals an action onto the UI thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Runs the action on the UI thread and completes when done.</summary>
    System.Threading.Tasks.Task RunAsync(Action action);
}

/// <summary>
/// Production dispatcher: marshals to the Avalonia UI thread, or runs
/// inline when already on it.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async System.Threading.Tasks.Task RunAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}

/// <summary>
/// Test dispatcher: runs the action immediately on the calling thread.
/// </summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public System.Threading.Tasks.Task RunAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
