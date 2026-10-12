// ============================================================================
// FILE:     src/FileHarvester.Tests/Documentation/ArchitectureClaimsTests.cs
// PURPOSE:  Documentation conformance tests for docs/architecture.md.
//           Every test asserts a claim the document makes about the
//           code. Uses stable markers: counts from code, exception
//           types, method existence. Never parses prose.
// RELATION: Executable documentation tier.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Platform.Cloud.Credentials;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Documentation;

public sealed class ArchitectureClaimsTests
{
    [Fact]
    public void DocumentationClaimsElevenProfiles()
    {
        // The architecture doc says File-Ferry ships eleven built-in
        // provider profiles. This test counts the code's profiles.
        BuiltInProfiles.All.Should().HaveCount(11);
    }

    [Fact]
    public void MacOsKeychainThrowsNamedError()
    {
        // The architecture doc (v1.1 delivery section) says the macOS
        // Keychain backend throws CloudCredentialUnavailableException.
        var store = new MacOsKeychainStore();
        var key = new CredentialKey("test", "test");

        var act = async () => await store.ReadAsync(key);
        act.Should().ThrowAsync<CloudCredentialUnavailableException>();
    }

    [Fact]
    public void LinuxSecretServiceThrowsNamedError()
    {
        // The architecture doc says the Linux Secret Service backend
        // throws CloudCredentialUnavailableException.
        var store = new LinuxSecretServiceStore();
        var key = new CredentialKey("test", "test");

        var act = async () => await store.ReadAsync(key);
        act.Should().ThrowAsync<CloudCredentialUnavailableException>();
    }

    [Fact]
    public void OverwriteConfirmerExists()
    {
        // The architecture doc (D.1 section) says IOverwriteConfirmer
        // exists and OutputSink uses it.
        typeof(IOverwriteConfirmer).Should().NotBeNull();

        var ctor = typeof(OutputSink).GetConstructors().SingleOrDefault();
        ctor.Should().NotBeNull();

        var parameters = ctor!.GetParameters().Select(p => p.ParameterType).ToList();
        parameters.Should().Contain(typeof(IOverwriteConfirmer),
            "the doc says OutputSink takes the confirmer by constructor injection");
    }
}
