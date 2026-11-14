// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/FileRoleClassifierTests.cs
// PURPOSE:  Unit tests for the file-role labels shown on code cards.
// ============================================================================

using CodeViewer.Core.Domain;
using FluentAssertions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class FileRoleClassifierTests
{
    [Theory]
    [InlineData("main.py", "Python Source")]
    [InlineData("Program.cs", "C# Source")]
    [InlineData("app.ts", "TypeScript Source")]
    [InlineData("index.html", "HTML Document")]
    [InlineData("styles.css", "CSS Stylesheet")]
    [InlineData("data.json", "JSON Document")]
    [InlineData("README.md", "Markdown Document")]
    [InlineData("query.sql", "SQL Script")]
    [InlineData("build.ps1", "PowerShell Script")]
    [InlineData("View.axaml", "Avalonia XAML")]
    public void ClassifyKnownExtensionsReturnsRole(string file, string expected)
    {
        FileRoleClassifier.Classify(file).Should().Be(expected);
    }

    [Fact]
    public void ClassifyUnknownExtensionFallsBackToTextFile()
    {
        FileRoleClassifier.Classify("data.xyzq").Should().Be("Text File");
    }

    [Fact]
    public void ClassifyNoExtensionFallsBackToTextFile()
    {
        FileRoleClassifier.Classify("LICENSE").Should().Be("Text File");
    }

    [Fact]
    public void ClassifyDockerfileByName()
    {
        FileRoleClassifier.Classify(@"C:\proj\Dockerfile").Should().Be("Dockerfile");
    }

    [Fact]
    public void ClassifyIsCaseInsensitive()
    {
        FileRoleClassifier.Classify("MAIN.PY").Should().Be("Python Source");
    }

    [Fact]
    public void ClassifyFullPathUsesFileName()
    {
        FileRoleClassifier.Classify(@"C:\projects\Demo\src\util.py").Should().Be("Python Source");
    }
}
