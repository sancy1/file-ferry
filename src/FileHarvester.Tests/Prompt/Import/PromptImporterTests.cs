// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Import/PromptImporterTests.cs
// PURPOSE:  Guards PromptImporter.ImportFileAsync: the .txt/.md direct
//           read, the .docx extraction (a minimal in-test docx), the .pdf
//           via the IPdfTextExtractor substitute, the unknown-extension
//           failure, and the missing-file failure.
// RELATION: Added at P3.5. Consumes PromptImporter (Prompt.Platform.Import)
//           and substitutes IPromptStore and IPdfTextExtractor.
// NOTES:    - The .docx test builds a minimal WordprocessingDocument in the
//             temp folder via DocumentFormat.OpenXml. One paragraph with a
//             known text.
//           - The .pdf test substitutes IPdfTextExtractor; the real PdfPig
//             extraction is exercised only in the app (not in unit tests).
//           - The store substitute returns the prompt it was given.
// ============================================================================

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using NSubstitute;
using Prompt.Core.Domain;
using Prompt.Platform.Import;
using Xunit;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.Import;

/// <summary>
/// Guards PromptImporter.ImportFileAsync. Six facts.
/// </summary>
public sealed class PromptImporterTests : IDisposable
{
    private readonly string _tempFolder;

    public PromptImporterTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "prompt-import-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true); } catch { }
    }

    private static (IPromptStore store, IPdfTextExtractor pdf) MakeSubstitutes(string? pdfText = null)
    {
        var store = Substitute.For<IPromptStore>();
        store.CreateAsync(Arg.Any<PromptRecord>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.ArgAt<PromptRecord>(0)));

        var pdf = Substitute.For<IPdfTextExtractor>();
        if (pdfText is not null)
        {
            pdf.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(pdfText));
        }

        return (store, pdf);
    }

    [Fact]
    public async Task ImportTxtCreatesPrompt()
    {
        // ARRANGE
        var file = Path.Combine(_tempFolder, "note.txt");
        await File.WriteAllTextAsync(file, "Hello from text.");
        var (store, pdf) = MakeSubstitutes();
        var importer = new PromptImporter(store, pdf);

        // ACT
        var result = await importer.ImportFileAsync(file, null, null);

        // ASSERT
        result.Success.Should().BeTrue();
        result.Prompt.Should().NotBeNull();
        result.Prompt!.Title.Should().Be("note");
        result.Prompt.Body.Should().Be("Hello from text.");
    }

    [Fact]
    public async Task ImportMdCreatesPrompt()
    {
        // ARRANGE
        var file = Path.Combine(_tempFolder, "readme.md");
        await File.WriteAllTextAsync(file, "# Heading\nBody.");
        var (store, pdf) = MakeSubstitutes();
        var importer = new PromptImporter(store, pdf);

        // ACT
        var result = await importer.ImportFileAsync(file, null, null);

        // ASSERT
        result.Success.Should().BeTrue();
        result.Prompt!.Body.Should().Be("# Heading\nBody.");
    }

    [Fact]
    public async Task ImportDocxCreatesPrompt()
    {
        // ARRANGE
        var file = Path.Combine(_tempFolder, "doc.docx");
        using (var doc = WordprocessingDocument.Create(file, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("Docx body text.")))));
            main.Document.Save();
        }
        var (store, pdf) = MakeSubstitutes();
        var importer = new PromptImporter(store, pdf);

        // ACT
        var result = await importer.ImportFileAsync(file, null, null);

        // ASSERT
        result.Success.Should().BeTrue();
        result.Prompt!.Body.Should().Contain("Docx body text.");
    }

    [Fact]
    public async Task ImportPdfUsesExtractor()
    {
        // ARRANGE
        var file = Path.Combine(_tempFolder, "doc.pdf");
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
        var (store, pdf) = MakeSubstitutes(pdfText: "PDF extracted text.");
        var importer = new PromptImporter(store, pdf);

        // ACT
        var result = await importer.ImportFileAsync(file, null, null);

        // ASSERT
        result.Success.Should().BeTrue();
        result.Prompt!.Body.Should().Be("PDF extracted text.");
        await pdf.Received(1).ExtractTextAsync(file, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportUnknownExtensionFails()
    {
        // ARRANGE
        var file = Path.Combine(_tempFolder, "thing.xyz");
        await File.WriteAllTextAsync(file, "x");
        var (store, pdf) = MakeSubstitutes();
        var importer = new PromptImporter(store, pdf);

        // ACT
        var result = await importer.ImportFileAsync(file, null, null);

        // ASSERT
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
        result.ErrorMessage!.Should().Contain("Unsupported");
    }

    [Fact]
    public async Task ImportMissingFileFails()
    {
        // ARRANGE
        var file = Path.Combine(_tempFolder, "does-not-exist.txt");
        var (store, pdf) = MakeSubstitutes();
        var importer = new PromptImporter(store, pdf);

        // ACT
        var result = await importer.ImportFileAsync(file, null, null);

        // ASSERT
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
        result.ErrorMessage!.Should().Contain("File not found");
    }
}