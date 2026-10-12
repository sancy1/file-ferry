// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Dispatch/DispatchRegistrationTests.cs
// PURPOSE:  DI integration tests for the dispatch layer. Builds a real
//           ServiceCollection, calls AddFileHarvester and
//           AddFileHarvesterCloudDispatch, and asserts the resolved
//           types and the end-to-end dispatch behavior.
// RELATION: Exercises App-side composition without constructing the
//           Avalonia shell. Uses real files in a temp directory for the
//           local-path end-to-end test.
// ============================================================================

using System.Text;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Platform.Cloud.Dispatch;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileHarvester.Tests.Cloud.Dispatch;

public sealed class DispatchRegistrationTests : IDisposable
{
    private readonly string _tempRoot;

    public DispatchRegistrationTests()
    {
        _tempRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "file-ferry-dispatch-tests",
            Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private ServiceProvider BuildGraph()
    {
        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(_tempRoot, "logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        services.AddFileHarvesterCloudDispatch();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void FileResolverResolvesAsDispatcher()
    {
        using var provider = BuildGraph();
        var resolver = provider.GetRequiredService<IFileResolver>();
        resolver.Should().BeOfType<DispatchingFileResolver>();
    }

    [Fact]
    public void ContentReaderResolvesAsDispatcher()
    {
        using var provider = BuildGraph();
        var reader = provider.GetRequiredService<IContentReader>();
        reader.Should().BeOfType<DispatchingContentReader>();
    }

    [Fact]
    public void OutputSinkResolvesAsDispatcher()
    {
        using var provider = BuildGraph();
        var sink = provider.GetRequiredService<IOutputSink>();
        sink.Should().BeOfType<DispatchingOutputSink>();
    }

    [Fact]
    public void LocalImplementationsAreTheSameInstanceAcrossInterfaces()
    {
        using var provider = BuildGraph();

        var resolverConcrete = provider.GetRequiredService<FileResolver>();
        var resolverLocal = provider.GetRequiredService<ILocalFileResolver>();
        var resolverPrimary = provider.GetRequiredService<IFileResolver>();

        resolverLocal.Should().BeSameAs(resolverConcrete);
        resolverPrimary.Should().NotBeSameAs(resolverConcrete,
            "the primary interface resolves to the dispatcher, not the local resolver");

        var readerConcrete = provider.GetRequiredService<ContentReader>();
        var readerLocal = provider.GetRequiredService<ILocalContentReader>();
        readerLocal.Should().BeSameAs(readerConcrete);

        var sinkConcrete = provider.GetRequiredService<OutputSink>();
        var sinkLocal = provider.GetRequiredService<ILocalOutputSink>();
        sinkLocal.Should().BeSameAs(sinkConcrete);
    }

    [Fact]
    public async Task LocalPathResolvesEndToEndThroughTheDispatcher()
    {
        var path = Path.Combine(_tempRoot, "hello.txt");
        await File.WriteAllTextAsync(path, "hello", new UTF8Encoding(false));

        using var provider = BuildGraph();
        var resolver = provider.GetRequiredService<IFileResolver>();

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: path),
        };

        var resolved = await resolver.ResolveAsync(requests);

        resolved.Should().HaveCount(1);
        resolved[0].AbsolutePath.Should().Be(path);
    }

    [Fact]
    public async Task CloudPathWithoutProviderProducesNotFoundWithNote()
    {
        using var provider = BuildGraph();
        var resolver = provider.GetRequiredService<IFileResolver>();

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "s3://bucket/key.txt"),
        };

        var ticks = new List<ResolveProgress>();
        var progress = new SyncProgress<ResolveProgress>(p => ticks.Add(p));

        var resolved = await resolver.ResolveAsync(requests, progress);

        resolved.Should().BeEmpty();
        ticks.Should().HaveCount(1);
        ticks[0].Status.Should().Be(FileStatus.NotFound);
        ticks[0].Note.Should().NotBeNullOrWhiteSpace();
        ticks[0].Note.Should().Contain("s3");
    }
}
