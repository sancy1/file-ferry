// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/VaultExporterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault
// since:    Phase B.2 (Deployment Vault export)
// purpose:  Unit tests for VaultExporter (Level 3 single-record export).
//           Uses the real FormatWriterRegistry; writes to temp files.
// ============================================================================

using System.Text.Json;
using DeploymentVault.Core.Domain;
using DeploymentVault.Platform.Export;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault;

/// <summary>
/// Unit tests for <see cref="VaultExporter"/>.
/// </summary>
public sealed class VaultExporterTests
{
    private static readonly string[] SampleTags = new[] { "prod" };

    private static Application NewApp(string name) => new(
        Id: Guid.NewGuid(),
        Name: name,
        Description: "A test application",
        Owner: "ops-team",
        RepositoryUrl: "https://example.com/repo",
        ApplicationUrl: "https://example.com/app",
        Status: RecordStatus.Active,
        Tags: SampleTags,
        Notes: "some notes",
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        DeletedAtUtc: null);

    private static InfrastructureRecord NewRecord() => new(
        Id: Guid.NewGuid(),
        ApplicationId: Guid.NewGuid(),
        Name: "Render API",
        Provider: "Render",
        ServiceCategory: ServiceCategory.Hosting,
        Environment: EnvironmentKind.Production,
        DashboardUrl: "https://dash.example.com",
        LoginEmail: "ops@example.com",
        Region: "eu-west-1",
        ResourceId: "srv-123",
        Status: RecordStatus.Active,
        Notes: "record notes",
        Tags: SampleTags,
        CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        DeletedAtUtc: null);

    private static VaultExporter NewExporter() =>
        new(new FormatWriterRegistry(new IFormatWriter[]
        {
            new MarkdownFormatWriter(),
            new TxtFormatWriter(),
            new JsonFormatWriter(),
            new CsvFormatWriter(),
            new PdfFormatWriter(),
            new ZipFormatWriter(),
        }));

    private static string TempPath(string ext) =>
        Path.Combine(Path.GetTempPath(), "vault-rec-" + Guid.NewGuid().ToString("N") + ext);

    [Fact]
    public async Task ExportRecordMarkdownCreatesFile()
    {
        var path = TempPath(".md");
        try
        {
            var result = await NewExporter().ExportRecordAsync(NewRecord(), ".md", path);

            Assert.True(result.Success);
            Assert.True(File.Exists(path));
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("Render API", text);
            Assert.Contains("Render", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportRecordTxtCreatesFile()
    {
        var path = TempPath(".txt");
        try
        {
            var result = await NewExporter().ExportRecordAsync(NewRecord(), ".txt", path);

            Assert.True(result.Success);
            Assert.True(File.Exists(path));
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("Render API", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportRecordJsonIsValidJsonWithFields()
    {
        var path = TempPath(".json");
        try
        {
            var result = await NewExporter().ExportRecordAsync(NewRecord(), ".json", path);

            Assert.True(result.Success);
            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.TryGetProperty("files", out _));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportRecordCsvHasHeaderRowAndOneDataRow()
    {
        var path = TempPath(".csv");
        try
        {
            var result = await NewExporter().ExportRecordAsync(NewRecord(), ".csv", path);

            Assert.True(result.Success);
            var text = await File.ReadAllTextAsync(path);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("Path", lines[0]);
            Assert.Contains("Render API", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportRecordPdfCreatesFile()
    {
        var path = TempPath(".pdf");
        try
        {
            var result = await NewExporter().ExportRecordAsync(NewRecord(), ".pdf", path);

            Assert.True(result.Success);
            Assert.True(File.Exists(path));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length > 4);
            Assert.Equal((byte)'%', bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportRecordZipCreatesArchiveWithDocument()
    {
        var path = TempPath(".zip");
        try
        {
            var result = await NewExporter().ExportRecordAsync(NewRecord(), ".zip", path);

            Assert.True(result.Success);
            Assert.True(File.Exists(path));
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            Assert.NotEmpty(archive.Entries);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportRecordUnknownExtensionFails()
    {
        var path = TempPath(".xyz");
        var result = await NewExporter().ExportRecordAsync(NewRecord(), ".xyz", path);

        Assert.False(result.Success);
        Assert.False(File.Exists(path));
    }

    // ---- Level 2: application ZIP ----

    [Fact]
    public async Task ExportApplicationZipContainsExpectedStructure()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord() with { ApplicationId = app.Id };
        var path = TempPath(".zip");
        try
        {
            var result = await NewExporter().ExportApplicationAsync(app, new[] { rec }, path);

            Assert.True(result.Success);
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains(names, n => n == "application.md");
            Assert.Contains(names, n => n.StartsWith("records/Render API/record.md", StringComparison.Ordinal));
            Assert.Contains(names, n => n.StartsWith("records/Render API/record.txt", StringComparison.Ordinal));
            Assert.Contains(names, n => n == "records.csv");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportApplicationZipRecordsCsvHasOneRowPerRecord()
    {
        var app = NewApp("Alpha");
        var rec1 = NewRecord() with { ApplicationId = app.Id, Name = "One" };
        var rec2 = NewRecord() with { ApplicationId = app.Id, Name = "Two" };
        var path = TempPath(".zip");
        try
        {
            var result = await NewExporter().ExportApplicationAsync(app, new[] { rec1, rec2 }, path);

            Assert.True(result.Success);
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = archive.GetEntry("records.csv");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            var csv = await reader.ReadToEndAsync();
            Assert.Contains("Name,Provider", csv);
            Assert.Contains("One", csv);
            Assert.Contains("Two", csv);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportApplicationZipRecordMarkdownContainsFields()
    {
        var app = NewApp("Alpha");
        var rec = NewRecord() with { ApplicationId = app.Id };
        var path = TempPath(".zip");
        try
        {
            var result = await NewExporter().ExportApplicationAsync(app, new[] { rec }, path);

            Assert.True(result.Success);
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = archive.Entries.First(e => e.FullName.EndsWith("record.md", StringComparison.Ordinal));
            using var reader = new StreamReader(entry.Open());
            var md = await reader.ReadToEndAsync();
            Assert.Contains("Render API", md);
            Assert.Contains("eu-west-1", md);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- Level 1: whole-vault ZIP ----

    [Fact]
    public async Task ExportEverythingZipContainsAllApplications()
    {
        var app1 = NewApp("Alpha");
        var app2 = NewApp("Beta");
        var rec1 = NewRecord() with { ApplicationId = app1.Id };
        var rec2 = NewRecord() with { ApplicationId = app2.Id, Name = "Vercel DB" };
        var payload = new[]
        {
            new VaultApplicationExport(app1, new[] { rec1 }),
            new VaultApplicationExport(app2, new[] { rec2 }),
        };
        var path = TempPath(".zip");
        try
        {
            var result = await NewExporter().ExportAllAsync(payload, path);

            Assert.True(result.Success);
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains(names, n => n.Contains("applications/Alpha/application.md", StringComparison.Ordinal));
            Assert.Contains(names, n => n.Contains("applications/Beta/application.md", StringComparison.Ordinal));
            Assert.Contains(names, n => n == "summary.csv");
            Assert.Contains(names, n => n == "records.csv");
            Assert.Contains(names, n => n == "README.txt");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportEverythingZipSummaryCsvHasOneRowPerApplication()
    {
        var app1 = NewApp("Alpha");
        var app2 = NewApp("Beta");
        var payload = new[]
        {
            new VaultApplicationExport(app1, Array.Empty<InfrastructureRecord>()),
            new VaultApplicationExport(app2, Array.Empty<InfrastructureRecord>()),
        };
        var path = TempPath(".zip");
        try
        {
            var result = await NewExporter().ExportAllAsync(payload, path);

            Assert.True(result.Success);
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = archive.GetEntry("summary.csv");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            var csv = await reader.ReadToEndAsync();
            Assert.Contains("Alpha", csv);
            Assert.Contains("Beta", csv);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExportEverythingLeavesNoTempFile()
    {
        var app = NewApp("Alpha");
        var dir = Path.Combine(Path.GetTempPath(), "vault-all-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "vault.zip");
        var payload = new[] { new VaultApplicationExport(app, Array.Empty<InfrastructureRecord>()) };
        try
        {
            var result = await NewExporter().ExportAllAsync(payload, path);

            Assert.True(result.Success);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(dir)) Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task ExportRecordLeavesNoTempFileOnFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), "vault-rec-" + Guid.NewGuid().ToString("N"), "out.md");
        var result = await NewExporter().ExportRecordAsync(NewRecord(), ".md", path);

        Assert.True(result.Success);
        Assert.True(File.Exists(path));
        var dir = Path.GetDirectoryName(path)!;
        var leftovers = Directory.GetFiles(dir, "*.tmp-*");
        Assert.Empty(leftovers);
        File.Delete(path);
        Directory.Delete(dir);
    }
}
