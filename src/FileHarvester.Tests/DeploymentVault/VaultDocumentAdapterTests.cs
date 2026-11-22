// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/VaultDocumentAdapterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault
// since:    Phase B.1 (Deployment Vault export)
// purpose:  Unit tests for VaultDocumentAdapter. No database, no I/O — the
//           adapter is pure.
// ============================================================================

using DeploymentVault.Core.Domain;
using DeploymentVault.Platform.Export;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault;

/// <summary>
/// Unit tests for <see cref="VaultDocumentAdapter"/>.
/// </summary>
public sealed class VaultDocumentAdapterTests
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

    private static InfrastructureRecord NewRecord(Guid applicationId, string name) => new(
        Id: Guid.NewGuid(),
        ApplicationId: applicationId,
        Name: name,
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

    [Fact]
    public void ApplicationProducesSingleMergedDocument()
    {
        var app = NewApp("Alpha");
        var writer = new MarkdownFormatWriter();

        var doc = VaultDocumentAdapter.ToComposedDocument(app, writer);

        Assert.Single(doc.Files);
        Assert.Equal(OutputMode.SingleMerged, doc.Mode);
        Assert.False(doc.IncludeHeaders);
        Assert.False(doc.IncludeCodeFences);

        var content = doc.Files[0];
        Assert.Contains("Alpha", content.DecodedText);
        Assert.Contains("ops-team", content.DecodedText);
        Assert.EndsWith(".md", content.File.AbsolutePath);
        Assert.Equal(ResolutionConfidence.Exact, content.File.Confidence);
        Assert.False(content.IsBinary);
        Assert.NotEmpty(content.Sha256);
    }

    [Fact]
    public void RecordProducesSingleMergedDocument()
    {
        var app = NewApp("Alpha");
        var record = NewRecord(app.Id, "Render API");
        var writer = new MarkdownFormatWriter();

        var doc = VaultDocumentAdapter.ToComposedDocument(record, writer);

        Assert.Single(doc.Files);
        Assert.Equal(OutputMode.SingleMerged, doc.Mode);

        var content = doc.Files[0];
        Assert.Contains("Render API", content.DecodedText);
        Assert.Contains("Render", content.DecodedText);
        Assert.Contains("eu-west-1", content.DecodedText);
        Assert.EndsWith(".md", content.File.AbsolutePath);
    }

    [Fact]
    public void NullOptionalFieldsStillProduceValidDocument()
    {
        var app = new Application(
            Id: Guid.NewGuid(),
            Name: "Bare",
            Description: null,
            Owner: null,
            RepositoryUrl: null,
            ApplicationUrl: null,
            Status: RecordStatus.Active,
            Tags: Array.Empty<string>(),
            Notes: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            DeletedAtUtc: null);
        var writer = new MarkdownFormatWriter();

        var doc = VaultDocumentAdapter.ToComposedDocument(app, writer);

        Assert.Single(doc.Files);
        Assert.Contains("Bare", doc.Files[0].DecodedText);
        Assert.DoesNotContain("**Owner:**", doc.Files[0].DecodedText);
    }
}
