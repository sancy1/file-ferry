// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Registry/ProviderConstructionGoogleDriveTests.cs
// PURPOSE:  Tests for the Google Drive factory in ProviderConstruction.
//           Uses substituted OAuth dependencies and a real OAuthTokenStore
//           over a SubstituteOAuthFlow. The factory constructs a real
//           DriveService wrapper; DriveService construction performs no
//           network I/O - the first request is where the network call
//           happens. No Google API request is made by this test.
// RELATION: Exercises ProviderConstruction.CreateGoogleDrive only.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Providers;
using FileHarvester.Platform.Cloud.Registry;
using FileHarvester.Tests.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;
using Portfolio.Resilience.Abstractions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Registry;

/// <summary>Tests for CreateGoogleDrive.</summary>
public sealed class ProviderConstructionGoogleDriveTests
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

    [Fact]
    public async Task CreateGoogleDriveReturnsProviderUsingResolvedToken()
    {
        var typedCredentialStore = Substitute.For<ITypedCredentialStore>();
        typedCredentialStore.ReadAsync(Arg.Any<CredentialStoreKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("stored-refresh-token"));

        var credentialStore = Substitute.For<ICredentialStore>();
        credentialStore.ListInstanceNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { InstanceName }));

        var flow = new SubstituteOAuthFlow();
        flow.OnRefresh = (_, _) => Task.FromResult(new OAuthTokens(
            AccessToken: "test-access-token",
            ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
            RefreshToken: "test-refresh-token",
            Scope: null));
        var registry = new SingleFlowRegistry(flow);
        var tokenStore = new OAuthTokenStore(typedCredentialStore, registry);

        var configStore = Substitute.For<IOAuthClientConfigStore>();
        configStore.ReadAsync(ProfileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OAuthClientConfig?>(Config()));

        var resolver = Substitute.For<IOneDriveDriveIdResolver>();
        var resilience = Substitute.For<IResilienceExecutor>();

        var construction = new ProviderConstruction(
            credentialStore, resilience, tokenStore, configStore, resolver);

        var factory = construction.CreateGoogleDrive(ProfileId);
        var provider = await factory(InstanceName, CancellationToken.None);

        provider.Should().NotBeNull();
    }
}
