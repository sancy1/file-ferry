// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/JsonWorkspaceStateStoreTests.cs
// PURPOSE:  Tests for the JSON workspace state store: round-trip, atomic
//           write, corrupt-file quarantine, foreign-key rejection, schema
//           handling, and the in-memory fallback. Uses a real temp dir.
// ============================================================================

using CodeViewer.Core.Domain;
using CodeViewer.Platform.State;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class JsonWorkspaceStateStoreTests : IDisposable
{
    private readonly string _root;
    private readonly IResilienceExecutor _resilience;

    public JsonWorkspaceStateStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cv-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(_root, "logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        _resilience = services.BuildServiceProvider()
            .GetRequiredService<IResilienceExecutor>();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private JsonWorkspaceStateStore Store() =>
        new(new CodeViewerStorageRoot(_root), _resilience);

    private static WorkspaceState Sample(string path) => new()
    {
        ProjectPath = path,
        ProjectName = "Demo",
        OpenedCount = 3,
        DisplayMode = DisplayMode.StructureAndCode,
        AutoDisplay = true,
        ShowSystemFiles = false,
        ExpandedDirectories = { "src", "src/sub" },
        CheckedPaths = { "src/main.py", "README.md" },
        SelectedFilePath = "src/main.py",
        BrowserScrollOffset = 12.5,
        StructureScrollOffset = 4.0,
        CodeScrollOffset = 99.0,
        BrowserWidth = 280,
        StructureRatio = 0.4,
    };

    [Fact]
    public async Task SaveThenLoadRoundTrips()
    {
        var store = Store();
        var state = Sample(@"C:\Projects\Demo");

        (await store.SaveAsync(state)).Should().BeTrue();
        var loaded = await store.LoadAsync(@"C:\Projects\Demo");

        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("Demo");
        loaded.OpenedCount.Should().Be(3);
        loaded.DisplayMode.Should().Be(DisplayMode.StructureAndCode);
        loaded.AutoDisplay.Should().BeTrue();
        loaded.ExpandedDirectories.Should().Equal("src", "src/sub");
        loaded.CheckedPaths.Should().Equal("src/main.py", "README.md");
        loaded.SelectedFilePath.Should().Be("src/main.py");
        loaded.BrowserScrollOffset.Should().Be(12.5);
        loaded.StructureRatio.Should().Be(0.4);
    }

    [Fact]
    public async Task LoadWhenNoFileReturnsNull()
    {
        var store = Store();

        (await store.LoadAsync(@"C:\Projects\Never")).Should().BeNull();
    }

    [Fact]
    public async Task SaveIsAtomicNoTempFileLeftBehind()
    {
        var store = Store();
        await store.SaveAsync(Sample(@"C:\Projects\Demo"));

        Directory.GetFiles(_root, "*.part").Should().BeEmpty();
        Directory.GetFiles(_root, "*.json").Should().HaveCount(1);
    }

    [Fact]
    public async Task CorruptFileIsQuarantinedAndReturnsNull()
    {
        var store = Store();
        var key = ProjectPathNormalizer.ComputeKey(@"C:\Projects\Demo");
        var file = Path.Combine(_root, key + ".json");
        File.WriteAllText(file, "{ not valid json !!!");

        var loaded = await store.LoadAsync(@"C:\Projects\Demo");

        loaded.Should().BeNull();
        File.Exists(file).Should().BeFalse();
        Directory.GetFiles(_root, "*.corrupt-*").Should().HaveCount(1);
    }

    [Fact]
    public async Task ForeignKeyFileIsQuarantinedNotApplied()
    {
        // A file whose embedded path does not match its file name key
        // (for example after a manual copy) must not be applied.
        var store = Store();
        var wrongKey = ProjectPathNormalizer.ComputeKey(@"C:\Projects\Other");
        var file = Path.Combine(_root, wrongKey + ".json");
        var foreign = Sample(@"C:\Projects\Demo");
        File.WriteAllText(file,
            System.Text.Json.JsonSerializer.Serialize(foreign));

        var loaded = await store.LoadAsync(@"C:\Projects\Other");

        loaded.Should().BeNull();
        Directory.GetFiles(_root, "*.corrupt-*").Should().HaveCount(1);
    }

    [Fact]
    public async Task NewerSchemaIsIgnoredButFileKept()
    {
        var store = Store();
        var state = Sample(@"C:\Projects\Demo");
        state.SchemaVersion = WorkspaceState.CurrentSchemaVersion + 5;
        await store.SaveAsync(state);

        var loaded = await store.LoadAsync(@"C:\Projects\Demo");

        loaded.Should().BeNull();
        // File kept for the newer build, not quarantined.
        Directory.GetFiles(_root, "*.json").Should().HaveCount(1);
        Directory.GetFiles(_root, "*.corrupt-*").Should().BeEmpty();
    }

    [Fact]
    public async Task InMemoryFallbackRoundTripsWithoutDisk()
    {
        var store = new JsonWorkspaceStateStore(
            CodeViewerStorageRoot.InMemory, _resilience);

        (await store.SaveAsync(Sample(@"C:\Projects\Demo"))).Should().BeTrue();
        var loaded = await store.LoadAsync(@"C:\Projects\Demo");

        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("Demo");
        // Nothing written to the (nonexistent) disk root.
    }

    [Fact]
    public async Task SaveNormalizesPathBeforeKeying()
    {
        var store = Store();
        await store.SaveAsync(Sample(@"C:\Projects\Demo\"));

        // Loading via a differently-cased / trailing-slash path finds it.
        var loaded = await store.LoadAsync(@"c:\projects\demo");
        loaded.Should().NotBeNull();
    }
}
