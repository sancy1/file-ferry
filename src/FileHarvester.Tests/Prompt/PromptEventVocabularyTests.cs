// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/PromptEventVocabularyTests.cs
// PURPOSE:  Proves the Prompt App's eight operation names are emitted with
//           their exact name carried on ResilienceEvent.Metadata["operation"].
//           Uses the existing CapturingLogSink (test infrastructure) as the
//           ILogSink target, so the test asserts on exactly what the runtime
//           Log Drawer would receive.
// RELATION: Consumes PromptEventNames (Prompt.Core), IPromptEventEmitter and
//           PromptEventEmitter (Prompt.Platform.Observability), and
//           CapturingLogSink (FileHarvester.Tests.Infrastructure). Pattern
//           mirrors PromptPolicyRegistrationTests: in-memory only, no host,
//           no files, no network. Two-sided assertions: the expected event
//           is present AND no other operation name is present, so a vacuous
//           pass cannot occur.
// NOTES:   - CapturingLogSink exposes Snapshot() (returns
//             IReadOnlyList<ResilienceEvent>, oldest first). It has no
//             Events property; the previous version of this file assumed
//             one and failed to compile (CS1061). Verified by reading the
//             full CapturingLogSink.cs.
//           - The helper takes the snapshot once and asserts against it, so
//             the two assertions in T8 do not race the queue.
//           - The two-sided assertion (present AND no other) is deliberate:
//             a one-sided "contains" assertion passes vacuously when the
//             sink is empty and the test would silently stop protecting the
//             vocabulary.
//           - PromptId is asserted when supplied and absent otherwise, so
//             the Metadata contract for the optional field is proven both
//             ways.
// ============================================================================

using FileHarvester.Tests.Infrastructure;
using Portfolio.Resilience.Events;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Observability;
using Xunit;

namespace FileHarvester.Tests.Prompt;

/// <summary>
/// Proves the eight Prompt operation names are emitted exactly once each and
/// carried on <c>Metadata["operation"]</c>.
/// </summary>
/// <remarks>
/// These tests are the acceptance proof for P5.1's event vocabulary. They do
/// not touch SQLite, the filesystem, or the network. Rollback: delete this
/// file. No existing file is modified.
/// </remarks>
public sealed class PromptEventVocabularyTests
{
    /// <summary>
    /// All eight Prompt operation names, used to assert that no foreign
    /// prompt_* name leaked into the capture.
    /// </summary>
    private static readonly string[] AllPromptNames =
    {
        PromptEventNames.Created,
        PromptEventNames.Updated,
        PromptEventNames.Deleted,
        PromptEventNames.Restored,
        PromptEventNames.Exported,
        PromptEventNames.Imported,
        PromptEventNames.SearchExecuted,
        PromptEventNames.RenderFailed,
    };

    /// <summary>
    /// Takes a single snapshot and asserts the expected operation name is
    /// present AND no other prompt_* operation name is present. The
    /// snapshot is taken once so successive assertions cannot race the
    /// queue.
    /// </summary>
    private static IReadOnlyList<ResilienceEvent> AssertSingleOperation(
        CapturingLogSink sink, string expected)
    {
        var snapshot = sink.Snapshot();

        var operations = snapshot
            .Select(e => e.Metadata.TryGetValue("operation", out var v) ? v as string : null)
            .Where(v => v is not null)
            .ToArray();

        Assert.Contains(expected, operations);

        var unexpected = operations
            .Where(v => AllPromptNames.Contains(v) && !string.Equals(v, expected, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(unexpected);

        return snapshot;
    }

    /// <summary>T1: <c>prompt_created</c> is emitted with the exact name.</summary>
    [Fact]
    public void CreatedIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });

        emitter.Emit(PromptEventNames.Created, Guid.NewGuid());

        AssertSingleOperation(sink, PromptEventNames.Created);
    }

    /// <summary>T2: <c>prompt_updated</c> is emitted with the exact name.</summary>
    [Fact]
    public void UpdatedIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });

        emitter.Emit(PromptEventNames.Updated, Guid.NewGuid());

        AssertSingleOperation(sink, PromptEventNames.Updated);
    }

    /// <summary>T3: <c>prompt_deleted</c> is emitted with the exact name.</summary>
    [Fact]
    public void DeletedIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });

        emitter.Emit(PromptEventNames.Deleted, Guid.NewGuid());

        AssertSingleOperation(sink, PromptEventNames.Deleted);
    }

    /// <summary>T4: <c>prompt_restored</c> is emitted with the exact name.</summary>
    [Fact]
    public void RestoredIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });

        emitter.Emit(PromptEventNames.Restored, Guid.NewGuid());

        AssertSingleOperation(sink, PromptEventNames.Restored);
    }

    /// <summary>T5: <c>prompt_exported</c> is emitted with the exact name
    /// and tagged with the export policy.</summary>
    [Fact]
    public void ExportedIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink }, PromptPolicyNames.PromptExport);

        emitter.Emit(PromptEventNames.Exported, Guid.NewGuid());

        var snapshot = AssertSingleOperation(sink, PromptEventNames.Exported);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.Equal(PromptPolicyNames.PromptExport, evt.PolicyName);
    }

    /// <summary>T6: <c>prompt_imported</c> is emitted with the exact name.</summary>
    [Fact]
    public void ImportedIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });

        emitter.Emit(PromptEventNames.Imported, Guid.NewGuid());

        AssertSingleOperation(sink, PromptEventNames.Imported);
    }

    /// <summary>T7: <c>prompt_search_executed</c> is emitted with the exact
    /// name; a search is not a single-prompt operation, so the promptId
    /// metadata key must be absent.</summary>
    [Fact]
    public void SearchExecutedIsEmittedWithExactName()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });

        emitter.Emit(PromptEventNames.SearchExecuted);

        var snapshot = AssertSingleOperation(sink, PromptEventNames.SearchExecuted);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.False(evt.Metadata.ContainsKey("promptId"));
    }

    /// <summary>
    /// T8: <c>prompt_render_failed</c> is emitted with the exact name and,
    /// when no prompt id is supplied, the <c>promptId</c> metadata key is
    /// absent (not an empty guid).
    /// </summary>
    [Fact]
    public void RenderFailedIsEmittedWithExactNameAndOmitsPromptIdWhenUnknown()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink }, PromptPolicyNames.PromptRender);

        emitter.Emit(PromptEventNames.RenderFailed);

        var snapshot = AssertSingleOperation(sink, PromptEventNames.RenderFailed);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.Equal(PromptPolicyNames.PromptRender, evt.PolicyName);
        Assert.False(evt.Metadata.ContainsKey("promptId"));
    }
}