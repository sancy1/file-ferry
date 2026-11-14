// filepath: src/FileHarvester.Tests/CodeViewer/JsonGlobalExclusionStoreTests.cs
// ============================================================================
// PURPOSE:  Tests for JsonGlobalExclusionStore and GlobalExclusionsHolder.
//           Covers: save->load roundtrip, in-memory fallback, corrupt-file
//           quarantine (never deleted), missing-file empty, backup file
//           written before overwrite, and holder Changed event semantics.
// ============================================================================

using System.Text.Json;
using CodeViewer.Core.Domain;
using CodeViewer.Platform.State;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class JsonGlobalExclusionStoreTests : IDisposable
{
    private readonly string _rootDir;
    private readonly CodeViewerStorageRoot _storageRoot;

    public JsonGlobalExclusionStoreTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "cv-excl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
        _storageRoot = new CodeViewerStorageRoot(_rootDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_rootDir, recursive: true); } catch { }
    }

    private static IResilienceExecutor CreateResilience()
    {
        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(Path.GetTempPath(), "cv-excl-logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        return services.BuildServiceProvider().GetRequiredService<IResilienceExecutor>();
    }

    private JsonGlobalExclusionStore CreateStore()
        => new(_storageRoot, CreateResilience());

    [Fact]
    public async Task MissingFileYieldsEmpty()
    {
        var store = CreateStore();
        var loaded = await store.LoadAsync();
        loaded.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAndLoadRoundtrip()
    {
        var store = CreateStore();
        var rules = GlobalExclusions.Empty
            .Add(new ExclusionRule("node_modules", ExclusionKind.Folder))
            .Add(new ExclusionRule("README.md", ExclusionKind.File));
        var saved = await store.SaveAsync(rules);
        saved.Should().BeTrue();

        var loaded = await store.LoadAsync();
        loaded.Rules.Should().HaveCount(2);
        loaded.IsExcluded("node_modules", ExclusionKind.Folder).Should().BeTrue();
        loaded.IsExcluded("README.md", ExclusionKind.File).Should().BeTrue();
    }

    [Fact]
    public async Task BackupFileWrittenOnOverwrite()
    {
        var store = CreateStore();
        await store.SaveAsync(GlobalExclusions.Empty
            .Add(new ExclusionRule("first", ExclusionKind.Folder)));
        await store.SaveAsync(GlobalExclusions.Empty
            .Add(new ExclusionRule("second", ExclusionKind.File)));

        var backupPath = Path.Combine(_rootDir, JsonGlobalExclusionStore.BackupFileName);
        File.Exists(backupPath).Should().BeTrue("backup must be written before each successful overwrite");
        var backupText = await File.ReadAllTextAsync(backupPath);
        backupText.Should().Contain("first", "backup must contain the previous generation");
    }

    [Fact]
    public async Task CorruptFileIsQuarantinedNotDeleted()
    {
        var file = Path.Combine(_rootDir, JsonGlobalExclusionStore.FileName);
        await File.WriteAllTextAsync(file, "{ this is not valid json ");

        var store = CreateStore();
        var loaded = await store.LoadAsync();
        loaded.IsEmpty.Should().BeTrue();

        var quarantineFiles = Directory.GetFiles(_rootDir, "*.corrupt-*");
        quarantineFiles.Should().NotBeEmpty("corrupt file must be quarantined, never deleted");

        var contents = await File.ReadAllTextAsync(quarantineFiles[0]);
        contents.Should().Contain("this is not valid json", "quarantine preserves the original bytes");
    }

    [Fact]
    public async Task NewerSchemaIsIgnoredNotApplied()
    {
        var file = Path.Combine(_rootDir, JsonGlobalExclusionStore.FileName);
        await File.WriteAllTextAsync(file,
            "{\"SchemaVersion\": 999, \"Rules\": [{\"Name\": \"x\", \"Kind\": \"Folder\"}]}");

        var store = CreateStore();
        var loaded = await store.LoadAsync();
        loaded.IsEmpty.Should().BeTrue("newer schema version is ignored, not applied");

        File.Exists(file).Should().BeTrue("newer schema file is preserved, not quarantined");
    }

    [Fact]
    public async Task InMemoryFallbackRoundtripsWithinSession()
    {
        var inMemoryRoot = CodeViewerStorageRoot.InMemory;
        var store = new JsonGlobalExclusionStore(inMemoryRoot, CreateResilience());

        var rules = GlobalExclusions.Empty
            .Add(new ExclusionRule("x", ExclusionKind.Folder));
        (await store.SaveAsync(rules)).Should().BeTrue();

        var loaded = await store.LoadAsync();
        loaded.IsExcluded("x", ExclusionKind.Folder).Should().BeTrue(
            "in-memory fallback must roundtrip within a single session");
    }

    [Fact]
    public void HolderReplaceFiresChanged()
    {
        var holder = new GlobalExclusionsHolder();
        GlobalExclusions? received = null;
        holder.Changed += (_, rules) => received = rules;

        var rules = GlobalExclusions.Empty.Add(new ExclusionRule("x", ExclusionKind.File));
        holder.Replace(rules);

        received.Should().BeSameAs(rules);
        holder.Current.Should().BeSameAs(rules);
    }

    [Fact]
    public void HolderReplaceWithSameInstanceDoesNotFire()
    {
        var holder = new GlobalExclusionsHolder();
        var rules = GlobalExclusions.Empty.Add(new ExclusionRule("x", ExclusionKind.File));
        holder.Replace(rules);

        var fired = 0;
        holder.Changed += (_, _) => fired++;
        holder.Replace(rules);

        fired.Should().Be(0, "replacing with the same instance must not fire Changed");
    }
}