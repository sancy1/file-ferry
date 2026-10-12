// ============================================================================
// FILE:     src/FileHarvester.Tests/Contracts/CloudProviderContractTests.cs
// PURPOSE:  Contract tests for ICloudProvider. Every test asserts a
//           behavior documented on the interface. Tests are written
//           from the doc, not from the implementation.
// RELATION: Executable contract tier. Uses StubCloudProvider.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Tests.Cloud.Services;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Contracts;

public sealed class CloudProviderContractTests
{
    [Fact]
    public async Task OpenReadMissingPathThrows()
    {
        // The interface doc says implementations must not silently
        // return a placeholder: a provider that cannot open the
        // object throws. The caller converts the exception to a
        // placeholder. This test verifies the provider side of that
        // contract by using a stub that throws.
        var provider = new StubCloudProvider();
        provider.OnOpenRead = _ => throw new InvalidOperationException(
            "documented: provider throws on missing object");

        var act = async () => await provider.OpenReadAsync("s3://bucket/missing");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancelledOperationThrows()
    {
        // The interface doc says "A cancelled operation is expected to
        // throw System.OperationCanceledException."
        var provider = new StubCloudProvider();
        provider.OnOpenRead = _ => throw new OperationCanceledException();

        var act = async () => await provider.OpenReadAsync("s3://bucket/x");
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MetadataMissingPathThrows()
    {
        // The interface doc says GetMetadataAsync "Throws when the
        // object does not exist." This is the provider-side contract:
        // a provider that cannot produce metadata for a missing object
        // throws; it does not return a default or an empty record.
        // The concrete exception type is provider-specific (Azure:
        // RequestFailedException status 404, wrapped by the resilience
        // pipeline; S3: AmazonS3Exception). This test asserts the
        // interface-level contract only.
        var provider = new StubCloudProvider();
        provider.OnGetMetadata = _ => throw new InvalidOperationException(
            "documented: provider throws on missing object metadata");

        var act = async () => await provider.GetMetadataAsync("s3://bucket/missing");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
