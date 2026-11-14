// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/PackageFoundationTests.cs
// PURPOSE:  Structural assertions for the Phase 2.1 package additions.
//           Proves the central package management entries exist with the
//           approved versions, that ImageTextify.Platform references both
//           packages without version attributes, that ImageTextify.Core
//           remains package-free, and that both assemblies load at test
//           time.
// RELATION: Phase 2.1 exit-criteria test.
// ============================================================================

using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Phase 2.1 package foundation tests.
/// </summary>
public sealed class PackageFoundationTests
{
    private const string OnnxRuntimeId = "Microsoft.ML.OnnxRuntime";
    private const string OnnxRuntimeVersion = "1.31.0";
    private const string SkiaSharpId = "SkiaSharp";
    private const string SkiaSharpVersion = "3.119.4";

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "FileHarvester.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static XDocument LoadXml(string relativePath)
    {
        var full = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(full).Should().BeTrue("expected " + relativePath + " to exist");
        return XDocument.Load(full);
    }

    private static string? PackageVersionValue(XDocument doc, string id)
    {
        var ns = doc.Root!.Name.Namespace;
        return doc.Root
            .Elements(ns + "ItemGroup")
            .Elements(ns + "PackageVersion")
            .Where(e => (string?)e.Attribute("Include") == id)
            .Select(e => (string?)e.Attribute("Version"))
            .FirstOrDefault();
    }

    private static bool HasPackageReference(XDocument doc, string id)
    {
        var ns = doc.Root!.Name.Namespace;
        return doc.Root
            .Elements(ns + "ItemGroup")
            .Elements(ns + "PackageReference")
            .Any(e => (string?)e.Attribute("Include") == id);
    }

    private static IEnumerable<XElement> AllPackageReferences(XDocument doc)
    {
        var ns = doc.Root!.Name.Namespace;
        return doc.Root
            .Elements(ns + "ItemGroup")
            .Elements(ns + "PackageReference");
    }

    // -----------------------------------------------------------------
    // 1. Central package management declares both packages
    // -----------------------------------------------------------------

    [Fact]
    public void DirectoryPackagesPropsDeclaresOnnxRuntime()
    {
        var doc = LoadXml("Directory.Packages.props");
        PackageVersionValue(doc, OnnxRuntimeId).Should().Be(OnnxRuntimeVersion);
    }

    [Fact]
    public void DirectoryPackagesPropsDeclaresSkiaSharp()
    {
        var doc = LoadXml("Directory.Packages.props");
        PackageVersionValue(doc, SkiaSharpId).Should().Be(SkiaSharpVersion);
    }

    // -----------------------------------------------------------------
    // 2. Versions are concrete, not floating
    // -----------------------------------------------------------------

    [Fact]
    public void OnnxRuntimeVersionIsNotFloating()
    {
        var doc = LoadXml("Directory.Packages.props");
        var v = PackageVersionValue(doc, OnnxRuntimeId);
        v.Should().NotBeNull();
        v!.Should().NotContain("*");
        v.Should().NotContain("[");
        v.Should().NotContain("]");
        v.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void SkiaSharpVersionIsNotFloating()
    {
        var doc = LoadXml("Directory.Packages.props");
        var v = PackageVersionValue(doc, SkiaSharpId);
        v.Should().NotBeNull();
        v!.Should().NotContain("*");
        v.Should().NotContain("[");
        v.Should().NotContain("]");
        v.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    // -----------------------------------------------------------------
    // 3. Platform csproj references both packages, without versions
    // -----------------------------------------------------------------

    [Fact]
    public void PlatformCsprojReferencesOnnxRuntime()
    {
        var doc = LoadXml(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        HasPackageReference(doc, OnnxRuntimeId).Should().BeTrue();
    }

    [Fact]
    public void PlatformCsprojReferencesSkiaSharp()
    {
        var doc = LoadXml(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        HasPackageReference(doc, SkiaSharpId).Should().BeTrue();
    }

    [Fact]
    public void PlatformCsprojPackageReferencesHaveNoVersionAttribute()
    {
        var doc = LoadXml(@"src\ImageTextify.Platform\ImageTextify.Platform.csproj");
        foreach (var reference in AllPackageReferences(doc))
        {
            var versionAttr = reference.Attribute("Version");
            versionAttr.Should().BeNull(
                "central package management requires no Version attribute on PackageReference");
        }
    }

    // -----------------------------------------------------------------
    // 4. Core remains package-free
    // -----------------------------------------------------------------

    [Fact]
    public void CoreCsprojStillHasNoPackageReferences()
    {
        var doc = LoadXml(@"src\ImageTextify.Core\ImageTextify.Core.csproj");
        AllPackageReferences(doc).Should().BeEmpty(
            "ImageTextify.Core is contracts-only and must remain package-free.");
    }

    // -----------------------------------------------------------------
    // 5. Assemblies load at test time
    // -----------------------------------------------------------------

    [Fact]
    public void OnnxRuntimeAssemblyLoads()
    {
        var t = Type.GetType(
            "Microsoft.ML.OnnxRuntime.SessionOptions, Microsoft.ML.OnnxRuntime",
            throwOnError: false);
        t.Should().NotBeNull(
            "the ONNX Runtime managed assembly must be loadable at test time.");
    }

    [Fact]
    public void SkiaSharpAssemblyLoads()
    {
        var t = Type.GetType("SkiaSharp.SKBitmap, SkiaSharp", throwOnError: false);
        t.Should().NotBeNull(
            "the SkiaSharp managed assembly must be loadable at test time.");
    }

    // -----------------------------------------------------------------
    // 6. Versions are recorded in the handover note
    // -----------------------------------------------------------------

    [Fact]
    public void PackageVersionsAreRecordedInFindings()
    {
        // Phase 2.1 records the pinned versions and their rationale in the
        // handover note. This test asserts the record exists after the
        // milestone append is written. It fails until the append lands,
        // which is the point: the finding must be recorded, not inferred.
        var handover = Path.Combine(FindRepoRoot(), "docs", "ImageTextify", "ImageTextify-Handover-Note.md");
        File.Exists(handover).Should().BeTrue();
        var text = File.ReadAllText(handover);
        text.Should().Contain("Microsoft.ML.OnnxRuntime 1.31.0");
        text.Should().Contain("SkiaSharp 3.119.4");
    }
}
