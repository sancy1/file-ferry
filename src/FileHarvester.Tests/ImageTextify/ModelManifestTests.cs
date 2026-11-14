// filepath: src/FileHarvester.Tests/ImageTextify/ModelManifestTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.D.1
// purpose:  Asserts the Phase 5 model descriptors are complete: each has
//           a non-zero SHA-256, an https source URL, and a positive size.
//           The placeholder PaddleOcrV1 is exempt.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : ImageTextify.Platform.Models.ModelManifest
//   Used by    : dotnet test --filter ModelManifestTests
//   See also   : ImageTextify.Platform.Models.ModelManifest

using System.Linq;
using FluentAssertions;
using ImageTextify.Platform.Models;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class ModelManifestTests
{
    [Fact]
    public void SpeechModelsHaveVerifiedDescriptors()
    {
        var speech = new[] { ModelManifest.WhisperSmallGgml, ModelManifest.SileroVadGgml };

        foreach (var d in speech)
        {
            d.Sha256Hex.Should().NotBe(new string('0', 64), "the model must be hash-verified");
            d.Sha256Hex.Should().HaveLength(64);
            d.SizeBytes.Should().BeGreaterThan(0);
            d.SourceUrl.Should().StartWith("https://");
            d.LicenseName.Should().NotBeNullOrEmpty();
            d.LicenseUrl.Should().StartWith("https://");
            d.Purpose.Should().NotBeNullOrEmpty();
            d.Id.Should().MatchRegex("^[a-z0-9][a-z0-9-]*$");
        }
    }
}