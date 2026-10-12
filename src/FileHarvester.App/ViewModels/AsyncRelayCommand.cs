// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/AsyncRelayCommand.cs
// PURPOSE:  Async ICommand implementations. The non-generic command
//           awaits a Func<Task> for view models whose command must
//           await an operation. The generic command owns its execution
//           lifecycle: an atomic running guard, an awaitable ExecutionTask,
//           dispatcher-safe CanExecuteChanged, and inspectable failure
//           diagnostics for the reporting callback and the dispatcher.
// RELATION: AsyncRelayCommand is consumed by CloudCredentialFormViewModel
//           .SaveCommand. AsyncRelayCommand<T> is consumed by
//           CloudPageViewModel.ConnectCommand and DisconnectCommand.
//           Siblings of RelayCommand and RelayCommand<T>.
// ============================================================================

using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// An ICommand that invokes an async Func. The execute delegate is
/// kicked off on the calling context; the returned task is awaited
/// so its exceptions are observed, not swallowed.
/// </summary>
/// <remarks>
/// The command does not hold a running state, does not re-query
/// CanExecute on completion, and does not support cancellation.
/// A caller that needs any of those adds them when the need exists.
/// The execute delegate is responsible for its own error handling.
/// </remarks>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Creates a command with the given async execute delegate and
    /// optional can-execute predicate.
    /// </summary>
    /// <param name="execute">The async delegate. Not null.</param>
    /// <param name="canExecute">The optional can-execute predicate.</param>
    /// <exception cref="ArgumentNullException">
    /// When <paramref name="execute"/> is null.
    /// </exception>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        // Fire-and-forget on the calling context. ExecuteAsync awaits
        // the delegate so a faulted task is observed, not silently dropped.
        _ = ExecuteAsync();
    }

    /// <summary>
    /// Raises CanExecuteChanged so bound buttons re-query their state.
    /// </summary>
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private async Task ExecuteAsync()
    {
        await _execute().ConfigureAwait(true);
    }
}

/// <summary>
/// A typed asynchronous ICommand that prevents concurrent execution,
/// exposes its current execution task, and reports unexpected failures
/// through a caller-supplied callback.
/// </summary>
/// <remarks>
/// <para>
/// Ownership rules:
/// </para>
/// <list type="bullet">
/// <item><description>The command owns the current execution task.</description></item>
/// <item><description>ExecuteCoreAsync awaits the user operation and handles its faults.</description></item>
/// <item><description>ExecutionTask may be awaited by tests or a future lifecycle owner.</description></item>
/// <item><description>The command does not claim to drain work during application shutdown.</description></item>
/// </list>
/// </remarks>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onUnexpectedFailure;

    private int _running;
    private Task _executionTask = Task.CompletedTask;

    private Exception? _lastReportingFailure;
    private Exception? _lastNotificationFailure;

    /// <summary>
    /// Creates the command.
    /// </summary>
    /// <param name="execute">The async operation. Not null.</param>
    /// <param name="canExecute">The optional can-execute predicate.</param>
    /// <param name="onUnexpectedFailure">
    /// The optional failure reporter. It must not throw; a throwing
    /// reporter is contained and recorded in LastReportingFailure.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// When <paramref name="execute"/> is null.
    /// </exception>
    public AsyncRelayCommand(
        Func<T?, Task> execute,
        Func<T?, bool>? canExecute = null,
        Action<Exception>? onUnexpectedFailure = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onUnexpectedFailure = onUnexpectedFailure;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <summary>True while an execution is active.</summary>
    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// The current execution task. A test or future lifecycle owner may
    /// await this task. It represents the command's full execution,
    /// including its fault reporting and notification cleanup.
    /// </summary>
    public Task ExecutionTask => _executionTask;

    /// <summary>
    /// The last exception thrown by the failure-reporting callback, if any.
    /// This makes a callback failure inspectable rather than silently lost.
    /// </summary>
    public Exception? LastReportingFailure => _lastReportingFailure;

    /// <summary>
    /// The last exception thrown while attempting to raise
    /// CanExecuteChanged on the Avalonia dispatcher, if any.
    /// </summary>
    public Exception? LastNotificationFailure => _lastNotificationFailure;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter)
    {
        return !IsRunning && (_canExecute?.Invoke(Cast(parameter)) ?? true);
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        // Atomically acquire the running guard before starting work.
        // If another execution is active, this invocation is ignored.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        // Start the command-owned execution. ExecutionTask is exposed so
        // tests or a future lifecycle owner can await completion.
        _executionTask = ExecuteCoreAsync(Cast(parameter));
    }

    private async Task ExecuteCoreAsync(T? parameter)
    {
        try
        {
            RaiseCanExecuteChangedOnDispatcher();

            try
            {
                await _execute(parameter).ConfigureAwait(false);
            }
            catch (Exception operationFailure)
            {
                ReportUnexpectedFailure(operationFailure);
            }
        }
        catch (Exception boundaryFailure)
        {
            // A failure outside the operation boundary - for example,
            // in the pre-execution notification path. Routed through the
            // same reporter so the command's safety contract is explicit
            // and does not depend on the notification method's own catch.
            ReportUnexpectedFailure(boundaryFailure);
        }
        finally
        {
            // Release the guard before attempting UI notification.
            // The command must never remain busy because a notification failed.
            Volatile.Write(ref _running, 0);

            RaiseCanExecuteChangedOnDispatcher();
        }
    }

    private void ReportUnexpectedFailure(Exception failure)
    {
        try
        {
            _onUnexpectedFailure?.Invoke(failure);
        }
        catch (Exception reportingFailure)
        {
            // The reporting callback is caller-supplied and must not throw.
            // If it does, retain the failure in an inspectable field rather
            // than silently discarding it or allowing it to fault the task.
            _lastReportingFailure = reportingFailure;
        }
    }

    private void RaiseCanExecuteChangedOnDispatcher()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Post without awaiting. A non-UI host may have no
            // dispatcher pump; awaiting InvokeAsync there would
            // never complete and would hang the command task.
            // Post queues the notification and returns immediately.
            Dispatcher.UIThread.Post(
                () => CanExecuteChanged?.Invoke(this, EventArgs.Empty),
                DispatcherPriority.Background);
        }
        catch (Exception notificationFailure)
        {
            // The dispatcher may be unavailable or shutting down. The UI
            // notification may not have been delivered. Retained for
            // diagnostics; not re-dispatched from this catch.
            _lastNotificationFailure = notificationFailure;
        }
    }

    /// <summary>
    /// Raises CanExecuteChanged so bound buttons re-query their state.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T? Cast(object? parameter)
    {
        return parameter is T typed ? typed : default;
    }
}
