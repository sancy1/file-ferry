// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Discovery/RemoteProviderSearchValidatorTests.cs
// PURPOSE:  Tests the pure remote-search validator: disabled passes;
//           enabled with no backend fails; enabled with a backend that
//           requires an endpoint and no endpoint fails; and the two
//           passing endpoint combinations. No DI, no network.
// RELATION: Exercises FileHarvester.Platform.Cloud.Discovery.RemoteProviderSearchValidator.
// ============================================================================

using System;
using FileHarvester.Platform.Cloud.Discovery;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Discovery;

/// <summary>
/// Tests for the remote-search options validator.
/// </summary>
public sealed class RemoteProviderSearchValidatorTests
{
    private static RemoteProviderSearchOptions Disabled =>
        RemoteProviderSearchOptions.Default;

    private static RemoteProviderSearchOptions Enabled(string? endpoint = null) =>
        RemoteProviderSearchOptions.Default with
        {
            Enabled = true,
            Endpoint = endpoint is null ? null : new Uri(endpoint),
        };

    [Fact]
    public void DisabledPassesEvenWithNoBackend()
    {
        var errors = RemoteProviderSearchValidator.Validate(
            Disabled, backendRegistered: false, endpointRequired: false);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void EnabledWithNoBackendFails()
    {
        var errors = RemoteProviderSearchValidator.Validate(
            Enabled(), backendRegistered: false, endpointRequired: false);

        errors.Should().HaveCount(1);
        errors[0].Should().Contain("no backend");
    }

    [Fact]
    public void EnabledWithBackendRequiringEndpointAndNoEndpointFails()
    {
        var errors = RemoteProviderSearchValidator.Validate(
            Enabled(), backendRegistered: true, endpointRequired: true);

        errors.Should().HaveCount(1);
        errors[0].Should().Contain("no endpoint");
    }

    [Fact]
    public void EnabledWithBackendRequiringEndpointAndEndpointPasses()
    {
        var errors = RemoteProviderSearchValidator.Validate(
            Enabled("https://example.invalid"),
            backendRegistered: true,
            endpointRequired: true);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void EnabledWithBackendNotRequiringEndpointAndNoEndpointPasses()
    {
        var errors = RemoteProviderSearchValidator.Validate(
            Enabled(), backendRegistered: true, endpointRequired: false);

        errors.Should().BeEmpty();
    }
}
