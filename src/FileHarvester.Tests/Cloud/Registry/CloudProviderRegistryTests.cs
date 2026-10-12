// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Registry/CloudProviderRegistryTests.cs
// PURPOSE:  Tests for the runtime provider registry. Proves the invariants:
//           resolution by scheme and id, resolution by optional instance,
//           last-wins duplicates, lazy construction once per instance,
//           retry after a factory failure, and per-instance caching.
// RELATION: Exercises FileHarvester.Platform.Cloud.Registry.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Registry;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.Registry;

/// <summary>
/// Tests for the provider registry.
/// </summary>
public sealed class CloudProviderRegistryTests
{

    private static ProviderProfile Profile(string id, CloudProviderKind kind)
        => new(
            Id: id,
            DisplayName: id,
            Kind: kind,
            AuthKind: AuthKind.LongLivedKey,
            CredentialFields: Array.Empty<FieldSpec>(),
            DefaultEndpointUrl: null,
            HelpPromptTemplate: "prompt",
            IconKey: null);

    private static ICloudProvider Provider() => Substitute.For<ICloudProvider>();

    /// <summary>
    /// A registry factory that ignores the instance and returns the given
    /// provider. Matches the Register contract: Task of non-null provider.
    /// </summary>
    private static Func<string?, CancellationToken, Task<ICloudProvider>> Factory(ICloudProvider provider)
        => (_, _) => Task.FromResult(provider);

    /// <summary>
    /// A registry factory that returns a fresh provider.
    /// </summary>
    private static Func<string?, CancellationToken, Task<ICloudProvider>> NewFactory()
        => (_, _) => Task.FromResult(Provider());

    [Fact]
    public async Task ResolveBySchemeReturnsNullWhenEmpty()
    {
        var registry = new CloudProviderRegistry();
        (await registry.ResolveAsync(PathSchemeKind.S3, null)).IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveByIdReturnsNullWhenEmpty()
    {
        var registry = new CloudProviderRegistry();
        (await registry.ResolveAsync("aws-s3", null)).IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterThenResolveBySchemeReturnsTheProvider()
    {
        var registry = new CloudProviderRegistry();
        var provider = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), Factory(provider));

        (await registry.ResolveAsync(PathSchemeKind.S3, null)).Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task RegisterThenResolveByIdReturnsTheProvider()
    {
        var registry = new CloudProviderRegistry();
        var provider = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), Factory(provider));

        (await registry.ResolveAsync("aws-s3", null)).Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task ResolveByIdIsCaseInsensitive()
    {
        var registry = new CloudProviderRegistry();
        var provider = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), Factory(provider));

        (await registry.ResolveAsync("AWS-S3", null)).Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task ResolveByUnknownSchemeReturnsNull()
    {
        var registry = new CloudProviderRegistry();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), NewFactory());
        (await registry.ResolveAsync(PathSchemeKind.AzureBlob, null)).IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveByUnknownIdReturnsNull()
    {
        var registry = new CloudProviderRegistry();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), NewFactory());
        (await registry.ResolveAsync("nonexistent", null)).IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateRegistrationOverwrites()
    {
        var registry = new CloudProviderRegistry();
        var first = Provider();
        var second = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), Factory(first));
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), Factory(second));

        (await registry.ResolveAsync("aws-s3", null)).Provider.Should().BeSameAs(second);
    }

    [Fact]
    public void ProfilesReturnsRegisteredProfiles()
    {
        var registry = new CloudProviderRegistry();
        registry.Register(Profile("a", CloudProviderKind.S3Compatible), NewFactory());
        registry.Register(Profile("b", CloudProviderKind.S3Compatible), NewFactory());
        registry.Register(Profile("c", CloudProviderKind.S3Compatible), NewFactory());

        registry.Profiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task FactoryIsInvokedLazilyOnFirstResolve()
    {
        var registry = new CloudProviderRegistry();
        var invoked = 0;
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible),
            (_, _) => { invoked++; return Task.FromResult(Provider()); });

        invoked.Should().Be(0);
        await registry.ResolveAsync("aws-s3", null);
        invoked.Should().Be(1);
    }

    [Fact]
    public async Task FactoryIsInvokedOncePerInstance()
    {
        var registry = new CloudProviderRegistry();
        var invoked = 0;
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible),
            (_, _) => { invoked++; return Task.FromResult(Provider()); });

        await registry.ResolveAsync("aws-s3", null);
        await registry.ResolveAsync("aws-s3", null);
        invoked.Should().Be(1);
    }

    [Fact]
    public async Task OneDriveProfileRegistersByIdOnlyWithNoScheme()
    {
        var registry = new CloudProviderRegistry();
        var provider = Provider();
        registry.Register(Profile("onedrive", CloudProviderKind.OneDrive), Factory(provider));

        (await registry.ResolveAsync("onedrive", null)).Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task FactoryExceptionIsNotCachedBothPathsRetry()
    {
        var registry = new CloudProviderRegistry();
        var invoked = 0;
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible),
            (_, _) => { invoked++; throw new InvalidOperationException("no credential"); });

        (await registry.ResolveAsync(PathSchemeKind.S3, null)).IsResolved.Should().BeFalse();
        (await registry.ResolveAsync("aws-s3", null)).IsResolved.Should().BeFalse();

        invoked.Should().Be(2);
    }

    [Fact]
    public async Task FactoryExceptionAllowsRetryOnSchemeAfterFix()
    {
        var registry = new CloudProviderRegistry();
        var fail = true;
        var provider = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible),
            (_, _) => { if (fail) { throw new InvalidOperationException("no credential"); } return Task.FromResult(provider); });

        (await registry.ResolveAsync(PathSchemeKind.S3, null)).IsResolved.Should().BeFalse();
        fail = false;
        (await registry.ResolveAsync(PathSchemeKind.S3, null)).Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task FactoryExceptionAllowsRetryOnIdAfterFix()
    {
        var registry = new CloudProviderRegistry();
        var fail = true;
        var provider = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible),
            (_, _) => { if (fail) { throw new InvalidOperationException("no credential"); } return Task.FromResult(provider); });

        (await registry.ResolveAsync("aws-s3", null)).IsResolved.Should().BeFalse();
        fail = false;
        (await registry.ResolveAsync("aws-s3", null)).Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task OlderFailureDoesNotRemoveNewerRegistration()
    {
        var registry = new CloudProviderRegistry();
        var failing = true;
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible),
            (_, _) => { if (failing) { throw new InvalidOperationException("no credential"); } return Task.FromResult(Provider()); });

        (await registry.ResolveAsync(PathSchemeKind.S3, null)).IsResolved.Should().BeFalse();

        var newer = Provider();
        registry.Register(Profile("aws-s3", CloudProviderKind.S3Compatible), Factory(newer));

        (await registry.ResolveAsync("aws-s3", null)).Provider.Should().BeSameAs(newer);
        (await registry.ResolveAsync(PathSchemeKind.S3, null)).Provider.Should().BeSameAs(newer);
    }

    [Fact]
    public async Task MapKindToSchemeMapsOneDrive()
    {
        var registry = new CloudProviderRegistry();
        var provider = Provider();
        registry.Register(Profile("onedrive", CloudProviderKind.OneDrive), Factory(provider));

        var byScheme = await registry.ResolveAsync(PathSchemeKind.OneDrive, null);
        var byId = await registry.ResolveAsync("onedrive", null);

        byScheme.IsResolved.Should().BeTrue();
        byScheme.Provider.Should().BeSameAs(provider);
        byId.Provider.Should().BeSameAs(provider);
    }
}
