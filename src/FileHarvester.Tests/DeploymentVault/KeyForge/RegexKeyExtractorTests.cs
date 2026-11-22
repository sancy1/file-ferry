// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/KeyForge/RegexKeyExtractorTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.KeyForge
// since:    Phase A (KeyForge)
// purpose:  Unit tests for RegexKeyExtractor: per-family detection, generic
//           fallback, exclusions (URL/email/date/path/plain word), and
//           non-overlapping spans.
// ============================================================================

using DeploymentVault.Core.KeyForge.Domain;
using DeploymentVault.Platform.KeyForge.Extraction;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.KeyForge;

/// <summary>
/// Unit tests for <see cref="RegexKeyExtractor"/>.
/// </summary>
public sealed class RegexKeyExtractorTests
{
    private static RegexKeyExtractor NewExtractor() => new();

    [Fact]
    public void DetectsPrefixedToken()
    {
        var text = "API_KEY=gsk_" + new string('a', 48) + "\n";
        var spans = NewExtractor().Extract(text);
        var span = Assert.Single(spans);
        Assert.Equal(KeyFamily.PrefixedToken, span.Family);
        Assert.StartsWith("gsk_", span.Original, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectsJwt()
    {
        var jwt = "eyJ" + new string('a', 16) + "." + new string('b', 16) + "." + new string('c', 16);
        var spans = NewExtractor().Extract($"TOKEN={jwt}\n");
        var span = Assert.Single(spans);
        Assert.Equal(KeyFamily.Jwt, span.Family);
    }

    [Fact]
    public void DetectsUuid()
    {
        var text = "id=123e4567-e89b-12d3-a456-426614174000\n";
        var spans = NewExtractor().Extract(text);
        var span = Assert.Single(spans);
        Assert.Equal(KeyFamily.Uuid, span.Family);
    }

    [Fact]
    public void DetectsPemBlock()
    {
        var text = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA7\n-----END RSA PRIVATE KEY-----\n";
        var spans = NewExtractor().Extract(text);
        var span = Assert.Single(spans);
        Assert.Equal(KeyFamily.PemBlock, span.Family);
    }

    [Fact]
    public void DetectsBase64Blob()
    {
        var blob = new string('A', 40) + "==";
        var spans = NewExtractor().Extract($"key={blob}\n");
        Assert.Contains(spans, s => s.Family == KeyFamily.Base64Blob || s.Family == KeyFamily.Generic);
    }

    [Fact]
    public void ExcludesUrl()
    {
        var text = "url=https://example.com/path\n";
        var spans = NewExtractor().Extract(text);
        Assert.DoesNotContain(spans, s => s.Original.Contains("example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludesEmail()
    {
        var text = "contact=user@example.com\n";
        var spans = NewExtractor().Extract(text);
        Assert.DoesNotContain(spans, s => s.Original.Contains('@'));
    }

    [Fact]
    public void ExcludesPlainWord()
    {
        var text = "environment=production\n";
        var spans = NewExtractor().Extract(text);
        Assert.Empty(spans);
    }

    [Fact]
    public void DetectsMultipleNonOverlapping()
    {
        var text = "A=gsk_" + new string('a', 48) + "\nB=123e4567-e89b-12d3-a456-426614174000\n";
        var spans = NewExtractor().Extract(text);
        Assert.Equal(2, spans.Count);
        Assert.True(spans[0].Start < spans[1].Start);
    }

    [Fact]
    public void EmptyTextReturnsEmpty()
    {
        Assert.Empty(NewExtractor().Extract(string.Empty));
    }
}
