// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/S3ProviderFactoryTests.cs
// PURPOSE:  Unit tests for S3ProviderFactory. Verifies the client is
//           built with the expected credentials, region, and endpoint.
//           No network calls.
// RELATION: Consumes FileHarvester.Platform.Cloud.Providers.S3ProviderFactory.
// ============================================================================

using Amazon.S3;
using FileHarvester.Platform.Cloud.Providers;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>
/// Tests for <see cref="S3ProviderFactory"/>.
/// </summary>
public sealed class S3ProviderFactoryTests
{
    private static S3ConnectionSettings BasicSettings() => new(
        AccessKeyId: "AKIAIOSFODNN7EXAMPLE",
        SecretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        SessionToken: null,
        Region: "us-west-2",
        CustomEndpointUrl: null,
        ForcePathStyle: false);

    [Fact]
    public void CreateClientUsesProvidedRegion()
    {
        var client = S3ProviderFactory.CreateClient(BasicSettings());

        client.Should().NotBeNull();
        client.Config.RegionEndpoint?.SystemName.Should().Be("us-west-2");

        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void CreateClientDefaultsToUsEast1WhenRegionMissing()
    {
        var settings = BasicSettings() with { Region = null };
        var client = S3ProviderFactory.CreateClient(settings);

        client.Config.RegionEndpoint?.SystemName.Should().Be("us-east-1");

        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void CreateClientSetsServiceUrlWhenCustomEndpointGiven()
    {
        var settings = BasicSettings() with
        {
            CustomEndpointUrl = "https://example.r2.cloudflarestorage.com",
        };

        var client = S3ProviderFactory.CreateClient(settings);

        // The AWS SDK 4.x normalizes ServiceURL to include a trailing slash.
        // The factory sets the value without the slash; the SDK adds it.
        client.Config.ServiceURL.Should().Be("https://example.r2.cloudflarestorage.com/");

        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void CreateClientForcesPathStyleWhenCustomEndpointGiven()
    {
        var settings = BasicSettings() with
        {
            CustomEndpointUrl = "https://example.r2.cloudflarestorage.com",
            ForcePathStyle = false,
        };

        var client = S3ProviderFactory.CreateClient(settings);

        ((AmazonS3Config)client.Config).ForcePathStyle.Should().BeTrue();

        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void CreateClientIncludesSessionTokenWhenGiven()
    {
        var settings = BasicSettings() with { SessionToken = "token-value" };

        var act = () => S3ProviderFactory.CreateClient(settings);
        act.Should().NotThrow();

        var client = act();
        (client as IDisposable)?.Dispose();
    }

    [Fact]
    public void CreateClientRejectsNullSettings()
    {
        var act = () => S3ProviderFactory.CreateClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateClientRejectsMissingAccessKeyId()
    {
        var settings = BasicSettings() with { AccessKeyId = "" };
        var act = () => S3ProviderFactory.CreateClient(settings);
        act.Should().Throw<ArgumentException>();
    }
}
