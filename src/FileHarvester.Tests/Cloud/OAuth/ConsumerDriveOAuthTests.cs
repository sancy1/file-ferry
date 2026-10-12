// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/ConsumerDriveOAuthTests.cs
// PURPOSE:  Consumer-driven tests for the Google Drive OAuth orchestration.
//           Drives the real CloudConnectionService with a substitute
//           IOAuthFlow and an in-memory ITypedCredentialStore. No browser,
//           no Google account, no network call.
// RELATION: Exercises CloudConnectionService, OAuthTokenStore, and
//           SubstituteOAuthFlow. Group B (token-store internals) is already
//           covered by OAuthTokenStoreTests and is not duplicated here.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>Consumer-driven Drive OAuth orchestration tests.</summary>
public sealed class ConsumerDriveOAuthTests
{
    private const string ProfileId = "gdrive";
    private const string InstanceName = "work";

    /// <summary>A minimal in-memory ITypedCredentialStore.</summary>
    private sealed class InMemoryTypedStore : ITypedCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public int DeleteCount { get; private set; }

        private static string K(CredentialStoreKey key)
            => key.Kind + "/" + key.ProfileId + "/" + key.InstanceName;

        public Task WriteAsync(CredentialStoreKey key, string secret, CancellationToken cancellationToken = default)
        {
            _values[K(key)] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(CredentialStoreKey key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(K(key), out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(CredentialStoreKey key, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            _values.Remove(K(key));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListInstanceNamesAsync(string profileId, CredentialKind kind, CancellationToken cancellationToken = default)
        {
            var names = new List<string>();
            foreach (var kvp in _values)
            {
                var parts = kvp.Key.Split((char)47);
                if (parts.Length == 3
                    && string.Equals(parts[0], kind.ToString(), StringComparison.Ordinal)
                    && string.Equals(parts[1], profileId, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(parts[2]);
                }
            }
            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    private static OAuthClientConfig Config() => new(
        ProviderId: ProfileId,
        ClientId: "test-client-id",
        AuthorizationEndpoint: "https://example.invalid/auth",
        TokenEndpoint: "https://example.invalid/token",
        Scopes: new[] { OAuthScopes.DriveFile },
        RedirectUri: "http://127.0.0.1:53682/callback/");

    private static CredentialKey Key(string instance = InstanceName)
        => new(ProfileId, instance);

    private static (CloudConnectionService Service, SubstituteOAuthFlow Flow, InMemoryTypedStore Store) Build()
    {
        var flow = new SubstituteOAuthFlow();
        var store = new InMemoryTypedStore();
        var registry = new SingleFlowRegistry(flow);
        var tokenStore = new OAuthTokenStore(store, registry);
        var service = new CloudConnectionService(registry, tokenStore, store);
        return (service, flow, store);
    }

    [Fact]
    public async Task ConnectStoresTheRefreshToken()
    {
        var (service, flow, store) = Build();

        await service.ConnectAsync(Config(), Key(), CancellationToken.None);

        flow.AuthorizeCalls.Should().Be(1);
        var stored = await store.ReadAsync(
            CredentialStoreKey.OAuth(ProfileId, InstanceName), CancellationToken.None);
        stored.Should().Be(SubstituteOAuthFlow.RefreshTokenValue);
    }

    [Fact]
    public async Task ConnectBuildsTheOAuthTypedKey()
    {
        var (service, flow, store) = Build();

        await service.ConnectAsync(Config(), Key(), CancellationToken.None);

        // The OAuth namespace holds the token. The static namespace does not.
        var oauth = await store.ReadAsync(
            CredentialStoreKey.OAuth(ProfileId, InstanceName), CancellationToken.None);
        var staticKey = await store.ReadAsync(
            CredentialStoreKey.Static(ProfileId, InstanceName), CancellationToken.None);

        oauth.Should().NotBeNull();
        staticKey.Should().BeNull();
    }

    [Fact]
    public async Task ListConnectedInstancesReturnsTheConnectedInstance()
    {
        var (service, flow, store) = Build();

        await service.ConnectAsync(Config(), Key(), CancellationToken.None);
        var instances = await service.ListConnectedInstancesAsync(ProfileId, CancellationToken.None);

        instances.Should().ContainSingle();
        instances[0].Should().Be(InstanceName);
    }


    [Fact]
    public async Task DisconnectRevokesThenDeletes()
    {
        var (service, flow, store) = Build();

        await service.ConnectAsync(Config(), Key(), CancellationToken.None);
        await service.DisconnectAsync(Config(), Key(), CancellationToken.None);

        // The service orchestrated a revoke and a delete.
        flow.RevokeCalls.Should().Be(1);
        flow.LastRevokedToken.Should().Be(SubstituteOAuthFlow.RefreshTokenValue);
        store.DeleteCount.Should().Be(1);

        // A later access through the service fails cleanly: no stored token.
        var instances = await service.ListConnectedInstancesAsync(ProfileId, CancellationToken.None);
        instances.Should().BeEmpty();
    }

    [Fact]
    public async Task DisconnectDoesNotAffectAnotherInstance()
    {
        var (service, flow, store) = Build();

        await service.ConnectAsync(Config(), Key("work"), CancellationToken.None);
        await service.ConnectAsync(Config(), Key("personal"), CancellationToken.None);

        await service.DisconnectAsync(Config(), Key("work"), CancellationToken.None);

        var remaining = await service.ListConnectedInstancesAsync(ProfileId, CancellationToken.None);
        remaining.Should().ContainSingle();
        remaining[0].Should().Be("personal");
    }

}
