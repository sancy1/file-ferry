// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/DropboxOAuthFlowTests.cs
// PURPOSE:  Consumer-driven tests for the Dropbox OAuth flow. Uses a
//           substitute IOAuthHttpClient, a recording browser delegate, a
//           fake listener, and the real OAuthStateStore. No Dropbox
//           account, browser, or network call.
// RELATION: Exercises DropboxOAuthFlow.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>Tests for the Dropbox OAuth flow.</summary>
public sealed class DropboxOAuthFlowTests
{
    private static OAuthClientConfig Config() => new(
        ProviderId: "dropbox",
        ClientId: "test-client-id",
        AuthorizationEndpoint: "https://example.invalid/authorize",
        TokenEndpoint: "https://example.invalid/token",
        Scopes: Array.Empty<string>(),
        RedirectUri: "http://127.0.0.1:53682/callback/");

    private sealed class FakeListener : IOAuthLoopbackListener
    {
        private readonly OAuthCallbackResult _result;
        public FakeListener(OAuthCallbackResult result) { _result = result; }
        public int Port => 53682;
        public Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken ct)
            => Task.FromResult(_result);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static (DropboxOAuthFlow Flow, List<string> Captured, IOAuthHttpClient Http) Build(
        OAuthCallbackResult callback,
        string tokenJson)
    {
        var stateStore = new OAuthStateStore();
        var captured = new List<string>();
        Func<string, Task> openBrowser = url => { captured.Add(url); return Task.CompletedTask; };
        Func<int, string, TimeSpan, IOAuthLoopbackListener> factory =
            (_, _, _) => new FakeListener(callback);
        var http = Substitute.For<IOAuthHttpClient>();
        http.PostFormAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tokenJson));

        var flow = new DropboxOAuthFlow(
            stateStore, openBrowser, factory, http, TimeSpan.FromSeconds(30));
        return (flow, captured, http);
    }

    [Fact]
    public async Task DropboxAuthorizeUrlCarriesTheRequiredParameters()
    {
        var callback = new OAuthCallbackResult(Code: null, State: null, Error: "access_denied");
        var (flow, captured, _) = Build(callback, "{}");

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);
        await act.Should().ThrowAsync<OAuthConsentDeniedException>();

        captured.Should().ContainSingle();
        captured[0].Should().Contain("test-client-id");
        captured[0].Should().Contain("code_challenge=");
        captured[0].Should().Contain("code_challenge_method=S256");
        captured[0].Should().Contain("state=");
        captured[0].Should().Contain("token_access_type=offline");
    }

    [Fact]
    public async Task DropboxAuthorizeMapsConsentDenial()
    {
        var callback = new OAuthCallbackResult(Code: null, State: null, Error: "access_denied");
        var (flow, _, _) = Build(callback, "{}");

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);

        await act.Should().ThrowAsync<OAuthConsentDeniedException>();
    }

    [Fact]
    public async Task DropboxAuthorizeThrowsOnStateMismatch()
    {
        var callback = new OAuthCallbackResult(
            Code: "test-authorization-code",
            State: "deliberately-wrong-state",
            Error: null);
        var (flow, _, _) = Build(callback, "{}");

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);

        await act.Should().ThrowAsync<OAuthStateException>();
    }

    [Fact]
    public async Task DropboxRefreshMapsInvalidGrantToRevoked()
    {
        var http = Substitute.For<IOAuthHttpClient>();
        http.PostFormAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("{\"error\":\"invalid_grant\"}"));

        var flow = new DropboxOAuthFlow(
            new OAuthStateStore(),
            _ => Task.CompletedTask,
            (_, _, _) => new FakeListener(new OAuthCallbackResult(null, null, null)),
            http,
            TimeSpan.FromSeconds(30));

        var act = async () => await flow.RefreshAsync(Config(), "test-refresh-token", CancellationToken.None);

        await act.Should().ThrowAsync<OAuthTokenRevokedException>();
    }

    [Fact]
    public async Task DropboxRevokeCallsTheRevokeEndpoint()
    {
        var http = Substitute.For<IOAuthHttpClient>();
        var flow = new DropboxOAuthFlow(
            new OAuthStateStore(),
            _ => Task.CompletedTask,
            (_, _, _) => new FakeListener(new OAuthCallbackResult(null, null, null)),
            http,
            TimeSpan.FromSeconds(30));

        await flow.RevokeAsync(Config(), "test-refresh-token", CancellationToken.None);

        await http.Received(1).PostFormAsync(
            Arg.Is<string>(u => u.Contains("token/revoke", StringComparison.Ordinal)),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DropboxRevokePropagates4xxAsPermanent()
    {
        var http = Substitute.For<IOAuthHttpClient>();
        http.PostFormAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new OAuthHttpStatusException(401));
        var flow = new DropboxOAuthFlow(
            new OAuthStateStore(),
            _ => Task.CompletedTask,
            (_, _, _) => new FakeListener(new OAuthCallbackResult(null, null, null)),
            http,
            TimeSpan.FromSeconds(30));

        var act = async () => await flow.RevokeAsync(Config(), "test-refresh-token", CancellationToken.None);

        // A 4xx is permanent: the typed exception propagates unchanged,
        // not wrapped in IOException.
        await act.Should().ThrowAsync<OAuthHttpStatusException>()
            .Where(ex => ex.StatusCode == 401);
    }

    [Fact]
    public async Task DropboxRevokeWraps5xxAsTransient()
    {
        var http = Substitute.For<IOAuthHttpClient>();
        http.PostFormAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new OAuthHttpStatusException(503));
        var flow = new DropboxOAuthFlow(
            new OAuthStateStore(),
            _ => Task.CompletedTask,
            (_, _, _) => new FakeListener(new OAuthCallbackResult(null, null, null)),
            http,
            TimeSpan.FromSeconds(30));

        var act = async () => await flow.RevokeAsync(Config(), "test-refresh-token", CancellationToken.None);

        // A 5xx is transient: wrapped in IOException, which the resilience
        // policy classifies as transient.
        await act.Should().ThrowAsync<IOException>()
            .Where(ex => ex.InnerException is OAuthHttpStatusException);
    }

    [Fact]
    public async Task DropboxRevokeWrapsNetworkFailureAsTransient()
    {
        var http = Substitute.For<IOAuthHttpClient>();
        http.PostFormAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("network down"));
        var flow = new DropboxOAuthFlow(
            new OAuthStateStore(),
            _ => Task.CompletedTask,
            (_, _, _) => new FakeListener(new OAuthCallbackResult(null, null, null)),
            http,
            TimeSpan.FromSeconds(30));

        var act = async () => await flow.RevokeAsync(Config(), "test-refresh-token", CancellationToken.None);

        // A network failure is transient: wrapped in IOException.
        await act.Should().ThrowAsync<IOException>()
            .Where(ex => ex.InnerException is HttpRequestException);
    }
}
