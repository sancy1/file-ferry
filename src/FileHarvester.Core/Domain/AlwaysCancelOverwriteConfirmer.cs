// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/AlwaysCancelOverwriteConfirmer.cs
// PURPOSE:  The default IOverwriteConfirmer registered by the Core
//           composition root. Fails closed: refuses every overwrite
//           without asking. Replaced in the App by the Avalonia modal
//           implementation and in tests by a deterministic stub.
// RELATION: Registered via TryAddSingleton in AddFileHarvester. Overridden
//           by AddSingleton<IOverwriteConfirmer, AvaloniaOverwriteConfirmer>
//           in the App. Consumed by OutputSink.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// An <see cref="IOverwriteConfirmer"/> that always refuses. Safe default
/// for any consumer with no way to prompt the user.
/// </summary>
/// <remarks>
/// <para>
/// This class exists so that a misconfigured DI graph - one that forgot
/// to register a confirmer - fails closed rather than silently
/// overwriting. Any code path that reaches <see cref="OutputSink"/>
/// without a UI confirmer will refuse to replace an existing file and
/// return a failed <see cref="SaveResult"/> with reason
/// <c>"Cancelled by user: file exists"</c>.
/// </para>
/// <para>
/// This class has no state and is safe to use as a singleton.
/// </para>
/// </remarks>
public sealed class AlwaysCancelOverwriteConfirmer : IOverwriteConfirmer
{
    /// <inheritdoc/>
    public Task<OverwritePromptResult> ConfirmAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new OverwritePromptResult(
            Decision: OverwriteDecision.Cancel,
            ApplyToAllRemaining: false));
    }
}
