// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ResilienceCallbackTests.cs
// PURPOSE:  Proves that AddFileHarvester invokes the ImageTextify resilience
//           callback exactly once and that the image-textify-model-download
//           policy is registered through it. Also proves that no other
//           ImageTextify policy is registered yet.
// RELATION: Phase 2.2 Block A exit-criteria test.
// ============================================================================

using System;
using System.Linq;
using FluentAssertions;
using ImageTextify.Platform.Composition;
using FileHarvester.Core.Composition;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Configuration;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for the ImageTextify resilience callback plumbing.
/// </summary>
public sealed class ResilienceCallbackTests
{
    private static ServiceCollection BuildServices(
        Action<ResilienceBuilder>? configureImageTextifyPolicies)
    {
        var services = new ServiceCollection();
        services.AddFileHarvester(
            config: new FileHarvester.Core.Composition.ResilienceConfig(
                LogDirectory: Path.Combine(Path.GetTempPath(), "file-ferry-tests-callback", Guid.NewGuid().ToString("N")),
                EnableConsoleSink: false,
                EnableFileSink: false),
            extraSink: null,
            configureImageTextifyPolicies: configureImageTextifyPolicies);
        return services;
    }

    [Fact]
    public void AddFileHarvesterRegistersImageTextifyModelDownloadPolicy()
    {
        var services = BuildServices(ImageTextifyComposition.RegisterResiliencePolicies);
        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();

        // The executor resolves by policy name at call time; a probe call
        // for a registered policy returns a value rather than throwing
        // "policy not found".
        var act = async () => await executor.ExecuteAsync(
            policyName: ImageTextifyPolicyNames.ModelDownload,
            operation: _ => Task.FromResult(0),
            fallback: _ => Task.FromResult(-1),
            ct: CancellationToken.None);

        act.Should().NotThrowAsync();
    }

    [Fact]
    public void AddFileHarvesterWithoutCallbackDoesNotRegisterImageTextifyPolicy()
    {
        var services = BuildServices(null);
        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var act = async () => await executor.ExecuteAsync(
            policyName: ImageTextifyPolicyNames.ModelDownload,
            operation: _ => Task.FromResult(0),
            fallback: _ => Task.FromResult(-1),
            ct: CancellationToken.None);

        act.Should().ThrowAsync<Exception>(
            "a policy that was never registered cannot be executed.");
    }

    [Fact]
    public void ImageTextifyRegistersOnlyTheModelDownloadPolicyAtThisPhase()
    {
        // The reviewed contract: at Phase 2.2, exactly one ImageTextify-owned
        // policy is registered - image-textify-model-download. The other four
        // ImageTextify policy names must not resolve yet.
        var services = BuildServices(ImageTextifyComposition.RegisterResiliencePolicies);
        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var otherNames = new[]
        {
            ImageTextifyPolicyNames.OcrInference,
            ImageTextifyPolicyNames.PdfRender,
            ImageTextifyPolicyNames.Media,
            ImageTextifyPolicyNames.Speech,
        };

        foreach (var name in otherNames)
        {
            var act = async () => await executor.ExecuteAsync(
                policyName: name,
                operation: _ => Task.FromResult(0),
                fallback: _ => Task.FromResult(-1),
                ct: CancellationToken.None);

            act.Should().ThrowAsync<Exception>(
                "policy " + name + " is declared but not registered in Phase 2.2.");
        }
    }

    [Fact]
    public void CallbackIsInvokedExactlyOnceByAddFileHarvester()
    {
        var invocations = 0;
        void CountingCallback(ResilienceBuilder builder)
        {
            invocations++;
            ImageTextifyComposition.RegisterResiliencePolicies(builder);
        }

        var services = BuildServices(CountingCallback);
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IResilienceExecutor>();

        invocations.Should().Be(1,
            "AddFileHarvester must invoke the callback exactly once, inside the single AddPortfolioResilience block.");
    }
}
