// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Import/PromptImporterEventEmissionTests.cs
// PURPOSE:  Proves PromptImporter.ImportFileAsync emits prompt_imported with
//           the created prompt's id after a successful import.
// RELATION: Consumes PromptImporter, PromptEventEmitter, PromptEventNames,
//           CapturingLogSink. Setup mirrors PromptImporterTests: NSubstitute
//           IPromptStore (returns the created prompt) and IPdfTextExtractor;
//           temp folder + IDisposable cleanup.
// NOTES:   - The emit is gated on the store call succeeding, so a failing
//             CreateAsync does not emit. The two-sided assertion catches any
//             stray operation name.
//           - The prompt id comes from ImportResult.Ok(created); the test
//             asserts the metadata promptId equals the created record's Id.
// ============================================================================

using System.IO;
using FileHarvester.Tests.Infrastructure;
using NSubstitute;
using Portfolio.Resilience.Events;
using Prompt.Core.Domain;
using Prompt.Platform.Import;
using Prompt.Platform.Observability;
using Xunit;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.Import;

/// <summary>
/// Event-emission test for <see cref="PromptImporter"/>.
/// </summary>
public sealed class PromptImporterEventEmissionTests : IDisposable
{
    private readonly string _tempFolder;

    public PromptImporterEventEmissionTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "prompt-import-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true); } catch { }
    }

    private static readonly string[] AllOperationNames = new[]
    {
        PromptEventNames.Created, PromptEventNames.Updated, PromptEventNames.Deleted,
        PromptEventNames.Restored, PromptEventNames.Exported, PromptEventNames.Imported,
        PromptEventNames.SearchExecuted, PromptEventNames.RenderFailed,
    };

    private static (IPromptStore store, IPdfTextExtractor pdf) MakeSubstitutes()
    {
        var store = Substitute.For<IPromptStore>();
        store.CreateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.ArgAt<PromptRecord>(0)));
        var pdf = Substitute.For<IPdfTextExtractor>();
        return (store, pdf);
    }

    private static IReadOnlyList<ResilienceEvent> AssertOnlyOperation(CapturingLogSink sink, string expected)
    {
        var snapshot = sink.Snapshot();
        var ops = snapshot
            .Select(e => e.Metadata.TryGetValue("operation", out var v) ? v as string : null)
            .Where(v => v is not null)
            .ToArray();
        Assert.Contains(expected, ops);
        var unexpected = ops
            .Where(v => AllOperationNames.Contains(v) && !string.Equals(v, expected, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(unexpected);
        return snapshot;
    }

    /// <summary>T1: a successful import emits prompt_imported with the created prompt id.</summary>
    [Fact]
    public async Task SuccessfulImportEmitsPromptImportedWithPromptId()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        var (store, pdf) = MakeSubstitutes();
        var importer = new PromptImporter(store, pdf, emitter);

        var file = Path.Combine(_tempFolder, "note.txt");
        await File.WriteAllTextAsync(file, "Hello from text.");

        var result = await importer.ImportFileAsync(file, null, null);

        Assert.True(result.Success);
        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Imported);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(result.Prompt!.Id, id);
    }
}