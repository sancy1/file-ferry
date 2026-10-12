// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/GoogleOAuthFlowBrowserTests.cs
// PURPOSE:  Consumer-driven test for the browser-launch seam. Constructs
//           the real GoogleOAuthFlow with a recording openBrowser delegate
//           and a fake listener, and asserts the authorization URL the
//           flow handed to the browser carries the client id, the redirect
//           URI, the Drive scope, a state value, and a PKCE code challenge.
// RELATION: Exercises GoogleOAuthFlow. No browser, no socket, no network.
// NOTES
//   The fake listener returns a callback with a deliberately wrong state,
//   so the flow rejects it after the URL is captured. The URL is the
//   artifact under test; the rejection proves the flow ran to the
//   validation step.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>Tests the browser-launch seam of the real GoogleOAuthFlow.</summary>
public sealed class GoogleOAuthFlowBrowserTests
{
    private sealed class FakeLoopbackListener : IOAuthLoopbackListener
    {
        public int Port => 53682;

        public Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken ct)
            => Task.FromResult(new OAuthCallbackResult(
                Code: "test-authorization-code",
                State: "deliberately-wrong-state",
                Error: null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AuthorizeInvokesTheBrowserWithTheAuthorizationUrl()
    {
        var stateStore = new OAuthStateStore();
        var capturedUrl = (string?)null;

        Func<string, Task> openBrowser = url =>
        {
            capturedUrl = url;
            return Task.CompletedTask;
        };

        Func<int, string, TimeSpan, IOAuthLoopbackListener> listenerFactory =
            (_, _, _) => new FakeLoopbackListener();

        var flow = new GoogleOAuthFlow(
            stateStore,
            openBrowser,
            listenerFactory,
            _ => Task.FromResult<string?>("test-client-secret"),
            TimeSpan.FromSeconds(30));

        var config = new OAuthClientConfig(
            ProviderId: "gdrive",
            ClientId: "test-client-id",
            AuthorizationEndpoint: "https://example.invalid/auth",
            TokenEndpoint: "https://example.invalid/token",
            Scopes: new[] { OAuthScopes.DriveFile },
            RedirectUri: "http://127.0.0.1:53682/callback/");

        // The flow rejects the mismatched state. The URL was captured first.
        var act = async () => await flow.AuthorizeAsync(config, CancellationToken.None);
        await act.Should().ThrowAsync<OAuthStateException>();

        capturedUrl.Should().NotBeNull();
        capturedUrl!.Should().Contain("test-client-id");
        capturedUrl!.Should().Contain("127.0.0.1");
        capturedUrl!.Should().Contain("drive.file");
        capturedUrl!.Should().Contain("state=");
        capturedUrl!.Should().Contain("code_challenge=");
    }
}
