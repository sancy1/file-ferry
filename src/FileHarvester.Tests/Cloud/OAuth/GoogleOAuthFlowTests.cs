// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/GoogleOAuthFlowTests.cs
// PURPOSE:  Unit tests for GoogleOAuthFlow using the two injected seams
//           (browser opener, listener factory). The listener factory
//           returns an IOAuthLoopbackListener stub, so no socket is bound
//           and no live Google endpoint is contacted. Covers constructor
//           validation, callback error and state mapping, browser URL
//           capture, and listener-factory invocation.
// RELATION: Exercises FileHarvester.Platform.Cloud.OAuth.GoogleOAuthFlow.
//           NETWORK PATHS NOT COVERED HERE: authorization-code exchange,
//           refresh, revoke, live token acquisition, and provider endpoint
//           behavior. All deferred to C.6.8 [VERIFY-REAL].
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>
/// Tests for <see cref="GoogleOAuthFlow"/> with stubbed seams.
/// </summary>
public sealed class GoogleOAuthFlowTests
{
    private const string ProviderId = "google-drive";
    private const string ClientId = "placeholder-client-id";
    private const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RedirectUri = "http://127.0.0.1:53682/callback/";

    private static OAuthClientConfig Config() => new(
        ProviderId: ProviderId,
        ClientId: ClientId,
        AuthorizationEndpoint: AuthUrl,
        TokenEndpoint: TokenUrl,
        Scopes: new List<string> { OAuthScopes.DriveFile },
        RedirectUri: RedirectUri);

    private static GoogleOAuthFlow Flow(
        OAuthStateStore store,
        Func<string, Task> openBrowser,
        Func<int, string, TimeSpan, IOAuthLoopbackListener> factory,
        TimeSpan? timeout = null)
        => new(store, openBrowser, factory, _ => Task.FromResult<string?>("test-client-secret"), timeout ?? TimeSpan.FromSeconds(10));

    [Fact]
    public void ConstructorRejectsNullStateStore()
    {
        var act = () => new GoogleOAuthFlow(null!, _ => Task.CompletedTask, (p, c, t) => new StubListener("code=c&state=s"), _ => Task.FromResult<string?>("test-client-secret"), TimeSpan.FromSeconds(10));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConstructorRejectsNullBrowserOpener()
    {
        var act = () => new GoogleOAuthFlow(new OAuthStateStore(), null!, (p, c, t) => new StubListener("code=c&state=s"), _ => Task.FromResult<string?>("test-client-secret"), TimeSpan.FromSeconds(10));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConstructorRejectsNullListenerFactory()
    {
        var act = () => new GoogleOAuthFlow(new OAuthStateStore(), _ => Task.CompletedTask, null!, _ => Task.FromResult<string?>("test-client-secret"), TimeSpan.FromSeconds(10));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConstructorRejectsNonPositiveTimeout()
    {
        var act = () => new GoogleOAuthFlow(new OAuthStateStore(), _ => Task.CompletedTask, (p, c, t) => new StubListener("code=c&state=s"), _ => Task.FromResult<string?>("test-client-secret"), TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AuthorizeAsyncMapsConsentDenialToTypedException()
    {
        var store = new OAuthStateStore();
        var flow = Flow(store, _ => Task.CompletedTask, StubFactory("error=access_denied&state=x"));

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);
        await act.Should().ThrowAsync<OAuthConsentDeniedException>();
    }

    [Fact]
    public async Task AuthorizeAsyncMapsOtherCallbackErrorToTypedException()
    {
        var store = new OAuthStateStore();
        var flow = Flow(store, _ => Task.CompletedTask, StubFactory("error=invalid_request&state=x"));

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);
        await act.Should().ThrowAsync<OAuthCallbackException>();
    }

    [Fact]
    public async Task AuthorizeAsyncThrowsWhenCallbackHasNoCodeOrError()
    {
        var store = new OAuthStateStore();
        var flow = Flow(store, _ => Task.CompletedTask, StubFactory("state=only-state"));

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);
        await act.Should().ThrowAsync<OAuthCallbackException>();
    }

    [Fact]
    public async Task AuthorizeAsyncThrowsOnStateMismatch()
    {
        var store = new OAuthStateStore();
        var flow = Flow(store, _ => Task.CompletedTask, StubFactory("code=the-code&state=wrong-state"));

        var act = async () => await flow.AuthorizeAsync(Config(), CancellationToken.None);
        await act.Should().ThrowAsync<OAuthStateException>();
    }

    [Fact]
    public async Task AuthorizeAsyncOpensBrowserWithAuthorizationUrl()
    {
        var store = new OAuthStateStore();
        string? captured = null;
        var flow = Flow(store, url => { captured = url; return Task.CompletedTask; }, StubFactory("error=access_denied&state=x"));

        try { await flow.AuthorizeAsync(Config(), CancellationToken.None); } catch (OAuthConsentDeniedException) { }

        captured.Should().NotBeNull();
        captured.Should().Contain("accounts.google.com");
        captured.Should().Contain("client_id=" + ClientId);
    }

    [Fact]
    public async Task AuthorizeAsyncInvokesListenerFactoryWithPortAndPath()
    {
        var store = new OAuthStateStore();
        int capturedPort = -1;
        string? capturedPath = null;
        Func<int, string, TimeSpan, IOAuthLoopbackListener> factory = (port, path, timeout) =>
        {
            capturedPort = port;
            capturedPath = path;
            return new StubListener("error=access_denied&state=x");
        };
        var flow = Flow(store, _ => Task.CompletedTask, factory);

        try { await flow.AuthorizeAsync(Config(), CancellationToken.None); } catch (OAuthConsentDeniedException) { }

        capturedPort.Should().Be(53682);
        capturedPath.Should().Be("/callback/");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    public async Task AuthorizeAsyncRejectsInvalidRedirectUri(string? redirectUri)
    {
        var store = new OAuthStateStore();
        var config = Config() with { RedirectUri = redirectUri! };
        var flow = Flow(store, _ => Task.CompletedTask, StubFactory("code=c&state=s"));

        var act = async () => await flow.AuthorizeAsync(config, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();
    }

    // ---- Test-only stubs -----------------------------------------------

    private static Func<int, string, TimeSpan, IOAuthLoopbackListener> StubFactory(string query)
        => (port, path, timeout) => new StubListener(query);

    /// <summary>
    /// An IOAuthLoopbackListener stub that returns a prepared callback
    /// result. No socket is bound. The query is parsed into code, state,
    /// and error only, matching the production listener's contract.
    /// </summary>
    private sealed class StubListener : IOAuthLoopbackListener
    {
        private readonly OAuthCallbackResult _result;

        public StubListener(string query)
        {
            string? code = null;
            string? state = null;
            string? error = null;
            foreach (var pair in query.Split((char)38))
            {
                var eq = pair.IndexOf((char)61);
                if (eq < 0) continue;
                var k = pair.Substring(0, eq);
                var v = pair.Substring(eq + 1);
                if (k == "code") code = v;
                else if (k == "state") state = v;
                else if (k == "error") error = v;
            }
            _result = new OAuthCallbackResult(code, state, error);
        }

        public int Port => 0;

        public Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken ct)
            => Task.FromResult(_result);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
