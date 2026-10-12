using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;

namespace FileHarvester.Tests.Cloud.OAuth;

public sealed class GoogleDrivePickerOAuthFlowTests
{
    [Fact]
    public void CallbackParsesCodeScopeAndCommaSeparatedIds()
    {
        var callback = GoogleDrivePickerCallback.Parse(
            "?code=auth-code&state=nonce&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file&picked_file_ids=file-one%2Cfile-two");

        callback.Code.Should().Be("auth-code");
        callback.State.Should().Be("nonce");
        callback.Scope.Should().Be(OAuthScopes.DriveFile);
        callback.FileIds.Should().Equal("file-one", "file-two");
    }

    [Fact]
    public void CallbackParsesSingleId()
    {
        var callback = GoogleDrivePickerCallback.Parse(
            "?code=auth-code&state=nonce&scope=scope&picked_file_ids=file-one");

        callback.FileIds.Should().ContainSingle().Which.Should().Be("file-one");
    }

    [Theory]
    [InlineData("?picked_file_ids=file-one,,file-two")]
    [InlineData("?picked_file_ids=file-one%2Cfile-one")]
    [InlineData("?picked_file_ids=file-one&picked_file_ids=file-two")]
    public void CallbackRejectsMalformedOrDuplicateIds(string query)
    {
        var act = () => GoogleDrivePickerCallback.Parse(query);

        act.Should().Throw<OAuthCallbackException>()
            .WithMessage("*invalid_picker_response*");
    }

    [Fact]
    public void PickerParametersPreserveExistingAuthorizationParametersAndRedirect()
    {
        const string authorizationUrl =
            "https://accounts.google.com/o/oauth2/v2/auth?client_id=client&state=old-sdk-state&redirect_uri=http%3A%2F%2F127.0.0.1%3A53682%2Fcallback%2F&scope=drive.file";

        var result = GoogleDrivePickerOAuthFlow.AddPickerParameters(authorizationUrl, "issued-state");
        var uri = new Uri(result);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        uri.GetLeftPart(UriPartial.Path).Should().Be("https://accounts.google.com/o/oauth2/v2/auth");
        query["client_id"].Should().Be("client");
        query["redirect_uri"].Should().Be("http://127.0.0.1:53682/callback/");
        query["scope"].Should().Be("drive.file");
        query.GetValues("state").Should().ContainSingle().Which.Should().Be("issued-state");
        query["prompt"].Should().Be("consent");
        query["trigger_onepick"].Should().Be("true");
        query["allow_multiple"].Should().Be("true");
    }

    [Fact]
    public async Task PickerAuthorizationRequestRequiresOnlyDriveFile()
    {
        var config = new OAuthClientConfig(
            "gdrive",
            "client",
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            new List<string> { OAuthScopes.DriveFile, "openid" },
            "http://127.0.0.1:53682/callback/");

        var flow = new GoogleDrivePickerOAuthFlow(
            new OAuthStateStore(),
            _ => System.Threading.Tasks.Task.CompletedTask,
            (port, path, timeout) => new GoogleDrivePickerLoopbackListener(port, path, timeout),
            _ => System.Threading.Tasks.Task.FromResult<string?>("secret"),
            TimeSpan.FromSeconds(5));

        Func<Task> act = () => flow.AuthorizeAsync(config, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*drive.file scope only*");
    }

    [Fact]
    public async Task AuthorizeAsyncExchangesPickerCodeWithStoredVerifierAndDoesNotReturnRefreshToken()
    {
        var config = new OAuthClientConfig(
            "gdrive",
            "client",
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            new List<string> { OAuthScopes.DriveFile },
            "http://127.0.0.1:53682/callback/");
        var listener = new StubListener();
        var stateStore = new OAuthStateStore();
        var browserUrl = string.Empty;
        string? exchangedCode = null;
        string? exchangedVerifier = null;
        string? exchangedSecret = null;
        var flow = new GoogleDrivePickerOAuthFlow(
            stateStore,
            url =>
            {
                browserUrl = url;
                var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
                listener.SetCallback(GoogleDrivePickerCallback.Parse(
                    "?code=picker-code&state=" + Uri.EscapeDataString(query["state"]!)
                    + "&scope=" + Uri.EscapeDataString(OAuthScopes.DriveFile)
                    + "&picked_file_ids=id-one%2Cid-two"));
                return Task.CompletedTask;
            },
            (_, _, _) => listener,
            _ => Task.FromResult<string?>("client-secret"),
            TimeSpan.FromSeconds(5),
            (_, _) => new GooglePickerAuthorizationRequest(
                "https://accounts.google.com/o/oauth2/v2/auth?client_id=client&redirect_uri=http%3A%2F%2F127.0.0.1%3A53682%2Fcallback%2F&scope=drive.file",
                "pkce-verifier"),
            (_, secret, code, verifier, _) =>
            {
                exchangedSecret = secret;
                exchangedCode = code;
                exchangedVerifier = verifier;
                return Task.FromResult(new GoogleDrivePickerAccessToken(
                    "picker-access-token",
                    DateTime.UtcNow.AddMinutes(10),
                    OAuthScopes.DriveFile));
            });

        var grant = await flow.AuthorizeAsync(config, CancellationToken.None);
        var browserQuery = System.Web.HttpUtility.ParseQueryString(new Uri(browserUrl).Query);

        grant.AccessToken.Should().Be("picker-access-token");
        grant.FileIds.Should().Equal("id-one", "id-two");
        exchangedSecret.Should().Be("client-secret");
        exchangedCode.Should().Be("picker-code");
        exchangedVerifier.Should().Be("pkce-verifier");
        browserQuery["prompt"].Should().Be("consent");
        browserQuery["trigger_onepick"].Should().Be("true");
        browserQuery["redirect_uri"].Should().Be(config.RedirectUri);
    }

    [Fact]
    public async Task AuthorizeAsyncRejectsStateMismatchBeforeExchangingCode()
    {
        var listener = new StubListener();
        var exchangeCalled = false;
        var (flow, config) = CreateTestFlow(
            listener,
            _ =>
            {
                listener.SetCallback(GoogleDrivePickerCallback.Parse(
                    "?code=picker-code&state=not-the-issued-state&scope="
                    + Uri.EscapeDataString(OAuthScopes.DriveFile)
                    + "&picked_file_ids=file-id"));
            },
            (_, _, _, _, _) =>
            {
                exchangeCalled = true;
                return Task.FromResult(new GoogleDrivePickerAccessToken(
                    "access-token", DateTime.UtcNow.AddMinutes(5), OAuthScopes.DriveFile));
            });

        var act = () => flow.AuthorizeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<OAuthStateException>();
        exchangeCalled.Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizeAsyncHandlesPickerDenialWithoutExchangingCode()
    {
        var listener = new StubListener();
        var exchangeCalled = false;
        var (flow, config) = CreateTestFlow(
            listener,
            url =>
            {
                var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
                listener.SetCallback(GoogleDrivePickerCallback.Parse(
                    "?state=" + Uri.EscapeDataString(query["state"]!)
                    + "&error=access_denied"));
            },
            (_, _, _, _, _) =>
            {
                exchangeCalled = true;
                return Task.FromResult(new GoogleDrivePickerAccessToken(
                    "access-token", DateTime.UtcNow.AddMinutes(5), OAuthScopes.DriveFile));
            });

        var act = () => flow.AuthorizeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<OAuthConsentDeniedException>();
        exchangeCalled.Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizeAsyncRejectsUnexpectedGrantedScopes()
    {
        var listener = new StubListener();
        var (flow, config) = CreateTestFlow(
            listener,
            url =>
            {
                var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
                listener.SetCallback(GoogleDrivePickerCallback.Parse(
                    "?code=picker-code&state=" + Uri.EscapeDataString(query["state"]!)
                    + "&scope=" + Uri.EscapeDataString(OAuthScopes.DriveFile)
                    + "&picked_file_ids=file-id"));
            },
            (_, _, _, _, _) => Task.FromResult(new GoogleDrivePickerAccessToken(
                "access-token",
                DateTime.UtcNow.AddMinutes(5),
                OAuthScopes.DriveFile + " openid")));

        var act = () => flow.AuthorizeAsync(config, CancellationToken.None);

        await act.Should().ThrowAsync<OAuthCallbackException>()
            .WithMessage("*picker_scope_missing*");
    }

    private static (GoogleDrivePickerOAuthFlow Flow, OAuthClientConfig Config) CreateTestFlow(
        StubListener listener,
        Action<string> onOpenBrowser,
        Func<OAuthClientConfig, string, string, string, CancellationToken, Task<GoogleDrivePickerAccessToken>> exchange)
    {
        var config = new OAuthClientConfig(
            "gdrive",
            "client",
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            new[] { OAuthScopes.DriveFile },
            "http://127.0.0.1:53682/callback/");
        var flow = new GoogleDrivePickerOAuthFlow(
            new OAuthStateStore(),
            url =>
            {
                onOpenBrowser(url);
                return Task.CompletedTask;
            },
            (_, _, _) => listener,
            _ => Task.FromResult<string?>("client-secret"),
            TimeSpan.FromSeconds(5),
            (_, _) => new GooglePickerAuthorizationRequest(
                "https://accounts.google.com/o/oauth2/v2/auth?client_id=client&redirect_uri=http%3A%2F%2F127.0.0.1%3A53682%2Fcallback%2F&scope=drive.file",
                "pkce-verifier"),
            exchange);
        return (flow, config);
    }

    private sealed class StubListener : IGoogleDrivePickerCallbackListener
    {
        private readonly TaskCompletionSource<GoogleDrivePickerCallback> _callback =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GoogleDrivePickerCallback> WaitAsync(CancellationToken ct) =>
            _callback.Task.WaitAsync(ct);

        public void SetCallback(GoogleDrivePickerCallback callback) =>
            _callback.TrySetResult(callback);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
