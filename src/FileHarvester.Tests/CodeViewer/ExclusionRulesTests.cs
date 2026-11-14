// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/ExclusionRulesTests.cs
// PURPOSE:  Unit tests for the OS-clutter exclusion list: only true OS
//           metadata is hidden by default, project directories are never
//           hidden, and the Show-system-files toggle reveals everything.
// ============================================================================

using CodeViewer.Platform.FileSystem;
using FluentAssertions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class ExclusionRulesTests
{
    [Theory]
    [InlineData(".DS_Store")]
    [InlineData("Thumbs.db")]
    [InlineData("desktop.ini")]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("System Volume Information")]
    public void OsClutterIsHiddenByDefault(string name)
    {
        ExclusionRules.IsHidden(name, showSystemFiles: false).Should().BeTrue();
    }

    [Theory]
    [InlineData("__pycache__")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".env")]
    [InlineData("main.py")]
    public void ProjectEntriesAreNeverHidden(string name)
    {
        ExclusionRules.IsHidden(name, showSystemFiles: false).Should().BeFalse();
    }

    [Fact]
    public void ShowSystemFilesRevealsOsClutter()
    {
        ExclusionRules.IsHidden("Thumbs.db", showSystemFiles: true).Should().BeFalse();
        ExclusionRules.IsHidden(".DS_Store", showSystemFiles: true).Should().BeFalse();
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        ExclusionRules.IsHidden("thumbs.DB", showSystemFiles: false).Should().BeTrue();
    }
}
