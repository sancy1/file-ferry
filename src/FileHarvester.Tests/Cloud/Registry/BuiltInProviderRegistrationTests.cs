// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Registry/BuiltInProviderRegistrationTests.cs
// PURPOSE:  Tests for the public registration facade. Proves that all six
//           built-in providers (S3, Azure Blob, GCS, Google Drive, OneDrive,
//           Dropbox) are registered, that a second registration call does not
//           duplicate profiles, and that a missing OAuth dependency produces
//           the explicit named exception. Uses an in-memory ICredentialStore
//           and substituted OAuth dependencies; never touches Windows
//           Credential Manager and never makes a network call.
// RELATION: Exercises FileHarvester.Platform.Cloud.Registry.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Registry;
using FileHarvester.Tests.Cloud.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Portfolio.Resilience.Extensions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Registry;

/// <summary>
/// Tests for the built-in provider registration facade.
/// </summary>
public sealed class BuiltInProviderRegistrationTests
{
    /// <summary>
    /// A minimal in-memory ICredentialStore. Stores plaintext values keyed
    /// by the composed store key. No persistence, no encryption, no
    /// Windows Credential Manager.
    /// </summary>
    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
        {
            _values[key.ProfileId + "/" + key.InstanceName] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key.ProfileId + "/" + key.InstanceName, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key.ProfileId + "/" + key.InstanceName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListInstanceNamesAsync(string profileId, CancellationToken cancellationToken = default)
        {
            var names = new List<string>();
            foreach (var kvp in _values)
            {
                var parts = kvp.Key.Split((char)47);
                if (parts.Length == 2 && string.Equals(parts[0], profileId, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(parts[1]);
                }
            }

            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialStore, InMemoryCredentialStore>();
        services.AddPortfolioResilience(_ => { });
        services.AddSingleton<ICloudProviderRegistry, CloudProviderRegistry>();
        return services;
    }

    private static (ServiceProvider Provider, ICloudProviderRegistry Registry) Build()
    {
        var services = BaseServices();

        var typedCredentialStore = Substitute.For<ITypedCredentialStore>();
        var flow = new SubstituteOAuthFlow();
        var registry = new SingleFlowRegistry(flow);
        var tokenStore = new OAuthTokenStore(typedCredentialStore, registry);
        var configStore = Substitute.For<IOAuthClientConfigStore>();
        services.AddSingleton(tokenStore);
        services.AddSingleton(configStore);

        var provider = services.BuildServiceProvider();
        BuiltInProviderRegistration.RegisterBuiltInProviders(provider);
        return (provider, provider.GetRequiredService<ICloudProviderRegistry>());
    }

    private static (ServiceProvider Provider, ICloudProviderRegistry Registry) BuildWithoutOAuth()
    {
        var services = BaseServices();
        var provider = services.BuildServiceProvider();
        BuiltInProviderRegistration.RegisterBuiltInProviders(provider);
        return (provider, provider.GetRequiredService<ICloudProviderRegistry>());
    }

    [Fact]
    public void RegistersAllSixProviders()
    {
        var (provider, registry) = Build();
        using (provider)
        {
            registry.Profiles.Should().Contain(p => p.Id == "aws-s3");
            registry.Profiles.Should().Contain(p => p.Id == "azure-blob");
            registry.Profiles.Should().Contain(p => p.Id == "gcs");
            registry.Profiles.Should().Contain(p => p.Id == "gdrive");
            registry.Profiles.Should().Contain(p => p.Id == "onedrive");
            registry.Profiles.Should().Contain(p => p.Id == "dropbox");
        }
    }

    [Fact]
    public void SecondCallDoesNotDuplicateProfiles()
    {
        var (provider, registry) = Build();
        using (provider)
        {
            BuiltInProviderRegistration.RegisterBuiltInProviders(provider);

            registry.Profiles.Should().HaveCount(6);
            registry.Profiles.Count(p => p.Id == "aws-s3").Should().Be(1);
            registry.Profiles.Count(p => p.Id == "azure-blob").Should().Be(1);
            registry.Profiles.Count(p => p.Id == "gcs").Should().Be(1);
            registry.Profiles.Count(p => p.Id == "gdrive").Should().Be(1);
            registry.Profiles.Count(p => p.Id == "onedrive").Should().Be(1);
            registry.Profiles.Count(p => p.Id == "dropbox").Should().Be(1);
        }
    }

    [Fact]
    public void ThrowsWhenOAuthDependenciesMissing()
    {
        var services = BaseServices();
        using var provider = services.BuildServiceProvider();

        var act = () => BuiltInProviderRegistration.RegisterBuiltInProviders(provider);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OAuth dependencies are missing*OAuthTokenStore*IOAuthClientConfigStore*");
    }

    [Fact]
    public void FindByIdReturnsNullForUnknownProfile()
    {
        BuiltInProfiles.FindById("does-not-exist").Should().BeNull();
    }

    [Fact]
    public async Task S3SchemeResolvesTheS3Registration()
    {
        var (provider, registry) = Build();
        using (provider)
        {
            var resolved = await registry.ResolveAsync(PathSchemeKind.S3, "test-instance", CancellationToken.None);
            resolved.IsResolved.Should().BeFalse();
            resolved.Failure.Should().Be(CloudResolutionFailure.UnknownInstance);
        }
    }

    [Fact]
    public async Task AzureSchemeResolvesTheAzureRegistration()
    {
        var (provider, registry) = Build();
        using (provider)
        {
            var resolved = await registry.ResolveAsync(PathSchemeKind.AzureBlob, "test-instance", CancellationToken.None);
            resolved.IsResolved.Should().BeFalse();
            resolved.Failure.Should().Be(CloudResolutionFailure.UnknownInstance);
        }
    }

    [Fact]
    public async Task GcsSchemeResolvesTheGcsRegistration()
    {
        var (provider, registry) = Build();
        using (provider)
        {
            var resolved = await registry.ResolveAsync(PathSchemeKind.Gcs, "test-instance", CancellationToken.None);
            resolved.IsResolved.Should().BeFalse();
            resolved.Failure.Should().Be(CloudResolutionFailure.UnknownInstance);
        }
    }
}
