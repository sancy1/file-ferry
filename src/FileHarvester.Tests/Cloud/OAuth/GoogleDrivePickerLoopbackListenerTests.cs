using System.Net;
using System.Net.Sockets;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;

namespace FileHarvester.Tests.Cloud.OAuth;

public sealed class GoogleDrivePickerLoopbackListenerTests
{
    [Fact]
    public async Task CallbackAcceptsEquivalentTrailingSlashAndReturnsSelectionPage()
    {
        var port = GetAvailablePort();
        await using var listener = new GoogleDrivePickerLoopbackListener(
            port,
            "/callback/",
            TimeSpan.FromSeconds(5));
        var callbackTask = listener.WaitAsync(CancellationToken.None);

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            $"http://127.0.0.1:{port}/callback?state=state&code=code&scope="
            + Uri.EscapeDataString(OAuthScopes.DriveFile)
            + "&picked_file_ids=file-one");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Selection received");
        var callback = await callbackTask;
        callback.State.Should().Be("state");
        callback.FileIds.Should().ContainSingle().Which.Should().Be("file-one");
    }

    private static int GetAvailablePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    }
}
