// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Discovery/LocalCatalogResolverTests.cs
// PURPOSE:  Tests the provider name discovery: the embedded catalog loads,
//           and the local resolver matches aliases with the right
//           confidence. No UI, no network.
// RELATION: Exercises FileHarvester.Platform.Cloud.Discovery.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Discovery;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Discovery;

/// <summary>
/// Tests for the catalog and the local resolver.
/// </summary>
public sealed class LocalCatalogResolverTests
{
    private static CloudNameCatalog LoadEmbeddedCatalog() => new();

    private static LocalCatalogResolver Resolver(CloudNameCatalog catalog)
        => new(catalog);

    [Fact]
    public void EmbeddedCatalogLoadsElevenEntries()
    {
        var catalog = LoadEmbeddedCatalog();
        catalog.Entries.Should().HaveCount(11);
    }

    [Fact]
    public void EmbeddedCatalogHasTheThreeOAuthProviders()
    {
        var catalog = LoadEmbeddedCatalog();
        var ids = catalog.Entries.Select(e => e.ProviderId).ToList();
        ids.Should().Contain("gdrive");
        ids.Should().Contain("onedrive");
        ids.Should().Contain("dropbox");
    }

    [Fact]
    public async Task GoogleResolvesToGoogleDriveExact()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("Google", CancellationToken.None);
        var gdrive = results.FirstOrDefault(r => r.ProviderId == "gdrive");
        gdrive.Should().NotBeNull();
        gdrive!.Confidence.Should().Be(CloudNameConfidence.Exact);
        gdrive.Source.Should().Be(CloudNameSource.Local);
    }

    [Fact]
    public async Task MicrosoftResolvesToOneDriveExact()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("Microsoft", CancellationToken.None);
        var onedrive = results.FirstOrDefault(r => r.ProviderId == "onedrive");
        onedrive.Should().NotBeNull();
        onedrive!.Confidence.Should().Be(CloudNameConfidence.Exact);
    }

    [Fact]
    public async Task AwsResolvesToS3Exact()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("AWS", CancellationToken.None);
        var s3 = results.FirstOrDefault(r => r.ProviderId == "aws-s3");
        s3.Should().NotBeNull();
        s3!.Confidence.Should().Be(CloudNameConfidence.Exact);
    }

    [Fact]
    public async Task MatchingIsCaseInsensitive()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("GOOGLE", CancellationToken.None);
        results.Any(r => r.ProviderId == "gdrive").Should().BeTrue();
    }

    [Fact]
    public async Task InputIsTrimmed()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("  dropbox  ", CancellationToken.None);
        results.Any(r => r.ProviderId == "dropbox").Should().BeTrue();
    }

    [Fact]
    public async Task EmptyInputReturnsEmpty()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("   ", CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownInputReturnsEmpty()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("nonexistent-provider-xyz", CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderIdIsForwarded()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("gdrive", CancellationToken.None);
        results[0].ProviderId.Should().Be("gdrive");
    }

    [Fact]
    public async Task AuthKindIsForwarded()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("Google Drive", CancellationToken.None);
        results[0].AuthKind.Should().Be(AuthKind.OAuth);
    }

    [Fact]
    public void ExplicitCatalogConstructorIsUsedByTests()
    {
        var entries = new List<CloudNameCatalog.CatalogEntry>
        {
            new(
                CanonicalName: "Test Cloud",
                Aliases: new List<string> { "testcloud", "test cloud" },
                ProviderId: "testcloud",
                AuthKind: AuthKind.LongLivedKey,
                OfficialUrl: null),
        };
        var catalog = new CloudNameCatalog(entries);
        catalog.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExplicitCatalogIsQueriedByTheResolver()
    {
        var entries = new List<CloudNameCatalog.CatalogEntry>
        {
            new(
                CanonicalName: "Test Cloud",
                Aliases: new List<string> { "testcloud", "test cloud" },
                ProviderId: "testcloud",
                AuthKind: AuthKind.LongLivedKey,
                OfficialUrl: null),
        };
        var sut = Resolver(new CloudNameCatalog(entries));
        var results = await sut.ResolveAsync("testcloud", CancellationToken.None);
        results.Should().HaveCount(1);
        results[0].ProviderId.Should().Be("testcloud");
    }

    [Fact]
    public async Task CancelledResolutionThrows()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await sut.ResolveAsync("Google", cts.Token);
        await act.Should().ThrowAsync<System.OperationCanceledException>();
    }

    [Fact]
    public async Task MultipleProvidersMatchASharedWord()
    {
        var sut = Resolver(LoadEmbeddedCatalog());
        var results = await sut.ResolveAsync("cloud", CancellationToken.None);
        results.Should().NotBeEmpty();
    }
}
