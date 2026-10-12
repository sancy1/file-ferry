// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/CloudPolicyTests.cs
// PURPOSE:  Verifies the four cloud resilience policies are registered
//           in AddFileHarvester with the exact configuration from the
//           confirmed plan, and that the three existing policies are
//           unchanged.
// RELATION: Consumes TestHarness and IResiliencePolicyRegistry from
//           Portfolio.Resilience.Abstractions.
// ============================================================================

using FileHarvester.Core.Composition;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Configuration;
using Xunit;

namespace FileHarvester.Tests.Cloud;

/// <summary>
/// Tests for the cloud resilience policies registered by
/// <see cref="ServiceCollectionExtensions.AddFileHarvester"/>.
/// </summary>
public sealed class CloudPolicyTests : IClassFixture<TestHarness>
{
    private readonly TestHarness _harness;

    public CloudPolicyTests(TestHarness harness)
    {
        _harness = harness;
    }

    private IResiliencePolicyRegistry Registry =>
        _harness.Provider.GetRequiredService<IResiliencePolicyRegistry>();

    // ------------------------------------------------------------------
    // Policy names
    // ------------------------------------------------------------------

    [Fact]
    public void CloudReadNameIsCorrect()
    {
        PolicyNames.CloudRead.Should().Be("cloud-read");
    }

    [Fact]
    public void CloudWriteNameIsCorrect()
    {
        PolicyNames.CloudWrite.Should().Be("cloud-write");
    }

    [Fact]
    public void CloudSearchNameIsCorrect()
    {
        PolicyNames.CloudSearch.Should().Be("cloud-search");
    }

    [Fact]
    public void CloudAuthNameIsCorrect()
    {
        PolicyNames.CloudAuth.Should().Be("cloud-auth");
    }

    // ------------------------------------------------------------------
    // All four policies registered
    // ------------------------------------------------------------------

    [Fact]
    public void AllFourCloudPoliciesAreRegistered()
    {
        var known = Registry.KnownPolicies;
        known.Should().Contain(PolicyNames.CloudRead);
        known.Should().Contain(PolicyNames.CloudWrite);
        known.Should().Contain(PolicyNames.CloudSearch);
        known.Should().Contain(PolicyNames.CloudAuth);
    }

    // ------------------------------------------------------------------
    // Configuration checks
    // ------------------------------------------------------------------

    [Fact]
    public void CloudReadPolicyHasExpectedConfiguration()
    {
        var p = Registry.Resolve(PolicyNames.CloudRead);

        p.Retry.MaxAttempts.Should().Be(5);
        p.Circuit.FailureThreshold.Should().Be(5);
        p.Circuit.OpenDurationSeconds.Should().Be(30);
        p.Timeout.TimeoutMs.Should().Be(30_000);
        p.RateLimiter.Enabled.Should().BeTrue();
        p.RateLimiter.Strategy.Should().Be(RateLimitStrategy.SlidingWindow);
        p.RateLimiter.PermitLimit.Should().Be(300);
        p.RateLimiter.WindowSeconds.Should().Be(60);
    }

    [Fact]
    public void CloudWritePolicyHasExpectedConfiguration()
    {
        var p = Registry.Resolve(PolicyNames.CloudWrite);

        p.Retry.MaxAttempts.Should().Be(3);
        p.Circuit.FailureThreshold.Should().Be(3);
        p.Circuit.OpenDurationSeconds.Should().Be(60);
        p.Timeout.TimeoutMs.Should().Be(300_000);
        p.RateLimiter.Enabled.Should().BeTrue();
        p.RateLimiter.PermitLimit.Should().Be(60);
        p.RateLimiter.WindowSeconds.Should().Be(60);
    }

    [Fact]
    public void CloudAuthPolicyHasFiveMinuteOpenDuration()
    {
        var p = Registry.Resolve(PolicyNames.CloudAuth);

        p.Circuit.OpenDurationSeconds.Should().Be(300);
        p.RateLimiter.PermitLimit.Should().Be(30);
        p.Timeout.TimeoutMs.Should().Be(15_000);
    }

    [Fact]
    public void CloudSearchPolicyHasExpectedRateLimit()
    {
        var p = Registry.Resolve(PolicyNames.CloudSearch);

        p.RateLimiter.Enabled.Should().BeTrue();
        p.RateLimiter.PermitLimit.Should().Be(120);
        p.Timeout.TimeoutMs.Should().Be(60_000);
    }

    // ------------------------------------------------------------------
    // Existing three policies unchanged
    // ------------------------------------------------------------------

    [Fact]
    public void ExistingThreePoliciesAreStillRegisteredAndUnchanged()
    {
        var fileRead = Registry.Resolve(PolicyNames.FileRead);
        fileRead.Retry.MaxAttempts.Should().Be(4);
        fileRead.Circuit.FailureThreshold.Should().Be(10);
        fileRead.Timeout.TimeoutMs.Should().Be(3_000);

        var pathSearch = Registry.Resolve(PolicyNames.PathSearch);
        pathSearch.Retry.MaxAttempts.Should().Be(2);
        pathSearch.Circuit.FailureThreshold.Should().Be(3);
        pathSearch.Timeout.TimeoutMs.Should().Be(180_000);

        var bundleWrite = Registry.Resolve(PolicyNames.BundleWrite);
        bundleWrite.Retry.MaxAttempts.Should().Be(3);
        bundleWrite.Circuit.FailureThreshold.Should().Be(5);
        bundleWrite.Timeout.TimeoutMs.Should().Be(15_000);
    }
}
