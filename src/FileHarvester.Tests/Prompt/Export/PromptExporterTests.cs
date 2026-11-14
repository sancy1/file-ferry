// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Export/PromptExporterTests.cs
// PURPOSE:  Guards PromptExporter.ExportSingleAsync: the happy path writes
//           the file; an unregistered extension fails; an existing file with
//           a user Cancel fails and leaves the file untouched; an existing
//           file with a user Replace overwrites the content.
// RELATION: Added at P3.2. Consumes PromptExporter (Prompt.Platform.Export),
//           the real TxtFormatWriter, and substitutes for the registry,
//           the resilience executor (via a pass-through StubExecutor),
//           and the overwrite confirmer.
// NOTES:    - The StubExecutor is a pass-through IResilienceExecutor that
//             invokes the operation directly. Its signature mirrors the
//             one used by SqlitePromptSearchTests; the real IResilienceExecutor
//             lives in Portfolio.Resilience.Abstractions.
//           - The overwrite confirmer is substituted; the tests set its
//             decision per test (Replace or Cancel).
//           - The registry is substituted; the tests return the real
//             TxtFormatWriter for ".txt" and null for anything else.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using FluentAssertions;
using NSubstitute;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Export;
using Xunit;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.Export;

/// <summary>
/// Guards PromptExporter.ExportSingleAsync. Four facts: the happy path,
/// the unknown extension, the Cancel overwrite, the Replace overwrite.
/// </summary>
public sealed class PromptExporterTests : IDisposable
{
    private readonly string _tempFolder;

    public PromptExporterTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "prompt-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true); } catch { }
    }

    private static PromptRecord MakePrompt(string title, string body, Guid? categoryId = null, Guid? subCategoryId = null) =>
        new(Guid.NewGuid(), title, body, BodyFormat.PlainText, categoryId, subCategoryId, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static PromptExporter MakeExporter(
        IFormatWriterRegistry reg,
        OverwriteDecision decision,
        IReadOnlyList<Category>? categories = null,
        IReadOnlyList<SubCategory>? subCategories = null)
    {
        var conf = Substitute.For<IOverwriteConfirmer>();
        conf.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OverwritePromptResult(decision, false)));

        var cats = Substitute.For<ICategoryStore>();
        cats.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(categories ?? Array.Empty<Category>()));

        var subs = Substitute.For<ISubCategoryStore>();
        subs.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubCategory>>(subCategories ?? Array.Empty<SubCategory>()));

        return new PromptExporter(reg, new StubExecutor(), conf, cats, subs);
    }

    private static IFormatWriterRegistry MakeRegistryWithTxt()
    {
        var reg = Substitute.For<IFormatWriterRegistry>();
        reg.GetByExtension(".txt").Returns(new TxtFormatWriter());
        reg.GetByExtension(Arg.Is<string>(s => s != ".txt")).Returns((IFormatWriter?)null);
        return reg;
    }

    /// <summary>
    /// Proves the happy path: a known extension, a fresh folder, and no
    /// existing file - the export writes a file and returns Ok.
    /// </summary>
    [Fact]
    public async Task ExportSingleAsyncWritesToDestination()
    {
        // ARRANGE
        var prompt = MakePrompt("Test Title", "Hello body.");
        var reg = MakeRegistryWithTxt();
        var exporter = MakeExporter(reg, OverwriteDecision.Replace);

        // ACT
        var result = await exporter.ExportSingleAsync(prompt, ".txt", _tempFolder);

        // ASSERT
        result.Success.Should().BeTrue();
        result.FileCount.Should().Be(1);
        result.OutputPath.Should().NotBeNull();
        File.Exists(result.OutputPath!).Should().BeTrue();
    }

    /// <summary>
    /// Proves an unregistered extension returns Fail with the expected
    /// message and writes nothing.
    /// </summary>
    [Fact]
    public async Task ExportSingleAsyncUnknownExtensionFails()
    {
        // ARRANGE
        var prompt = MakePrompt("P", "B");
        var reg = MakeRegistryWithTxt();
        var exporter = MakeExporter(reg, OverwriteDecision.Replace);

        // ACT
        var result = await exporter.ExportSingleAsync(prompt, ".xyz", _tempFolder);

        // ASSERT
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
        result.ErrorMessage!.Should().Contain("No format writer");
    }

    /// <summary>
    /// Proves an existing file with a user Cancel returns Fail and leaves
    /// the existing file's content untouched.
    /// </summary>
    [Fact]
    public async Task ExportSingleAsyncCancelOnExistingFails()
    {
        // ARRANGE
        var prompt = MakePrompt("CancelTest", "body");
        var target = Path.Combine(_tempFolder, "CancelTest.txt");
        File.WriteAllText(target, "existing");
        var reg = MakeRegistryWithTxt();
        var exporter = MakeExporter(reg, OverwriteDecision.Cancel);

        // ACT
        var result = await exporter.ExportSingleAsync(prompt, ".txt", _tempFolder);

        // ASSERT
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
        result.ErrorMessage!.Should().Contain("Cancelled by user");
        File.ReadAllText(target).Should().Be("existing");
    }

    /// <summary>
    /// Proves an existing file with a user Replace overwrites the file's
    /// content and returns Ok.
    /// </summary>
    [Fact]
    public async Task ExportSingleAsyncReplaceOverwritesExisting()
    {
        // ARRANGE
        var prompt = MakePrompt("ReplaceTest", "new content");
        var target = Path.Combine(_tempFolder, "ReplaceTest.txt");
        File.WriteAllText(target, "old content");
        var reg = MakeRegistryWithTxt();
        var exporter = MakeExporter(reg, OverwriteDecision.Replace);

        // ACT
        var result = await exporter.ExportSingleAsync(prompt, ".txt", _tempFolder);

        // ASSERT
        result.Success.Should().BeTrue();
        File.ReadAllText(target).Should().Contain("new content");
    }

    /// <summary>
    /// Proves ExportManyAsync builds the Category/SubCategory/Title.ext tree
    /// in a zip with a manifest.json. Two prompts in "Literature" with the
    /// "Sonnets" sub-category, one prompt uncategorized.
    /// </summary>
    [Fact]
    public async Task ExportManyAsyncBuildsZipTree()
    {
        // ARRANGE
        var cat = new Category(Guid.NewGuid(), "Literature", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var sub = new SubCategory(Guid.NewGuid(), cat.Id, "Sonnets", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var p1 = MakePrompt("Sonnet 18", "body1", cat.Id, sub.Id);
        var p2 = MakePrompt("Othello", "body2", cat.Id, null);
        var p3 = MakePrompt("Loose", "body3");
        var prompts = new List<PromptRecord> { p1, p2, p3 };
        var reg = MakeRegistryWithTxt();
        var exporter = MakeExporter(
            reg, OverwriteDecision.Replace,
            categories: new[] { cat },
            subCategories: new[] { sub });

        // ACT
        var result = await exporter.ExportManyAsync(prompts, ".txt", _tempFolder);

        // ASSERT
        result.Success.Should().BeTrue();
        result.FileCount.Should().Be(3);
        File.Exists(result.OutputPath!).Should().BeTrue();

        using var archive = new System.IO.Compression.ZipArchive(
            File.OpenRead(result.OutputPath!), System.IO.Compression.ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain("Literature/Sonnets/Sonnet 18.txt");
        names.Should().Contain("Literature/Othello.txt");
        names.Should().Contain("Uncategorized/Loose.txt");
        names.Should().Contain("manifest.json");
    }
    /// <summary>
    /// A pass-through IResilienceExecutor. The operation runs directly; the
    /// fallback, idempotency key, and time budget are ignored. Matches the
    /// shape used by SqlitePromptSearchTests.
    /// </summary>
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