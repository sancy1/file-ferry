// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/PathSchemeTests.cs
// PURPOSE:  Unit tests for PathScheme.Parse and its convenience predicates.
//           Covers every recognized scheme, every unrecognized input,
//           case-insensitivity, and the safe Local default.
// RELATION: Consumes FileHarvester.Core.Domain.PathScheme. No DI, no I/O;
//           pure in-memory tests.
// ============================================================================

using FileHarvester.Core.Domain;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud;

/// <summary>
/// Unit tests for <see cref="PathScheme"/>.
/// </summary>
public sealed class PathSchemeTests
{
    // ------------------------------------------------------------------
    // Parse - recognized schemes
    // ------------------------------------------------------------------

    [Fact]
    public void ParseFileUriReturnsFileKind()
    {
        var info = PathScheme.Parse("file:///C:/Users/HP/file.txt");
        info.Kind.Should().Be(PathSchemeKind.File);
        info.Scheme.Should().Be("file");
        info.Rest.Should().Be("/C:/Users/HP/file.txt");
    }

    [Fact]
    public void ParseS3PathReturnsS3Kind()
    {
        var info = PathScheme.Parse("s3://my-bucket/docs/a.txt");
        info.Kind.Should().Be(PathSchemeKind.S3);
        info.Scheme.Should().Be("s3");
        info.Rest.Should().Be("my-bucket/docs/a.txt");
    }

    [Fact]
    public void ParseS3BucketOnlyReturnsS3Kind()
    {
        var info = PathScheme.Parse("s3://bucket");
        info.Kind.Should().Be(PathSchemeKind.S3);
        info.Rest.Should().Be("bucket");
    }

    [Fact]
    public void ParseS3EmptyRestReturnsEmptyString()
    {
        var info = PathScheme.Parse("s3://");
        info.Kind.Should().Be(PathSchemeKind.S3);
        info.Rest.Should().BeEmpty();
    }

    [Fact]
    public void ParseGcsPathReturnsGcsKind()
    {
        var info = PathScheme.Parse("gs://bucket/key");
        info.Kind.Should().Be(PathSchemeKind.Gcs);
        info.Scheme.Should().Be("gs");
        info.Rest.Should().Be("bucket/key");
    }

    [Fact]
    public void ParseGcsEmptyRestReturnsEmptyString()
    {
        var info = PathScheme.Parse("gs://");
        info.Kind.Should().Be(PathSchemeKind.Gcs);
        info.Rest.Should().BeEmpty();
    }

    [Fact]
    public void ParseGoogleDrivePathReturnsDriveKind()
    {
        var info = PathScheme.Parse("gdrive://file/1a2b3c");
        info.Kind.Should().Be(PathSchemeKind.GoogleDrive);
        info.Scheme.Should().Be("gdrive");
        info.Rest.Should().Be("file/1a2b3c");
    }

    [Fact]
    public void ParseAzureBlobPathReturnsAzureKind()
    {
        var info = PathScheme.Parse("azure://container/blob");
        info.Kind.Should().Be(PathSchemeKind.AzureBlob);
        info.Scheme.Should().Be("azure");
        info.Rest.Should().Be("container/blob");
    }

    [Fact]
    public void ParseDropboxPathReturnsDropboxKind()
    {
        var info = PathScheme.Parse("dropbox:///docs/a.txt");
        info.Kind.Should().Be(PathSchemeKind.Dropbox);
        info.Scheme.Should().Be("dropbox");
        info.Rest.Should().Be("/docs/a.txt");
    }

    [Fact]
    public void ParseDropboxEmptyRestReturnsEmptyString()
    {
        var info = PathScheme.Parse("dropbox://");
        info.Kind.Should().Be(PathSchemeKind.Dropbox);
        info.Rest.Should().BeEmpty();
    }

    [Fact]
    public void ParseUppercaseSchemeIsCaseInsensitive()
    {
        var info = PathScheme.Parse("S3://bucket/key");
        info.Kind.Should().Be(PathSchemeKind.S3);
        info.Scheme.Should().Be("s3");
        info.Rest.Should().Be("bucket/key");
    }

    // ------------------------------------------------------------------
    // Parse - unrecognized inputs
    // ------------------------------------------------------------------

    [Fact]
    public void ParseWindowsAbsolutePathReturnsLocal()
    {
        var info = PathScheme.Parse(@"C:\Users\HP\file.txt");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Scheme.Should().BeEmpty();
        info.Rest.Should().Be(@"C:\Users\HP\file.txt");
    }

    [Fact]
    public void ParseHttpSchemeIsNotRecognized()
    {
        var info = PathScheme.Parse("HTTP://example.com");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().Be("HTTP://example.com");
    }

    [Fact]
    public void ParseFtpSchemeIsNotRecognized()
    {
        var info = PathScheme.Parse("ftp://server/file");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().Be("ftp://server/file");
    }

    [Fact]
    public void ParseEmptyStringReturnsLocal()
    {
        var info = PathScheme.Parse(string.Empty);
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Scheme.Should().BeEmpty();
        info.Rest.Should().BeEmpty();
    }

    [Fact]
    public void ParseWhitespaceReturnsLocalWithEmptyRest()
    {
        var info = PathScheme.Parse("   ");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().BeEmpty();
    }

    [Fact]
    public void ParseNullReturnsLocal()
    {
        var info = PathScheme.Parse(null);
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().BeEmpty();
    }

    [Fact]
    public void ParseSingleSlashIsNotAScheme()
    {
        var info = PathScheme.Parse("s3:/bucket");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().Be("s3:/bucket");
    }

    [Fact]
    public void ParseNoSlashIsNotAScheme()
    {
        var info = PathScheme.Parse("s3:bucket");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().Be("s3:bucket");
    }

    [Fact]
    public void ParseUncPathReturnsLocal()
    {
        var info = PathScheme.Parse(@"\\server\share\file");
        info.Kind.Should().Be(PathSchemeKind.Local);
    }

    [Fact]
    public void ParseUnixPathReturnsLocal()
    {
        var info = PathScheme.Parse("/unix/path");
        info.Kind.Should().Be(PathSchemeKind.Local);
    }

    [Fact]
    public void ParseTildePathReturnsLocal()
    {
        var info = PathScheme.Parse("~/home/file");
        info.Kind.Should().Be(PathSchemeKind.Local);
    }

    [Fact]
    public void ParseBareFilenameReturnsLocal()
    {
        var info = PathScheme.Parse("a.txt");
        info.Kind.Should().Be(PathSchemeKind.Local);
        info.Rest.Should().Be("a.txt");
    }

    [Fact]
    public void ParseTrimsTrailingWhitespace()
    {
        var info = PathScheme.Parse("s3://bucket/key   ");
        info.Kind.Should().Be(PathSchemeKind.S3);
        info.Rest.Should().Be("bucket/key");
    }

    // ------------------------------------------------------------------
    // IsLocal
    // ------------------------------------------------------------------

    [Fact]
    public void IsLocalWindowsPathReturnsTrue()
    {
        PathScheme.IsLocal(@"C:\Users\HP\file.txt").Should().BeTrue();
    }

    [Fact]
    public void IsLocalS3PathReturnsFalse()
    {
        PathScheme.IsLocal("s3://bucket/key").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // IsCloudPath
    // ------------------------------------------------------------------

    [Fact]
    public void IsCloudPathWindowsPathReturnsFalse()
    {
        PathScheme.IsCloudPath(@"C:\Users\HP\file.txt").Should().BeFalse();
    }

    [Fact]
    public void IsCloudPathS3PathReturnsTrue()
    {
        PathScheme.IsCloudPath("s3://bucket/key").Should().BeTrue();
    }

    [Fact]
    public void IsCloudPathFileUriReturnsFalse()
    {
        PathScheme.IsCloudPath("file:///C:/x.txt").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // HasScheme
    // ------------------------------------------------------------------

    [Fact]
    public void HasSchemeLocalPathReturnsFalse()
    {
        PathScheme.HasScheme(@"C:\Users\HP\file.txt").Should().BeFalse();
    }

    [Fact]
    public void HasSchemeS3PathReturnsTrue()
    {
        PathScheme.HasScheme("s3://bucket/key").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // GetSchemePrefix
    // ------------------------------------------------------------------

    [Fact]
    public void GetSchemePrefixLocalReturnsNull()
    {
        PathScheme.GetSchemePrefix(PathSchemeKind.Local).Should().BeNull();
    }

    [Fact]
    public void GetSchemePrefixS3ReturnsS3()
    {
        PathScheme.GetSchemePrefix(PathSchemeKind.S3).Should().Be("s3");
    }

    [Fact]
    public void GetSchemePrefixAzureReturnsAzure()
    {
        PathScheme.GetSchemePrefix(PathSchemeKind.AzureBlob).Should().Be("azure");
    }

    // ------------------------------------------------------------------
    // GetKind
    // ------------------------------------------------------------------

    [Fact]
    public void GetKindLowercaseReturnsS3()
    {
        PathScheme.GetKind("s3").Should().Be(PathSchemeKind.S3);
    }

    [Fact]
    public void GetKindUppercaseReturnsS3()
    {
        PathScheme.GetKind("S3").Should().Be(PathSchemeKind.S3);
    }

    [Fact]
    public void GetKindUnknownReturnsLocal()
    {
        PathScheme.GetKind("unknown").Should().Be(PathSchemeKind.Local);
    }

    [Fact]
    public void GetKindNullReturnsLocal()
    {
        PathScheme.GetKind(null).Should().Be(PathSchemeKind.Local);
    }

    [Fact]
    public void ParseOneDrivePathReturnsOneDriveKind()
    {
        var info = PathScheme.Parse("onedrive://item-id");

        info.Kind.Should().Be(PathSchemeKind.OneDrive);
        info.Scheme.Should().Be("onedrive");
        info.Rest.Should().Be("item-id");
        info.Instance.Should().BeNull();
    }

    [Fact]
    public void ParseOneDriveWithInstanceReturnsOneDriveAndInstance()
    {
        var info = PathScheme.Parse("onedrive://[work]/item-id");

        info.Kind.Should().Be(PathSchemeKind.OneDrive);
        info.Instance.Should().Be("work");
        info.Rest.Should().Be("item-id");
    }

    [Fact]
    public void ParseOneDriveUnclosedBracketThrows()
    {
        var act = () => PathScheme.Parse("onedrive://[work/item-id");

        act.Should().Throw<FormatException>();
    }
}
