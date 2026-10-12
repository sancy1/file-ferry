// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/CloudPathTests.cs
// PURPOSE:  Unit tests for CloudPath. Covers URI-shaped paths, Windows
//           drive-letter paths, UNC paths, POSIX paths, mixed separators,
//           and the null/whitespace rules. All tests are host-OS independent.
// RELATION: Consumes FileHarvester.Core.Domain.CloudPath. No DI, no I/O.
// ============================================================================

using FileHarvester.Core.Domain;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud;

/// <summary>
/// Unit tests for <see cref="CloudPath"/>.
/// </summary>
public sealed class CloudPathTests
{
    // ------------------------------------------------------------------
    // GetFileName
    // ------------------------------------------------------------------

    [Fact]
    public void GetFileNameS3Path()
    {
        CloudPath.GetFileName("s3://bucket/docs/a.txt").Should().Be("a.txt");
    }

    [Fact]
    public void GetFileNameS3NoSlash()
    {
        CloudPath.GetFileName("s3://bucket").Should().Be("bucket");
    }

    [Fact]
    public void GetFileNameS3TrailingSlash()
    {
        CloudPath.GetFileName("s3://bucket/docs/").Should().BeEmpty();
    }

    [Fact]
    public void GetFileNameFileUriPath()
    {
        CloudPath.GetFileName("file:///tmp/a.txt").Should().Be("a.txt");
    }

    [Fact]
    public void GetFileNameLocalWindows()
    {
        CloudPath.GetFileName(@"C:\a\b.txt").Should().Be("b.txt");
    }

    [Fact]
    public void GetFileNameLocalMixedSeparators()
    {
        CloudPath.GetFileName(@"C:\a/b\c.txt").Should().Be("c.txt");
    }

    [Fact]
    public void GetFileNameUncPath()
    {
        CloudPath.GetFileName(@"\\server\share\file.txt").Should().Be("file.txt");
    }

    [Fact]
    public void GetFileNamePosixPath()
    {
        CloudPath.GetFileName("/var/tmp/a.txt").Should().Be("a.txt");
    }

    // ------------------------------------------------------------------
    // GetDirectoryName
    // ------------------------------------------------------------------

    [Fact]
    public void GetDirectoryNameS3BucketOnly()
    {
        CloudPath.GetDirectoryName("s3://bucket").Should().BeEmpty();
    }

    [Fact]
    public void GetDirectoryNameS3WithKey()
    {
        CloudPath.GetDirectoryName("s3://bucket/key").Should().Be("s3://bucket");
    }

    [Fact]
    public void GetDirectoryNameS3WithNestedPath()
    {
        CloudPath.GetDirectoryName("s3://bucket/a/b.txt").Should().Be("s3://bucket/a");
    }

    [Fact]
    public void GetDirectoryNameFileUriWithFile()
    {
        CloudPath.GetDirectoryName("file:///tmp/a.txt").Should().Be("file:///tmp");
    }

    [Fact]
    public void GetDirectoryNameFileUriNoFile()
    {
        CloudPath.GetDirectoryName("file:///tmp").Should().Be("file://");
    }

    [Fact]
    public void GetDirectoryNameLocalWindows()
    {
        CloudPath.GetDirectoryName(@"C:\a\b.txt").Should().Be(@"C:\a");
    }

    [Fact]
    public void GetDirectoryNameUncPath()
    {
        CloudPath.GetDirectoryName(@"\\server\share\file.txt").Should().Be(@"\\server\share");
    }

    [Fact]
    public void GetDirectoryNamePosixPath()
    {
        CloudPath.GetDirectoryName("/var/tmp/a.txt").Should().Be("/var/tmp");
    }

    // ------------------------------------------------------------------
    // Combine
    // ------------------------------------------------------------------

    [Fact]
    public void CombineS3SchemeOnlyWithLeadingSlash()
    {
        CloudPath.Combine("s3://bucket", "/key").Should().Be("s3://bucket/key");
    }

    [Fact]
    public void CombineS3SchemeOnlyNoLeadingSlash()
    {
        CloudPath.Combine("s3://bucket", "key").Should().Be("s3://bucket/key");
    }

    [Fact]
    public void CombineS3Paths()
    {
        CloudPath.Combine("s3://bucket/a", "b/c.txt").Should().Be("s3://bucket/a/b/c.txt");
    }

    [Fact]
    public void CombineS3TrailingSlashBase()
    {
        CloudPath.Combine("s3://bucket/a/", "b/c.txt").Should().Be("s3://bucket/a/b/c.txt");
    }

    [Fact]
    public void CombineFileUriWithLeadingSlash()
    {
        CloudPath.Combine("file:///tmp", "/a.txt").Should().Be("file:///tmp/a.txt");
    }

    [Fact]
    public void CombineLocalWindows()
    {
        CloudPath.Combine(@"C:\a", "b.txt").Should().Be(@"C:\a\b.txt");
    }

    [Fact]
    public void CombineLocalDuplicateSeparators()
    {
        CloudPath.Combine(@"C:\a\\", "b.txt").Should().Be(@"C:\a\b.txt");
    }

    [Fact]
    public void CombineLocalAbsoluteDriveArgWins()
    {
        CloudPath.Combine(@"C:\a", @"D:\b.txt").Should().Be(@"D:\b.txt");
    }

    [Fact]
    public void CombineLocalAbsoluteUncArgWins()
    {
        CloudPath.Combine(@"C:\a", @"\\server\share\x").Should().Be(@"\\server\share\x");
    }

    [Fact]
    public void CombineNullBaseReturnsRelative()
    {
        CloudPath.Combine(null, "x").Should().Be("x");
    }

    // ------------------------------------------------------------------
    // EnsureTrailingSlash
    // ------------------------------------------------------------------

    [Fact]
    public void EnsureTrailingSlashS3Appends()
    {
        CloudPath.EnsureTrailingSlash("s3://bucket/a").Should().Be("s3://bucket/a/");
    }

    [Fact]
    public void EnsureTrailingSlashS3AlreadyHas()
    {
        CloudPath.EnsureTrailingSlash("s3://bucket/a/").Should().Be("s3://bucket/a/");
    }

    [Fact]
    public void EnsureTrailingSlashFileUri()
    {
        CloudPath.EnsureTrailingSlash("file:///tmp").Should().Be("file:///tmp/");
    }

    // ------------------------------------------------------------------
    // IsDirectoryShaped
    // ------------------------------------------------------------------

    [Fact]
    public void IsDirectoryShapedS3WithSlash()
    {
        CloudPath.IsDirectoryShaped("s3://bucket/docs/").Should().BeTrue();
    }

    [Fact]
    public void IsDirectoryShapedS3WithoutSlash()
    {
        CloudPath.IsDirectoryShaped("s3://bucket/docs").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // GetFileNameWithoutExtension
    // ------------------------------------------------------------------

    [Fact]
    public void GetFileNameWithoutExtensionLocalWindows()
    {
        CloudPath.GetFileNameWithoutExtension(@"C:\a\b.txt").Should().Be("b");
    }

    [Fact]
    public void GetFileNameWithoutExtensionS3Path()
    {
        CloudPath.GetFileNameWithoutExtension("s3://bucket/docs/a.txt").Should().Be("a");
    }

    [Fact]
    public void GetFileNameWithoutExtensionNoExtension()
    {
        CloudPath.GetFileNameWithoutExtension("s3://bucket/docs/readme").Should().Be("readme");
    }

    [Fact]
    public void GetFileNameWithoutExtensionLeadingDot()
    {
        CloudPath.GetFileNameWithoutExtension(@"C:\a\.gitignore").Should().Be(".gitignore");
    }

    [Fact]
    public void GetFileNameWithoutExtensionMultipleDots()
    {
        CloudPath.GetFileNameWithoutExtension(@"C:\a\archive.tar.gz").Should().Be("archive.tar");
    }

    [Fact]
    public void GetFileNameWithoutExtensionNullReturnsEmpty()
    {
        CloudPath.GetFileNameWithoutExtension(null).Should().BeEmpty();
    }
}
