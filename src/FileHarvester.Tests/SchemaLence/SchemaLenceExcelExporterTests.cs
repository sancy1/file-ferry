using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using NSubstitute;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Providers;
using FileHarvester.App.Services;

namespace FileHarvester.Tests.SchemaLence;

public sealed class SchemaLenceExcelExporterTests
{
    [Fact]
    public async Task ExportWritesHeadersAndEveryStreamedValueAsText()
    {
        var provider = Substitute.For<IDatabaseProvider>();
        var profile = new DatabaseConnectionProfile(
            "id", "Weather", "postgresql", "localhost", 5432, "weather", "reader", "Require");
        var columns = new[] { "id", "city_name", "formula_like" };
        IReadOnlyList<string?>[] rows =
        {
            new string?[] { "7", "Nairobi", "=1+1" },
            new string?[] { "8", null, "+SUM(A1:A2)" },
        };
        provider.StreamRowsAsync(
                profile,
                "secret",
                "public",
                "weather",
                500,
                Arg.Any<Func<IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string?>>, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var writeBatch = call.Arg<
                    Func<IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string?>>, Task>>();
                return writeBatch(columns, rows);
            });

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            var exported = await SchemaLenceExcelExporter.ExportAsync(
                provider, profile, "secret", "public", "weather", path);

            exported.Should().Be(2);
            using var workbook = SpreadsheetDocument.Open(path, false);
            var sheet = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet!;
            var sheetRows = sheet.Descendants<Row>().ToArray();
            sheetRows.Should().HaveCount(3);
            sheetRows.SelectMany(row => row.Elements<Cell>())
                .Select(cell => string.Concat(cell.Descendants<Text>().Select(text => text.Text)))
                .Should()
                .Equal(
                    "id", "city_name", "formula_like",
                    "7", "Nairobi", "=1+1",
                    "8", "", "+SUM(A1:A2)");
            sheet.Descendants<CellFormula>().Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
