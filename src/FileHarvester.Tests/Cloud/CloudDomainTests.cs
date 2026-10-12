// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/CloudDomainTests.cs
// PURPOSE:  Unit tests for the cloud domain records, enums, and the
//           CloudWriteHandle. No I/O, no DI; pure in-memory.
// RELATION: Consumes FileHarvester.Core.Domain cloud types. Exercised
//           by every provider adapter in C.3 and later.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud;

/// <summary>
/// Unit tests for the cloud domain types.
/// </summary>
public sealed class CloudDomainTests
{
    // ------------------------------------------------------------------
    // CloudEntry
    // ------------------------------------------------------------------

    [Fact]
    public void CloudEntryConstructsAndExposesAllFields()
    {
        var modified = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var entry = new CloudEntry(
            Path: "s3://bucket/docs/a.txt",
            Name: "a.txt",
            IsDirectory: false,
            SizeBytes: 1024,
            LastModifiedUtc: modified,
            ContentType: "text/plain");

        entry.Path.Should().Be("s3://bucket/docs/a.txt");
        entry.Name.Should().Be("a.txt");
        entry.IsDirectory.Should().BeFalse();
        entry.SizeBytes.Should().Be(1024);
        entry.LastModifiedUtc.Should().Be(modified);
        entry.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public void CloudEntryEqualityByValue()
    {
        var a = new CloudEntry("s3://b/k", "k", false, 1, null, null);
        var b = new CloudEntry("s3://b/k", "k", false, 1, null, null);
        a.Should().Be(b);
    }

    // ------------------------------------------------------------------
    // CloudEntryPage
    // ------------------------------------------------------------------

    [Fact]
    public void CloudEntryPageConstructsAndExposesAllFields()
    {
        var entries = new[]
        {
            new CloudEntry("s3://b/a", "a", false, 1, null, null),
            new CloudEntry("s3://b/b", "b", false, 2, null, null),
        };
        var page = new CloudEntryPage(entries, "next-token");

        page.Entries.Should().HaveCount(2);
        page.ContinuationToken.Should().Be("next-token");
    }

    [Fact]
    public void CloudEntryPageNullContinuationMeansLastPage()
    {
        var page = new CloudEntryPage(Array.Empty<CloudEntry>(), null);
        page.ContinuationToken.Should().BeNull();
        page.Entries.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // ProviderMetadata
    // ------------------------------------------------------------------

    [Fact]
    public void ProviderMetadataConstructsAndExposesAllFields()
    {
        var extras = new Dictionary<string, string>
        {
            ["storage-class"] = "STANDARD",
        };
        var meta = new ProviderMetadata(
            SizeBytes: 2048,
            LastModifiedUtc: null,
            ContentType: "application/json",
            ETag: "abc123",
            Extra: extras);

        meta.SizeBytes.Should().Be(2048);
        meta.LastModifiedUtc.Should().BeNull();
        meta.ContentType.Should().Be("application/json");
        meta.ETag.Should().Be("abc123");
        meta.Extra.Should().ContainKey("storage-class");
    }

    // ------------------------------------------------------------------
    // FieldSpec
    // ------------------------------------------------------------------

    [Fact]
    public void FieldSpecConstructsAndExposesAllFields()
    {
        var spec = new FieldSpec(
            Key: "accessKeyId",
            Label: "Access Key ID",
            Kind: FieldKind.Text,
            Required: true,
            Placeholder: "AKIA...",
            HelpText: "From the IAM console.",
            ValidatePattern: "^AKIA[0-9A-Z]{16}$");

        spec.Key.Should().Be("accessKeyId");
        spec.Label.Should().Be("Access Key ID");
        spec.Kind.Should().Be(FieldKind.Text);
        spec.Required.Should().BeTrue();
        spec.Placeholder.Should().Be("AKIA...");
        spec.HelpText.Should().Be("From the IAM console.");
        spec.ValidatePattern.Should().Be("^AKIA[0-9A-Z]{16}$");
    }

    [Fact]
    public void FieldKindEnumValuesAreStable()
    {
        ((int)FieldKind.Text).Should().Be(0);
        ((int)FieldKind.Password).Should().Be(1);
        ((int)FieldKind.Multiline).Should().Be(2);
        ((int)FieldKind.File).Should().Be(3);
        ((int)FieldKind.Choice).Should().Be(4);
    }

    // ------------------------------------------------------------------
    // CloudProviderKind
    // ------------------------------------------------------------------

    [Fact]
    public void CloudProviderKindEnumValuesAreStable()
    {
        ((int)CloudProviderKind.S3Compatible).Should().Be(0);
        ((int)CloudProviderKind.AzureBlob).Should().Be(1);
        ((int)CloudProviderKind.GcsObject).Should().Be(2);
        ((int)CloudProviderKind.GoogleDrive).Should().Be(3);
        ((int)CloudProviderKind.OneDrive).Should().Be(4);
        ((int)CloudProviderKind.Dropbox).Should().Be(5);
    }

    // ------------------------------------------------------------------
    // AuthKind
    // ------------------------------------------------------------------

    [Fact]
    public void AuthKindEnumValuesAreStable()
    {
        ((int)AuthKind.LongLivedKey).Should().Be(0);
        ((int)AuthKind.OAuth).Should().Be(1);
        ((int)AuthKind.ConnectionString).Should().Be(2);
    }

    // ------------------------------------------------------------------
    // ProviderProfile
    // ------------------------------------------------------------------

    [Fact]
    public void ProviderProfileConstructsAndExposesAllFields()
    {
        var fields = new[]
        {
            new FieldSpec("accessKeyId", "Access Key ID", FieldKind.Text, true, null, null, null),
            new FieldSpec("secretAccessKey", "Secret Access Key", FieldKind.Password, true, null, null, null),
        };
        var profile = new ProviderProfile(
            Id: "aws-s3",
            DisplayName: "AWS S3",
            Kind: CloudProviderKind.S3Compatible,
            AuthKind: AuthKind.LongLivedKey,
            CredentialFields: fields,
            DefaultEndpointUrl: null,
            HelpPromptTemplate: "How do I get an API key for {DisplayName}?",
            IconKey: "aws");

        profile.Id.Should().Be("aws-s3");
        profile.DisplayName.Should().Be("AWS S3");
        profile.Kind.Should().Be(CloudProviderKind.S3Compatible);
        profile.AuthKind.Should().Be(AuthKind.LongLivedKey);
        profile.CredentialFields.Should().HaveCount(2);
        profile.DefaultEndpointUrl.Should().BeNull();
        profile.HelpPromptTemplate.Should().Contain("{DisplayName}");
        profile.IconKey.Should().Be("aws");
    }

    // ------------------------------------------------------------------
    // CloudWriteHandle
    // ------------------------------------------------------------------

    [Fact]
    public async Task CloudWriteHandleCompleteInvokesDelegateExactlyOnce()
    {
        var invoked = 0;
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, ct =>
        {
            invoked++;
            return Task.CompletedTask;
        });

        await handle.CompleteAsync();
        invoked.Should().Be(1);

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task CloudWriteHandleSecondCompleteThrowsInvalidOperation()
    {
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        await handle.CompleteAsync();

        var act = async () => await handle.CompleteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task CloudWriteHandleCompleteAfterDisposeThrowsObjectDisposed()
    {
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        await handle.DisposeAsync();

        var act = async () => await handle.CompleteAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task CloudWriteHandleDisposeIsIdempotent()
    {
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        await handle.DisposeAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task CloudWriteHandleDisposeSwallowsStreamErrors()
    {
        var stream = new ThrowingDisposeStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        var act = async () => await handle.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // Test doubles
    // ------------------------------------------------------------------

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                base.Dispose(disposing);
                throw new IOException("simulated dispose failure");
            }
            base.Dispose(disposing);
        }
    }
}
