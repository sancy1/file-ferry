// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ModelStoreContractTests.cs
// PURPOSE:  Contract-shape tests for the model store types. Behavior tests
//           for the implementation arrive in Phase 2.2 Block C.
// RELATION: Phase 2.2 Block B exit-criteria test.
// ============================================================================

using System.Reflection;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Models;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Contract-shape tests for the model store types.
/// </summary>
public sealed class ModelStoreContractTests
{
    private static readonly string[] ExpectedDescriptorProperties = new[]
    {
        "Id",
        "DisplayName",
        "SourceUrl",
        "SizeBytes",
        "Sha256Hex",
        "LicenseName",
        "LicenseUrl",
        "Purpose",
    };

    private static readonly string[] ExpectedStatusMembers = new[]
    {
        "NotDownloaded",
        "Downloaded",
        "Corrupted",
        "Unavailable",
    };

    [Fact]
    public void ImageTextifyModelDescriptorHasExpectedProperties()
    {
        var props = typeof(ImageTextifyModelDescriptor)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        props.Should().BeEquivalentTo(ExpectedDescriptorProperties);
    }

    [Fact]
    public void ImageTextifyModelStatusHasApprovedMembers()
    {
        var names = Enum.GetNames<ImageTextifyModelStatus>();
        names.Should().BeEquivalentTo(ExpectedStatusMembers);
    }

    [Fact]
    public void ImageTextifyModelStatusDoesNotContainDownloading()
    {
        // Per the Phase 2.2 amendment: no concurrency-safe download session
        // exists yet, so the state does not exist in the type. It will be
        // added in a later phase when a real session owner is introduced.
        Enum.GetNames<ImageTextifyModelStatus>().Should().NotContain("Downloading");
    }

    [Fact]
    public void ModelDownloadProgressCarriesNullableTotalAndPercent()
    {
        var props = typeof(ModelDownloadProgress)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.PropertyType);

        props.Should().ContainKey("BytesReceived");
        props["BytesReceived"].Should().Be<long>();

        props.Should().ContainKey("TotalBytes");
        props["TotalBytes"].Should().Be<long?>();

        props.Should().ContainKey("PercentComplete");
        props["PercentComplete"].Should().Be<double?>();
    }

    [Fact]
    public void IModelStoreExposesApprovedMethodShapes()
    {
        var t = typeof(IModelStore);

        var status = t.GetMethod("GetStatusAsync");
        status.Should().NotBeNull();
        status!.ReturnType.Should().Be<Task<ImageTextifyModelStatus>>();
        status.GetParameters().Should().HaveCount(2);

        var ensure = t.GetMethod("EnsureAvailableAsync");
        ensure.Should().NotBeNull();
        ensure!.ReturnType.Should().Be<Task<string>>();
        ensure.GetParameters().Should().HaveCount(3);

        var path = t.GetMethod("GetLocalPath");
        path.Should().NotBeNull();
        path!.ReturnType.Should().Be<string>();
        path.GetParameters().Should().HaveCount(1);
    }

    [Fact]
    public void ModelDownloadExceptionHasThreeConstructors()
    {
        var ctors = typeof(ModelDownloadException).GetConstructors();
        ctors.Should().HaveCount(3);
    }
}