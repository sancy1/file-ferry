// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Export/PromptExporterEventEmissionTests.cs
// PURPOSE:  Proves PromptExporter emits prompt_exported on a successful single
//           export (with the prompt id) and on a successful bulk export (no
//           prompt id, bulk operation).
// RELATION: Consumes PromptExporter, PromptEventEmitter, PromptEventNames,
//           CapturingLogSink. Setup mirrors PromptExporterTests: NSubstitute
//           registry, confirmer, category stores; StubExecutor; TxtFormatWriter;
//           temp folder + IDisposable cleanup.
// NOTES:   - The emit fires only when ExportResult.Success is true, so the
//             failure paths (unregistered extension, Cancel overwrite) do not
//             emit. The two-sided assertion on the captured events would fail
//             if a failure path emitted.
//           - The single export carries promptId; the many export does not
//             (bulk operation; same convention as SearchAsync).
// ============================================================================

using System.IO.Compression;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using FileHarvester.Tests.Infrastructure;
using NSubstitute;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Export;
using Prompt.Platform.Observability;
using Xunit;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.Export;

/// <summary>
/// Event-emission tests for <see cref="PromptExporter"/>.
/// </summary>
public sealed class PromptExporterEventEmissionTests : IDisposable
{
    private readonly string _tempFolder;

    public PromptExporterEventEmissionTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "prompt-export-events-" + Guid.NewGuid().ToString("N"));
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

    private static PromptRecord MakePrompt(string title, string body) =>
        new(Guid.NewGuid(), title, body, BodyFormat.PlainText, null, null, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static IFormatWriterRegistry MakeRegistryWithTxt()
    {
        var reg = Substitute.For<IFormatWriterRegistry>();
        reg.GetByExtension(".txt").Returns(new TxtFormatWriter());
        reg.GetByExtension(Arg.Is<string>(s => s != ".txt")).Returns((IFormatWriter?)null);
        return reg;
    }

    private static PromptExporter MakeExporter(IFormatWriterRegistry reg, IPromptEventEmitter emitter)
    {
        var conf = Substitute.For<IOverwriteConfirmer>();
        conf.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OverwritePromptResult(OverwriteDecision.Replace, false)));

        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>()));

        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(Array.Empty<SubCategory>()));

        return new PromptExporter(reg, new StubExecutor(), conf, cats, subs, emitter);
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

    /// <summary>T1: ExportSingleAsync success emits prompt_exported with the prompt id.</summary>
    [Fact]
    public async Task SingleExportEmitsPromptExportedWithPromptId()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        var exporter = MakeExporter(MakeRegistryWithTxt(), emitter);
        var prompt = MakePrompt("SingleExportEvents", "body");

        var result = await exporter.ExportSingleAsync(prompt, ".txt", _tempFolder);

        Assert.True(result.Success);
        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Exported);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.True(evt.Metadata.TryGetValue("promptId", out var id));
        Assert.Equal(prompt.Id, id);
    }

    /// <summary>T2: ExportManyAsync success emits prompt_exported without a promptId (bulk).</summary>
    [Fact]
    public async Task ManyExportEmitsPromptExportedWithoutPromptId()
    {
        var sink = new CapturingLogSink();
        var emitter = new PromptEventEmitter(new[] { sink });
        var exporter = MakeExporter(MakeRegistryWithTxt(), emitter);
        var prompts = new List<PromptRecord>
        {
            MakePrompt("ManyA", "bodyA"),
            MakePrompt("ManyB", "bodyB"),
        };

        var result = await exporter.ExportManyAsync(prompts, ".txt", _tempFolder);

        Assert.True(result.Success);
        var snapshot = AssertOnlyOperation(sink, PromptEventNames.Exported);
        var evt = snapshot.Single(e => e.Metadata.ContainsKey("operation"));
        Assert.False(evt.Metadata.ContainsKey("promptId"));
    }

    private sealed class StubExecutor : IResilienceExecutor
    {
        public async Task<T> ExecuteAsync<T>(string p, Func<CancellationToken, Task<T>> op,
            Func<CancellationToken, Task<T>>? fb, string? k, int? t, CancellationToken ct)
            => await op(ct).ConfigureAwait(false);
        public Task ExecuteAsync(string p, Func<CancellationToken, Task> op,
            Func<CancellationToken, Task>? fb, string? k, int? t, CancellationToken ct)
            => op(ct);
    }
}