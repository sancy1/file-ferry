// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Credentials/CredentialStoreKeyTests.cs
// PURPOSE:  Unit tests for CredentialStoreKey. Verifies the static format
//           is byte-for-byte the legacy format; the OAuth format uses the
//           reserved namespace; the two formats are structurally disjoint;
//           a profile id that happens to be the literal oauth is a valid
//           static id; and the parsing rules match the two formats.
// RELATION: Exercises FileHarvester.Platform.Cloud.Credentials.CredentialStoreKey.
// ============================================================================

using System;
using FileHarvester.Platform.Cloud.Credentials;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Credentials;

/// <summary>
/// Tests for the typed credential store key.
/// </summary>
public sealed class CredentialStoreKeyTests
{
    [Fact]
    public void FromCredentialKeyProducesStaticKeyWithLegacyFormat()
    {
        var legacy = new CredentialKey("aws-s3", "work");
        var typed = CredentialStoreKey.FromCredentialKey(legacy);

        typed.Kind.Should().Be(CredentialKind.Static);
        typed.ProfileId.Should().Be("aws-s3");
        typed.InstanceName.Should().Be("work");
        typed.ToStoreKey().Should().Be(legacy.ToStoreKey());
        typed.ToStoreKey().Should().Be("file-ferry/aws-s3/work");
    }

    [Fact]
    public void StaticFactoryProducesLegacyFormat()
    {
        CredentialStoreKey.Static("aws-s3", "work").ToStoreKey()
            .Should().Be("file-ferry/aws-s3/work");
    }

    [Fact]
    public void OAuthFactoryProducesOAuthNamespace()
    {
        var key = CredentialStoreKey.OAuth("gdrive", "prod");

        key.Kind.Should().Be(CredentialKind.OAuth);
        key.ToStoreKey().Should().Be("file-ferry/oauth/gdrive/prod");
    }

    [Fact]
    public void StaticAndOAuthKeysAreNotEqual()
    {
        var staticKey = CredentialStoreKey.Static("a", "b");
        var oauthKey = CredentialStoreKey.OAuth("a", "b");

        staticKey.Should().NotBe(oauthKey);
        staticKey.ToStoreKey().Should().NotBe(oauthKey.ToStoreKey());
    }

    [Fact]
    public void FromCredentialKeyAndStaticFactoryAreEqual()
    {
        var a = CredentialStoreKey.FromCredentialKey(new CredentialKey("a", "b"));
        var b = CredentialStoreKey.Static("a", "b");

        a.Should().Be(b);
    }

    [Fact]
    public void ParseRoundTripsStatic()
    {
        var parsed = CredentialStoreKey.Parse("file-ferry/aws-s3/work");

        parsed.Kind.Should().Be(CredentialKind.Static);
        parsed.ProfileId.Should().Be("aws-s3");
        parsed.InstanceName.Should().Be("work");
        parsed.ToStoreKey().Should().Be("file-ferry/aws-s3/work");
    }

    [Fact]
    public void ParseRoundTripsOAuth()
    {
        var parsed = CredentialStoreKey.Parse("file-ferry/oauth/gdrive/prod");

        parsed.Kind.Should().Be(CredentialKind.OAuth);
        parsed.ProfileId.Should().Be("gdrive");
        parsed.InstanceName.Should().Be("prod");
        parsed.ToStoreKey().Should().Be("file-ferry/oauth/gdrive/prod");
    }

    [Fact]
    public void ParseRejectsUnknownSegmentCount()
    {
        var act = () => CredentialStoreKey.Parse("file-ferry/a/b/c/d");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParseRejectsFourSegmentsWithoutOAuthNamespace()
    {
        var act = () => CredentialStoreKey.Parse("file-ferry/not-oauth/x/y");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void StaticProfileIdMayBeTheLiteralOauth()
    {
        // "oauth" as a static profile id produces a THREE-segment key,
        // distinct from the FOUR-segment OAuth format. No collision.
        CredentialStoreKey.Static("oauth", "work").ToStoreKey()
            .Should().Be("file-ferry/oauth/work");
    }

    [Fact]
    public void ParseDisambiguatesThreeSegmentAsStatic()
    {
        // The three-segment form is ALWAYS static, even when the second
        // segment is the literal string oauth.
        var parsed = CredentialStoreKey.Parse("file-ferry/oauth/work");

        parsed.Kind.Should().Be(CredentialKind.Static);
        parsed.ProfileId.Should().Be("oauth");
        parsed.InstanceName.Should().Be("work");
    }

    [Fact]
    public void ToStoreKeyRejectsSeparatorInProfileId()
    {
        var key = CredentialStoreKey.Static("aws/s3", "work");
        var act = () => key.ToStoreKey();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToStoreKeyRejectsSeparatorInInstanceName()
    {
        var key = CredentialStoreKey.Static("aws-s3", "work/account");
        var act = () => key.ToStoreKey();
        act.Should().Throw<InvalidOperationException>();
    }
}
