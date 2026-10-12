// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/IOverwriteConfirmer.cs
// PURPOSE:  Contract for asking the user whether an existing output file
//           may be replaced. Abstracted so the Core sink does not depend
//           on any UI framework; the App supplies an Avalonia modal
//           implementation; tests supply a deterministic stub.
// RELATION: Consumed by OutputSink (Phase D.1.2) before every write that
//           would collide with an existing file. Implemented by
//           AvaloniaOverwriteConfirmer in FileHarvester.App and by the
//           test stub in FileHarvester.Tests.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Asks the user whether an existing file may be replaced by the current
/// harvest.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be safe to call from the harvest pipeline's async
/// context. The default implementation registered by the Core composition
/// root, <see cref="AlwaysCancelOverwriteConfirmer"/>, fails closed and
/// returns <see cref="OverwriteDecision.Cancel"/> without asking anyone -
/// this is the correct default for any consumer that has no way to prompt
/// (a headless run, a test, a scheduled job).
/// </para>
/// <para>
/// The dialog-style implementation in the App layer is permitted to
/// present a modal window and block until the user chooses. It must
/// honor the supplied <see cref="CancellationToken"/>: if the token is
/// signaled while the prompt is open, the prompt closes and the method
/// returns an <see cref="OverwritePromptResult"/> with
/// <see cref="OverwriteDecision.Cancel"/> and
/// <see cref="OverwritePromptResult.ApplyToAllRemaining"/> = false. It
/// must never write to the target path itself.
/// </para>
/// <para>
/// The confirm path (an existing file) is the only path that reaches
/// this interface. When the target does not exist, the sink does not
/// call the confirmer at all.
/// </para>
/// </remarks>
public interface IOverwriteConfirmer
{
    /// <summary>
    /// Asks whether the file at <paramref name="targetPath"/> may be
    /// replaced, and whether the same decision should apply to every
    /// subsequent collision in the current harvest operation.
    /// </summary>
    /// <param name="targetPath">
    /// The absolute path of the file that already exists and would be
    /// replaced. Never null or empty. Callers pass a canonicalized path
    /// (already produced by <see cref="System.IO.Path.GetFullPath(string)"/>).
    /// </param>
    /// <param name="cancellationToken">
    /// Signals that the harvest has been cancelled by the user or the
    /// application. Implementations must return an
    /// <see cref="OverwritePromptResult"/> with Decision = Cancel and
    /// ApplyToAllRemaining = false promptly when the token fires.
    /// </param>
    /// <returns>
    /// An <see cref="OverwritePromptResult"/> whose
    /// <see cref="OverwritePromptResult.Decision"/> is either
    /// <see cref="OverwriteDecision.Replace"/> (the caller may overwrite)
    /// or <see cref="OverwriteDecision.Cancel"/> (the caller must not
    /// overwrite; the harvest aborts), and whose
    /// <see cref="OverwritePromptResult.ApplyToAllRemaining"/> indicates
    /// whether the same decision should be reused for every subsequent
    /// collision in the current operation without asking again.
    /// </returns>
    Task<OverwritePromptResult> ConfirmAsync(
        string targetPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The user's response to an overwrite prompt, and whether the same
/// response applies to every remaining collision in the current harvest
/// operation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Decision"/> has exactly two values. There is no
/// <c>Unknown</c>, no <c>Deferred</c>, and no default-zero value that
/// could be returned by accident. A confirmer that cannot decide must
/// return <see cref="OverwriteDecision.Cancel"/> - the fail-closed
/// choice. The sink treats any non-<c>Replace</c> value as Cancel.
/// </para>
/// <para>
/// <see cref="ApplyToAllRemaining"/> is honored only while the current
/// <c>WriteSeparateAsync</c> operation is in flight. It is reset for
/// every new save operation; it is not persisted. When true and
/// <see cref="Decision"/> is Replace, the sink overwrites every later
/// collision in the same operation without asking. When true and
/// <see cref="Decision"/> is Cancel, the sink aborts the operation at
/// the first collision without writing any further files.
/// </para>
/// </remarks>
public sealed record OverwritePromptResult(
    OverwriteDecision Decision,
    bool ApplyToAllRemaining);

/// <summary>
/// The user's response to a single overwrite prompt.
/// </summary>
/// <remarks>
/// Two values, deliberately. There is no <c>Unknown</c>, no <c>Deferred</c>,
/// and no default-zero value that could be returned by accident. A
/// confirmer that cannot decide must return <see cref="Cancel"/> - the
/// fail-closed choice. The sink treats any non-<c>Replace</c> value as
/// Cancel.
/// </remarks>
public enum OverwriteDecision
{
    /// <summary>
    /// Do not overwrite. The existing file is left untouched and the sink
    /// returns a failed <see cref="SaveResult"/> with reason
    /// <c>"Cancelled by user: file exists"</c>. This is the safe default
    /// and the zero value of the enum.
    /// </summary>
    Cancel = 0,

    /// <summary>
    /// Overwrite the existing file with the harvest output.
    /// </summary>
    Replace = 1,
}
