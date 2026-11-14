// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/CoreProjectShapeTests.cs
// PURPOSE:  Structural claims test for the ImageTextify project skeleton.
//           Asserts the two new csproj files declare net10.0 / ImplicitUsings
//           / Nullable, and that ImageTextify.Platform references exactly
//           ImageTextify.Core and FileHarvester.Platform. Also asserts the
//           solution file lists both new projects.
// RELATION: Executable documentation for Phase 1.1. Reads the actual csproj
//           and slnx bytes at test time; never trusts memory of them.
// ============================================================================

using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Asserts the shape of the ImageTextify project skeleton matches the
/// SchemaLence precedent: same TFM, same language properties, and the
/// same dependency direction (Platform -> Core, Platform -> shared platform).
/// </summary>
public sealed class CoreProjectShapeTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var slnx = Path.Combine(dir, "FileHarvester.slnx");
            if (File.Exists(slnx))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static XDocument LoadCsproj(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue(
            "expected the project file to exist at " + relativePath);
        return XDocument.Load(path);
    }

    private static string? PropertyValue(XDocument doc, string name)
    {
        var ns = doc.Root!.Name.Namespace;
        return doc.Root
            .Elements(ns + "PropertyGroup")
            .Elements(ns + name)
            .Select(e => e.Value.Trim())
            .FirstOrDefault();
    }

    private static string[] ProjectReferences(XDocument doc)
    {
        var ns = doc.Root!.Name.Namespace;
        return doc.Root
            .Elements(ns + "ItemGroup")
            .Elements(ns + "ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToArray();
    }

    private static string[] PackageReferences(XDocument doc)
    {
        var ns = doc.Root!.Name.Namespace;
        return doc.Root
            .Elements(ns + "ItemGroup")
            .Elements(ns + "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToArray();
    }

    // -----------------------------------------------------------------
    // ImageTextify.Core
    // -----------------------------------------------------------------

    [Fact]
    public void CoreProjectDeclaresNet10()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Core\ImageTextify.Core.csproj");
        PropertyValue(doc, "TargetFramework").Should().Be("net10.0");
    }

    [Fact]
    public void CoreProjectEnablesNullableAndImplicitUsings()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Core\ImageTextify.Core.csproj");
        PropertyValue(doc, "Nullable").Should().Be("enable");
        PropertyValue(doc, "ImplicitUsings").Should().Be("enable");
    }

    [Fact]
    public void CoreProjectHasNoProjectReferences()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Core\ImageTextify.Core.csproj");
        ProjectReferences(doc).Should().BeEmpty(
            "ImageTextify.Core is a contracts-only project and must not depend on any other project.");
    }

    [Fact]
    public void CoreProjectHasNoPackageReferences()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Core\ImageTextify.Core.csproj");
        PackageReferences(doc).Should().BeEmpty(
            "Phase 1.1 introduces no packages; additions require their own approval gate.");
    }

    // -----------------------------------------------------------------
    // ImageTextify.Platform
    // -----------------------------------------------------------------

    [Fact]
    public void PlatformProjectDeclaresNet10()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        PropertyValue(doc, "TargetFramework").Should().Be("net10.0");
    }

    [Fact]
    public void PlatformProjectEnablesNullableAndImplicitUsings()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        PropertyValue(doc, "Nullable").Should().Be("enable");
        PropertyValue(doc, "ImplicitUsings").Should().Be("enable");
    }

    [Fact]
    public void PlatformProjectReferencesApprovedProjects()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        var refs = ProjectReferences(doc);

        refs.Should().HaveCount(3, "Phase 1.5a adds FileHarvester.Core to the approved reference set.");
        refs.Should().Contain(r => r.EndsWith(@"ImageTextify.Core\ImageTextify.Core.csproj", StringComparison.OrdinalIgnoreCase));
        refs.Should().Contain(r => r.EndsWith(@"FileHarvester.Platform\FileHarvester.Platform.csproj", StringComparison.OrdinalIgnoreCase));
        refs.Should().Contain(r => r.EndsWith(@"FileHarvester.Core\FileHarvester.Core.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlatformProjectHasApprovedPackageReferences()
    {
        var doc = LoadCsproj(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        var refs = PackageReferences(doc);

        refs.Should().Contain("Microsoft.ML.OnnxRuntime");
        refs.Should().Contain("SkiaSharp");
        refs.Should().Contain("Microsoft.Extensions.Http");
    }

    // -----------------------------------------------------------------
    // Solution file lists both new projects
    // -----------------------------------------------------------------

    [Fact]
    public void SolutionListsBothImageTextifyProjects()
    {
        var slnxPath = Path.Combine(FindRepoRoot(), "FileHarvester.slnx");
        File.Exists(slnxPath).Should().BeTrue();
        var text = File.ReadAllText(slnxPath);

        text.Should().Contain("src/ImageTextify.Core/ImageTextify.Core.csproj");
        text.Should().Contain("src/ImageTextify.Platform/ImageTextify.Platform.csproj");
    }
}

