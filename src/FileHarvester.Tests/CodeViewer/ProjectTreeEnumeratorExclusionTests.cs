// filepath: src/FileHarvester.Tests/CodeViewer/ProjectTreeEnumeratorExclusionTests.cs
// ============================================================================
// PURPOSE:  Tests for the exclusion-parameter integration in
//           ProjectTreeEnumerator. Proves that:
//             - an excluded folder is never yielded (so its contents are
//               never enumerated downstream),
//             - an excluded file is never yielded,
//             - differently-named folders and files are still yielded,
//             - null / empty exclusions preserve today's behavior exactly,
//             - existing OS-clutter exclusion still works alongside user
//               exclusions.
// ============================================================================

using CodeViewer.Core.Domain;
using CodeViewer.Platform.FileSystem;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class ProjectTreeEnumeratorExclusionTests : IDisposable
{
    private readonly string _rootDir;
    private readonly ProjectTreeEnumerator _enumerator;

    private static readonly string[] AllEntries =
        { "backend", "frontend", "node_modules", "README.md", "main.py" };

    private static readonly string[] BackendFrontendEntries =
        { "backend", "frontend" };

    public ProjectTreeEnumeratorExclusionTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "cv-enum-excl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);

        // Fixture: backend/, frontend/, node_modules/, README.md, main.py
        Directory.CreateDirectory(Path.Combine(_rootDir, "backend"));
        Directory.CreateDirectory(Path.Combine(_rootDir, "frontend"));
        Directory.CreateDirectory(Path.Combine(_rootDir, "node_modules"));
        File.WriteAllText(Path.Combine(_rootDir, "README.md"), "#");
        File.WriteAllText(Path.Combine(_rootDir, "main.py"), "#");

        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(Path.GetTempPath(), "cv-enum-logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        var resilience = services.BuildServiceProvider().GetRequiredService<IResilienceExecutor>();
        _enumerator = new ProjectTreeEnumerator(resilience);
    }

    public void Dispose()
    {
        try { Directory.Delete(_rootDir, recursive: true); } catch { }
    }

    private ProjectNode RootNode()
        => new(NodeKind.Directory, Path.GetFileName(_rootDir.TrimEnd(Path.DirectorySeparatorChar)), _rootDir, null);

    private async Task<List<ProjectNode>> EnumerateAsync(GlobalExclusions? exclusions)
    {
        var results = new List<ProjectNode>();
        await foreach (var child in _enumerator.EnumerateChildrenAsync(
            RootNode(), showSystemFiles: false, exclusions: exclusions))
        {
            results.Add(child);
        }
        return results;
    }

    [Fact]
    public async Task NullExclusionsYieldsAllEntries()
    {
        var results = await EnumerateAsync(null);
        results.Select(r => r.Name).Should().BeEquivalentTo(AllEntries);
    }

    [Fact]
    public async Task EmptyExclusionsYieldsAllEntries()
    {
        var results = await EnumerateAsync(GlobalExclusions.Empty);
        results.Select(r => r.Name).Should().BeEquivalentTo(AllEntries);
    }

    [Fact]
    public async Task ExcludedFolderIsNotYielded()
    {
        var exclusions = GlobalExclusions.Empty.Add(new ExclusionRule("node_modules", ExclusionKind.Folder));
        var results = await EnumerateAsync(exclusions);

        results.Select(r => r.Name).Should().NotContain("node_modules",
            "an excluded folder must never appear in the enumeration, so its contents are never enumerated");
        results.Select(r => r.Name).Should().Contain(BackendFrontendEntries);
    }

    [Fact]
    public async Task ExcludedFileIsNotYielded()
    {
        var exclusions = GlobalExclusions.Empty.Add(new ExclusionRule("README.md", ExclusionKind.File));
        var results = await EnumerateAsync(exclusions);

        results.Select(r => r.Name).Should().NotContain("README.md");
        results.Select(r => r.Name).Should().Contain("main.py");
    }

    [Fact]
    public async Task FolderRuleExcludesFolderButNotDifferentlyNamedFiles()
    {
        // A file and a folder cannot share a name in the same directory on
        // Windows. The domain-level folder/file independence is already
        // proven in GlobalExclusionsTests.FolderAndFileWithSameNameAreIndependent.
        // Here we prove the enumerator-level property that is physically
        // constructible: a Folder rule excludes the real folder of that
        // name but leaves differently-named files untouched.
        var exclusions = GlobalExclusions.Empty
            .Add(new ExclusionRule("node_modules", ExclusionKind.Folder));
        var results = await EnumerateAsync(exclusions);

        results.Should().NotContain(
            r => r.Name == "node_modules" && r.Kind == NodeKind.Directory,
            "the Folder rule must exclude the real node_modules directory");
        results.Should().Contain(
            r => r.Name == "main.py" && r.Kind == NodeKind.File,
            "the Folder rule must not affect a differently-named file");
        results.Should().Contain(
            r => r.Name == "README.md" && r.Kind == NodeKind.File,
            "the Folder rule must not affect a differently-named file");
    }

    [Fact]
    public async Task DifferentNamesAreUnaffected()
    {
        var exclusions = GlobalExclusions.Empty
            .Add(new ExclusionRule("node_modules", ExclusionKind.Folder))
            .Add(new ExclusionRule("README.md", ExclusionKind.File));
        var results = await EnumerateAsync(exclusions);

        results.Select(r => r.Name).Should().BeEquivalentTo("backend", "frontend", "main.py");
    }

    [Fact]
    public async Task ExistingOsClutterExclusionStillWorks()
    {
        // .DS_Store is OS clutter and must be hidden by the existing rule even
        // when user exclusions are null.
        File.WriteAllText(Path.Combine(_rootDir, ".DS_Store"), "x");
        var results = await EnumerateAsync(null);
        results.Select(r => r.Name).Should().NotContain(".DS_Store");
    }
}