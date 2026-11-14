// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/ProjectPathNormalizerTests.cs
// PURPOSE:  Unit tests for project path normalization and persistence keys:
//           casing, separators, trailing separators, and key stability.
// ============================================================================

using CodeViewer.Platform.State;
using FluentAssertions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class ProjectPathNormalizerTests
{
    [Fact]
    public void TrailingSeparatorIsRemoved()
    {
        var a = ProjectPathNormalizer.Normalize(@"C:\Projects\Demo\");
        var b = ProjectPathNormalizer.Normalize(@"C:\Projects\Demo");

        a.Should().Be(b);
    }

    [Fact]
    public void ForwardSlashesAreUnified()
    {
        var a = ProjectPathNormalizer.Normalize("C:/Projects/Demo");
        var b = ProjectPathNormalizer.Normalize(@"C:\Projects\Demo");

        a.Should().Be(b);
    }

    [Fact]
    public void CasingMapsToSameKeyOnCaseInsensitiveFileSystem()
    {
        if (OperatingSystem.IsLinux())
        {
            return; // case-sensitive; distinct paths
        }

        var a = ProjectPathNormalizer.ComputeKey(@"c:\projects\demo");
        var b = ProjectPathNormalizer.ComputeKey(@"C:\Projects\Demo");

        a.Should().Be(b);
    }

    [Fact]
    public void KeyIsStableLowercaseHex()
    {
        var key = ProjectPathNormalizer.ComputeKey(@"C:\Projects\Demo");

        key.Should().HaveLength(64);
        key.Should().MatchRegex("^[0-9a-f]{64}$");
        ProjectPathNormalizer.ComputeKey(@"C:\Projects\Demo").Should().Be(key);
    }

    [Fact]
    public void DifferentPathsHaveDifferentKeys()
    {
        ProjectPathNormalizer.ComputeKey(@"C:\Projects\A")
            .Should().NotBe(ProjectPathNormalizer.ComputeKey(@"C:\Projects\B"));
    }

    [Fact]
    public void NormalizeForDisplayPreservesCasing()
    {
        var display = ProjectPathNormalizer.NormalizeForDisplay(@"C:\Projects\Demo\");

        display.Should().EndWith("Demo");
        display.Should().NotEndWith(Path.DirectorySeparatorChar.ToString());
    }

    [Fact]
    public void DriveRootIsNotTrimmedToEmpty()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!; // e.g. C:\
        var normalized = ProjectPathNormalizer.Normalize(root);

        normalized.Should().NotBeNullOrEmpty();
    }
}
