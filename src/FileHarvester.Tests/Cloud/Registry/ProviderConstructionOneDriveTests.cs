// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Registry/ProviderConstructionOneDriveTests.cs
// PURPOSE:  Tests for the F-136 drive-id resolution in
//           ProviderConstruction.CreateOneDrive. Uses substituted
//           IOneDriveDriveIdResolver, IOAuthClientConfigStore, and
//           ITypedCredentialStore, and a real OAuthTokenStore over a
//           SubstituteOAuthFlow. No Graph client is constructed by the
//           test; the factory constructs one in-memory per call.
// RELATION: Exercises ProviderConstruction.CreateOneDrive only.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Providers;
using FileHarvester.Platform.Cloud.Registry;
using FluentAssertions;
using FileHarvester.Tests.Cloud.OAuth;
using NSubstitute;
using Portfolio.Resilience.Abstractions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Registry;

/// <summary>Tests for CreateOneDrive F-136 resolution.</summary>
public sealed class ProviderConstructionOneDriveTests
{
    private const string ProfileId = "onedrive";
    private const string InstanceName = "work";

    private static OAuthClientConfig Config() => new(
        ProviderId: ProfileId,
        ClientId: "test-client-id",
        AuthorizationEndpoint: "https://example.invalid/auth",
        TokenEndpoint: "https://example.invalid/token",
        Scopes: new[] { OAuthScopes.DriveFile },
        RedirectUri: "http://127.0.0.1:53682/callback/");

    private static OAuthTokens Tokens(DateTime? expiry = null) => new(
        AccessToken: "test-access-token",
        ExpiresAtUtc: expiry ?? DateTime.UtcNow.AddHours(1),
        RefreshToken: "test-refresh-token",
        Scope: null);

    private static (ProviderConstruction Construction, IOneDriveDriveIdResolver Resolver, IOAuthClientConfigStore ConfigStore, SubstituteOAuthFlow Flow) Build()
    {
        var typedCredentialStore = Substitute.For<ITypedCredentialStore>();
        typedCredentialStore.ReadAsync(Arg.Any<CredentialStoreKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("stored-refresh-token"));
        var credentialStore = Substitute.For<ICredentialStore>();
        credentialStore.ListInstanceNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { InstanceName }));
        var flow = new SubstituteOAuthFlow();
        var registry = new SingleFlowRegistry(flow);
        var tokenStore = new OAuthTokenStore(typedCredentialStore, registry);
        var configStore = Substitute.For<IOAuthClientConfigStore>();
        configStore.ReadAsync(ProfileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OAuthClientConfig?>(Config()));
        var resolver = Substitute.For<IOneDriveDriveIdResolver>();
        var resilience = Substitute.For<IResilienceExecutor>();
        var construction = new ProviderConstruction(credentialStore, resilience, tokenStore, configStore, resolver);
        return (construction, resolver, configStore, flow);
    }

    [Fact]
    public async Task CreateOneDriveUsesTheResolvedDriveId()
    {
        var (construction, resolver, _, flow) = Build();
        flow.OnRefresh = (_, _) => Task.FromResult(Tokens());
        resolver.ResolveAsync(Arg.Any<IDefaultDriveIdProvider>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("drive-id-1"));

        var factory = construction.CreateOneDrive(ProfileId);
        var provider = await factory(InstanceName, CancellationToken.None);

        provider.Should().NotBeNull();
        await resolver.Received(1).ResolveAsync(
            Arg.Any<IDefaultDriveIdProvider>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOneDrivePropagatesResolverFailure()
    {
        var (construction, resolver, _, flow) = Build();
        flow.OnRefresh = (_, _) => Task.FromResult(Tokens());
        resolver.ResolveAsync(Arg.Any<IDefaultDriveIdProvider>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("no drive"));

        var factory = construction.CreateOneDrive(ProfileId);
        var act = async () => await factory(InstanceName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateOneDriveThrowsWhenOAuthConfigMissing()
    {
        var (construction, _, configStore, _) = Build();
        configStore.ReadAsync(ProfileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OAuthClientConfig?>(null));

        var factory = construction.CreateOneDrive(ProfileId);
        var act = async () => await factory(InstanceName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateOneDriveThrowsWhenTokenExpiryIsDefault()
    {
        var (construction, _, _, flow) = Build();
        flow.OnRefresh = (_, _) => Task.FromResult(Tokens(DateTime.MinValue));

        var factory = construction.CreateOneDrive(ProfileId);
        var act = async () => await factory(InstanceName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateOneDrivePropagatesCancellation()
    {
        var (construction, _, _, _) = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var factory = construction.CreateOneDrive(ProfileId);
        var act = async () => await factory(InstanceName, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
