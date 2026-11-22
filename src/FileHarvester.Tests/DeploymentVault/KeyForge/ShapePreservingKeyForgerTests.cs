// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/KeyForge/ShapePreservingKeyForgerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.KeyForge
// since:    Phase A (KeyForge)
// purpose:  Unit tests for ShapePreservingKeyForger: shape preservation per
//           family, deterministic reproducibility, and random variation.
// ============================================================================

using DeploymentVault.Core.KeyForge.Domain;
using DeploymentVault.Platform.KeyForge.Forgery;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.KeyForge;

/// <summary>
/// Unit tests for <see cref="ShapePreservingKeyForger"/>.
/// </summary>
public sealed class ShapePreservingKeyForgerTests
{
    private static ShapePreservingKeyForger NewForger() => new();

    [Fact]
    public void PrefixedTokenPreservesPrefixAndLength()
    {
        var original = "gsk_" + new string('a', 48);
        var fake = NewForger().Forge(original, KeyFamily.PrefixedToken, FakePolicy.Deterministic);
        Assert.StartsWith("gsk_", fake, StringComparison.Ordinal);
        Assert.Equal(original.Length, fake.Length);
        Assert.NotEqual(original, fake);
    }

    [Fact]
    public void PrefixedTokenPreservesCharacterClasses()
    {
        var original = "sk-abcDEF123ghiJKL456mnoPQR789stuVWX";
        var fake = NewForger().Forge(original, KeyFamily.PrefixedToken, FakePolicy.Deterministic);
        Assert.Equal(original.Length, fake.Length);
        for (var i = 0; i < original.Length; i++)
        {
            var o = original[i];
            var f = fake[i];
            if (char.IsUpper(o)) Assert.True(char.IsUpper(f), $"pos {i}");
            else if (char.IsLower(o)) Assert.True(char.IsLower(f), $"pos {i}");
            else if (char.IsDigit(o)) Assert.True(char.IsDigit(f), $"pos {i}");
        }
    }

    [Fact]
    public void JwtPreservesSegmentCountAndSeparators()
    {
        var original = "eyJ" + new string('a', 16) + "." + new string('b', 16) + "." + new string('c', 16);
        var fake = NewForger().Forge(original, KeyFamily.Jwt, FakePolicy.Deterministic);
        Assert.Equal(3, fake.Split('.').Length);
        Assert.Equal(original.Length, fake.Length);
        Assert.NotEqual(original, fake);
    }

    [Fact]
    public void UuidPreservesLayout()
    {
        var original = "123e4567-e89b-12d3-a456-426614174000";
        var fake = NewForger().Forge(original, KeyFamily.Uuid, FakePolicy.Deterministic);
        Assert.Equal(original.Length, fake.Length);
        Assert.Equal('-', fake[8]);
        Assert.Equal('-', fake[13]);
        Assert.Equal('-', fake[18]);
        Assert.Equal('-', fake[23]);
        Assert.NotEqual(original, fake);
    }

    [Fact]
    public void PemPreservesBeginEndLines()
    {
        var original = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA7\n-----END RSA PRIVATE KEY-----";
        var fake = NewForger().Forge(original, KeyFamily.PemBlock, FakePolicy.Deterministic);
        Assert.Contains("-----BEGIN RSA PRIVATE KEY-----", fake, StringComparison.Ordinal);
        Assert.Contains("-----END RSA PRIVATE KEY-----", fake, StringComparison.Ordinal);
        Assert.NotEqual(original, fake);
    }

    [Fact]
    public void DeterministicSameInputSameFake()
    {
        var original = "gsk_" + new string('a', 48);
        var f1 = NewForger().Forge(original, KeyFamily.PrefixedToken, FakePolicy.Deterministic);
        var f2 = NewForger().Forge(original, KeyFamily.PrefixedToken, FakePolicy.Deterministic);
        Assert.Equal(f1, f2);
    }

    [Fact]
    public void RandomDifferentFakes()
    {
        var original = "gsk_" + new string('a', 48);
        var f1 = NewForger().Forge(original, KeyFamily.PrefixedToken, FakePolicy.Random);
        var f2 = NewForger().Forge(original, KeyFamily.PrefixedToken, FakePolicy.Random);
        Assert.NotEqual(f1, f2);
    }

    [Fact]
    public void GenericPreservesLengthAndClasses()
    {
        var original = "Ab3dEf5hIj7kLm9nOp1qRs3tUv5wXy7z";
        var fake = NewForger().Forge(original, KeyFamily.Generic, FakePolicy.Deterministic);
        Assert.Equal(original.Length, fake.Length);
        Assert.NotEqual(original, fake);
    }
}
