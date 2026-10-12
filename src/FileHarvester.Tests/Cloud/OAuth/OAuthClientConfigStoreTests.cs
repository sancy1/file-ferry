// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/OAuthClientConfigStoreTests.cs
// PURPOSE:  Unit tests for OAuthClientConfigStore. Uses a per-test temp
//           directory. Covers missing, empty, and malformed files; matching
//           and non-matching ids; case-insensitive lookup; the full-field
//           round trip; upsert new and replace; deterministic duplicate
//           handling; delete existing and missing; atomic-write failure
//           preserving the original; cancellation; resilience routing; and
//           the reflective no-client-secret-field check.
// RELATION: Exercises FileHarvester.Platform.Cloud.OAuth.OAuthClientConfigStore.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>
/// Tests for <see cref="OAuthClientConfigStore"/>.
/// </summary>
public sealed class OAuthClientConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _filePath;

    public OAuthClientConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "file-ferry-oauth-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "oauth-clients.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private OAuthClientConfigStore Store()
        => new(_dir);

    private static OAuthClientConfig Config(string providerId = "google-drive")
        => new(
            ProviderId: providerId,
            ClientId: "client-id-value",
            AuthorizationEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint: "https://oauth2.googleapis.com/token",
            Scopes: new List<string> { "scope-one", "scope-two" },
            RedirectUri: "http://127.0.0.1:53682/callback/");

    // ---- Read path -----------------------------------------------------

    [Fact]
    public async Task ReadAsyncReturnsNullWhenFileMissing()
    {
        var store = Store();
        var result = await store.ReadAsync("google-drive", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsyncReturnsNullWhenFileEmpty()
    {
        File.WriteAllText(_filePath, string.Empty);
        var store = Store();
        var result = await store.ReadAsync("google-drive", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsyncReturnsNullWhenJsonMalformed()
    {
        File.WriteAllText(_filePath, "{ this is not valid json");
        var store = Store();
        var result = await store.ReadAsync("google-drive", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsyncReturnsNullForNonMatchingProviderId()
    {
        var store = Store();
        await store.UpsertAsync(Config("google-drive"), CancellationToken.None);

        var result = await store.ReadAsync("some-other-provider", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsyncReturnsMatchingConfigWithEveryField()
    {
        var store = Store();
        await store.UpsertAsync(Config("google-drive"), CancellationToken.None);

        var result = await store.ReadAsync("google-drive", CancellationToken.None);

        result.Should().NotBeNull();
        result!.ProviderId.Should().Be("google-drive");
        result.ClientId.Should().Be("client-id-value");
        result.AuthorizationEndpoint.Should().Be("https://accounts.google.com/o/oauth2/v2/auth");
        result.TokenEndpoint.Should().Be("https://oauth2.googleapis.com/token");
        result.Scopes.Should().Equal("scope-one", "scope-two");
        result.RedirectUri.Should().Be("http://127.0.0.1:53682/callback/");
    }

    [Fact]
    public async Task ReadAsyncMatchesProviderIdCaseInsensitively()
    {
        var store = Store();
        await store.UpsertAsync(Config("Google-Drive"), CancellationToken.None);

        var result = await store.ReadAsync("google-drive", CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ---- Upsert and validation ----------------------------------------

    [Fact]
    public async Task UpsertAddsNewRegistration()
    {
        var store = Store();
        await store.UpsertAsync(Config("provider-a"), CancellationToken.None);

        var result = await store.ReadAsync("provider-a", CancellationToken.None);
        result.Should().NotBeNull();
        result!.ClientId.Should().Be("client-id-value");
    }

    [Fact]
    public async Task UpsertReplacesExistingRegistrationForSameProvider()
    {
        var store = Store();
        await store.UpsertAsync(Config("google-drive"), CancellationToken.None);

        var replacement = new OAuthClientConfig(
            ProviderId: "google-drive",
            ClientId: "second-client-id",
            AuthorizationEndpoint: "https://example.invalid/auth",
            TokenEndpoint: "https://example.invalid/token",
            Scopes: new List<string> { "scope-x" },
            RedirectUri: "http://127.0.0.1:12345/callback/");
        await store.UpsertAsync(replacement, CancellationToken.None);

        var result = await store.ReadAsync("google-drive", CancellationToken.None);
        result!.ClientId.Should().Be("second-client-id");

        var json = File.ReadAllText(_filePath);
        var occurrences = 0;
        var idx = 0;
        while ((idx = json.IndexOf("google-drive", idx, StringComparison.Ordinal)) >= 0)
        {
            occurrences++;
            idx += "google-drive".Length;
        }
        occurrences.Should().Be(1);
    }

    [Fact]
    public async Task UpsertRejectsMissingProviderId()
    {
        var store = Store();
        var bad = Config("google-drive") with { ProviderId = string.Empty };
        var act = async () => await store.UpsertAsync(bad, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertRejectsMissingClientId()
    {
        var store = Store();
        var bad = Config("google-drive") with { ClientId = string.Empty };
        var act = async () => await store.UpsertAsync(bad, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertRejectsInvalidAuthorizationEndpoint()
    {
        var store = Store();
        var bad = Config("google-drive") with { AuthorizationEndpoint = "not-a-uri" };
        var act = async () => await store.UpsertAsync(bad, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertRejectsInvalidTokenEndpoint()
    {
        var store = Store();
        var bad = Config("google-drive") with { TokenEndpoint = "not-a-uri" };
        var act = async () => await store.UpsertAsync(bad, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertRejectsInvalidRedirectUri()
    {
        var store = Store();
        var bad = Config("google-drive") with { RedirectUri = "not-a-uri" };
        var act = async () => await store.UpsertAsync(bad, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpsertRejectsEmptyScopes()
    {
        var store = Store();
        var bad = Config("google-drive") with { Scopes = new List<string>() };
        var act = async () => await store.UpsertAsync(bad, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---- Delete --------------------------------------------------------

    [Fact]
    public async Task DeleteRemovesExistingRegistration()
    {
        var store = Store();
        await store.UpsertAsync(Config("google-drive"), CancellationToken.None);

        await store.DeleteAsync("google-drive", CancellationToken.None);

        var result = await store.ReadAsync("google-drive", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteIsNoOpForMissingRegistration()
    {
        var store = Store();
        await store.UpsertAsync(Config("google-drive"), CancellationToken.None);
        var before = File.ReadAllText(_filePath);

        await store.DeleteAsync("some-other-provider", CancellationToken.None);

        File.ReadAllText(_filePath).Should().Be(before);
    }

    [Fact]
    public async Task DeleteRejectsMissingProviderId()
    {
        var store = Store();
        var act = async () => await store.DeleteAsync(string.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---- Atomicity, cancellation, routing ------------------------------

    [Fact]
    public async Task UpsertLeavesExistingFileIntactWhenReplacementFails()
    {
        var store = Store();
        await store.UpsertAsync(Config("google-drive"), CancellationToken.None);
        var original = File.ReadAllText(_filePath);

        // Hold the destination open with no sharing so the move cannot
        // replace it. The write fails; the original file must be intact.
        using (var _ = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var replacement = Config("another-provider");
            var act = async () => await store.UpsertAsync(replacement, CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        File.ReadAllText(_filePath).Should().Be(original);
    }

    [Fact]
    public async Task ReadAsyncPropagatesCancellation()
    {
        var store = Store();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await store.ReadAsync("google-drive", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UpsertAsyncPropagatesCancellation()
    {
        var store = Store();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await store.UpsertAsync(Config("google-drive"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResilienceRoutesReadsThroughFileRead()
    {
        var executor = Substitute.For<Portfolio.Resilience.Abstractions.IResilienceExecutor>();
        var store = new OAuthClientConfigStore(_dir, executor);

        await store.ReadAsync("google-drive", CancellationToken.None);

        await executor.Received().ExecuteAsync(
            Arg.Is<string>(n => n == FileHarvester.Core.Composition.PolicyNames.FileRead),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OAuthClientConfigHasNoClientSecretProperty()
    {
        var properties = typeof(OAuthClientConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var p in properties)
        {
            p.Name.Should().NotContain("Secret");
        }
    }
}
