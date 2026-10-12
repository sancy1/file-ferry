// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/OAuthLoopbackListenerTests.cs
// PURPOSE:  Unit tests for OAuthLoopbackListener against a real loopback
//           socket. The listener binds a real port and receives a real
//           HTTP request; this is the behavior under test, so a mock
//           would prove nothing. Each test picks a free port, makes a
//           real HttpClient request, and asserts the parsed result.
// RELATION: Exercises FileHarvester.Platform.Cloud.OAuth.OAuthLoopbackListener.
// ============================================================================

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>
/// Tests for <see cref="OAuthLoopbackListener"/> using a real loopback
/// socket and a real HTTP request.
/// </summary>
public sealed class OAuthLoopbackListenerTests
{
    private const string CallbackPath = "/callback/";

    /// <summary>
    /// Returns a port the OS reports as free. Binds a TcpListener on
    /// 127.0.0.1:0, reads the assigned port, closes the TcpListener, and
    /// returns the port. There is a small race window between close and
    /// the HttpListener bind; acceptable for tests.
    /// </summary>
    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static string CallbackUrl(int port, string query)
        => "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + CallbackPath + "?" + query;

    [Fact]
    public async Task WaitForCallbackAsyncReturnsCodeAndState()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));

        var waitTask = listener.WaitForCallbackAsync(CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync(CallbackUrl(port, "code=the-code&state=the-state"));

        var result = await waitTask;
        result.Code.Should().Be("the-code");
        result.State.Should().Be("the-state");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task WaitForCallbackAsyncReturnsErrorWhenProviderDenies()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));

        var waitTask = listener.WaitForCallbackAsync(CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync(CallbackUrl(port, "error=access_denied&state=the-state"));

        var result = await waitTask;
        result.Error.Should().Be("access_denied");
        result.Code.Should().BeNull();
    }

    [Fact]
    public async Task WaitForCallbackAsyncIgnoresExtraQueryParameters()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));

        var waitTask = listener.WaitForCallbackAsync(CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync(CallbackUrl(port, "code=c&state=s&token=should-not-surface&extra=x"));

        var result = await waitTask;
        result.Code.Should().Be("c");
        result.State.Should().Be("s");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task WaitForCallbackAsyncRejectsUnknownPath()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));

        var waitTask = listener.WaitForCallbackAsync(CancellationToken.None);

        using var http = new HttpClient();
        var wrong = await http.GetAsync("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/wrong/?code=x");
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await http.GetAsync(CallbackUrl(port, "code=right&state=ok"));

        var result = await waitTask;
        result.Code.Should().Be("right");
    }

    [Fact]
    public async Task WaitForCallbackAsyncThrowsOnTimeout()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(1));

        var act = async () => await listener.WaitForCallbackAsync(CancellationToken.None);
        await act.Should().ThrowAsync<OAuthTimeoutException>();
    }

    [Fact]
    public async Task WaitForCallbackAsyncRethrowsCallerCancellation()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var act = async () => await listener.WaitForCallbackAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitForCallbackAsyncPreventsSecondCallback()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));

        var waitTask = listener.WaitForCallbackAsync(CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync(CallbackUrl(port, "code=first&state=s"));
        var result = await waitTask;
        result.Code.Should().Be("first");

        var second = async () => await http.GetAsync(CallbackUrl(port, "code=second&state=s"));
        await second.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task PortPropertyReflectsBoundPort()
    {
        var port = GetFreePort();
        await using var listener = new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));

        var waitTask = listener.WaitForCallbackAsync(CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync(CallbackUrl(port, "code=c&state=s"));
        await waitTask;

        listener.Port.Should().Be(port);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(70000)]
    public void ConstructorRejectsInvalidPort(int port)
    {
        var act = () => new OAuthLoopbackListener(port, CallbackPath, TimeSpan.FromSeconds(10));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("no-leading-slash")]
    [InlineData("")]
    public void ConstructorRejectsInvalidCallbackPath(string path)
    {
        var act = () => new OAuthLoopbackListener(5000, path, TimeSpan.FromSeconds(10));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConstructorRejectsNonPositiveTimeout()
    {
        var act = () => new OAuthLoopbackListener(5000, CallbackPath, TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
