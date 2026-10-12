// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/CloudConnectionServiceTests.cs
// PURPOSE:  Contract tests for CloudConnectionService: the null-argument
//           guards on all three methods, and the OAuth-kind query the
//           list operation performs. The revoke-then-delete ordering,
//           the revoke-failure token preservation, and cross-instance
//           isolation are already covered by ConsumerDriveOAuthTests and
//           OAuthTokenStoreTests and are not duplicated here.
// RELATION: Exercises CloudConnectionService directly with substituted
//           IOAuthFlowRegistry and ITypedCredentialStore, and a real
//           OAuthTokenStore over the substitutes.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>Contract tests for CloudConnectionService.</summary>
public sealed class CloudConnectionServiceTests
{
    private const string ProfileId = "gdrive";
    private const string InstanceName = "work";

    private static OAuthClientConfig Config() => new(
        ProviderId: ProfileId,
        ClientId: "test-client-id",
        AuthorizationEndpoint: "https://example.invalid/auth",
        TokenEndpoint: "https://example.invalid/token",
        Scopes: new[] { OAuthScopes.DriveFile },
        RedirectUri: "http://127.0.0.1:53682/callback/");

    private static CredentialKey Key(string instance = InstanceName)
        => new(ProfileId, instance);

    private static CloudConnectionService BuildService(
        ITypedCredentialStore? credentialStore = null)
    {
        var store = credentialStore ?? Substitute.For<ITypedCredentialStore>();
        var flow = new SubstituteOAuthFlow();
        var registry = new SingleFlowRegistry(flow);
        var tokenStore = new OAuthTokenStore(store, registry);
        return new CloudConnectionService(registry, tokenStore, store);
    }

    [Fact]
    public async Task DisconnectAsyncRejectsNullConfig()
    {
        var service = BuildService();

        var act = async () => await service.DisconnectAsync(null!, Key(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DisconnectAsyncRejectsNullKey()
    {
        var service = BuildService();

        var act = async () => await service.DisconnectAsync(Config(), null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ConnectAsyncRejectsNullConfig()
    {
        var service = BuildService();

        var act = async () => await service.ConnectAsync(null!, Key(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ConnectAsyncRejectsNullKey()
    {
        var service = BuildService();

        var act = async () => await service.ConnectAsync(Config(), null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ListConnectedInstancesAsyncRejectsNullProviderId()
    {
        var service = BuildService();

        var act = async () => await service.ListConnectedInstancesAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ListConnectedInstancesAsyncRejectsEmptyProviderId()
    {
        var service = BuildService();

        var act = async () => await service.ListConnectedInstancesAsync(string.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ListConnectedInstancesAsyncQueriesOAuthNamespace()
    {
        var store = Substitute.For<ITypedCredentialStore>();
        store.ListInstanceNamesAsync(ProfileId, CredentialKind.OAuth, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { InstanceName }));
        store.ListInstanceNamesAsync(ProfileId, CredentialKind.Static, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        var service = BuildService(store);

        var instances = await service.ListConnectedInstancesAsync(ProfileId, CancellationToken.None);

        instances.Should().ContainSingle().Which.Should().Be(InstanceName);
        await store.Received(1).ListInstanceNamesAsync(
            ProfileId, CredentialKind.OAuth, Arg.Any<CancellationToken>());
        await store.DidNotReceive().ListInstanceNamesAsync(
            ProfileId, CredentialKind.Static, Arg.Any<CancellationToken>());
    }
}
