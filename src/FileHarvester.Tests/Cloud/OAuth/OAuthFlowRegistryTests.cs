// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/OAuthFlowRegistryTests.cs
// PURPOSE:  Tests for the per-provider OAuth flow registry. Proves that
//           gdrive selects the Google flow, onedrive selects the Microsoft
// RELATION: Exercises OAuthFlowRegistry.
// ============================================================================

using System;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>Tests for the OAuth flow registry.</summary>
public sealed class OAuthFlowRegistryTests
{
    [Fact]
    public void ProviderIdSelectsTheFlow()
    {
        var google = Substitute.For<IOAuthFlow>();
        var microsoft = Substitute.For<IOAuthFlow>();
        var dropbox = Substitute.For<IOAuthFlow>();
        var registry = new OAuthFlowRegistry(google, microsoft, dropbox);

        registry.Resolve("gdrive").Should().BeSameAs(google);
        registry.Resolve("dropbox").Should().BeSameAs(dropbox);
        registry.Resolve("onedrive").Should().BeSameAs(microsoft);
        registry.Resolve("GDRIVE").Should().BeSameAs(google);
        registry.Resolve("dropbox").Should().BeSameAs(dropbox);
    }

    [Fact]
    public void UnknownProviderIdThrows()
    {
        var registry = new OAuthFlowRegistry(
            Substitute.For<IOAuthFlow>(),
            Substitute.For<IOAuthFlow>(),
            Substitute.For<IOAuthFlow>());

        var act = () => registry.Resolve("unknown-provider");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*unknown-provider*");
    }
}
