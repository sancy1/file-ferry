// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/CloudConnectionServiceResolutionTests.cs
// PURPOSE:  Proves that the OAuth service graph resolves end to end in the
//           production registration order. This is the verification that
//           ICloudConnectionService can be resolved with ICredentialStore
//           registered first, resolving the lazy token-store dependency.
// RELATION: Uses the real Platform extensions in the same order as
//           App.axaml.cs BuildServices.
// ============================================================================

using System;
using System.IO;
using FileHarvester.Core.Composition;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.Dispatch;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>
/// Resolution tests for the OAuth service graph.
/// </summary>
public sealed class CloudConnectionServiceResolutionTests
{
    [Fact]
    public void CloudConnectionServiceResolvesInProductionOrder()
    {
        var services = new ServiceCollection();

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "file-ferry-di-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            // The production order, mirroring App.axaml.cs BuildServices.
            services.AddFileHarvester(new ResilienceConfig(
                LogDirectory: Path.Combine(tempRoot, "logs"),
                EnableConsoleSink: false,
                EnableFileSink: false));

            services.AddFileHarvesterCloudCredentials();
            services.AddFileHarvesterCloudDispatch();
            services.AddFileHarvesterCloudOAuth();

            using var provider = services.BuildServiceProvider();

            var service = provider.GetRequiredService<ICloudConnectionService>();

            service.Should().NotBeNull();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void CredentialStoreResolvesBeforeTheConnectionServiceIsBuilt()
    {
        var services = new ServiceCollection();

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "file-ferry-di-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            services.AddFileHarvester(new ResilienceConfig(
                LogDirectory: Path.Combine(tempRoot, "logs"),
                EnableConsoleSink: false,
                EnableFileSink: false));

            services.AddFileHarvesterCloudCredentials();
            services.AddFileHarvesterCloudDispatch();
            services.AddFileHarvesterCloudOAuth();

            using var provider = services.BuildServiceProvider();

            // Both resolve without a missing-dependency failure.
            provider.GetRequiredService<ICredentialStore>().Should().NotBeNull();
            provider.GetRequiredService<ICloudConnectionService>().Should().NotBeNull();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
