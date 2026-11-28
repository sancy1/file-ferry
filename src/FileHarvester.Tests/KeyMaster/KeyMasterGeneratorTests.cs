// ============================================================================
// filepath: src/FileHarvester.Tests/KeyMaster/KeyMasterGeneratorTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.KeyMaster
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Generator correctness tests: every KeyKind produces output that
//           satisfies its framework's hard rules (length, charset, format).
//           Includes the RFC 8032 Ed25519 test vector and entropy checks.
// RELATIONSHIPS
//   Tests     : DeploymentVault.Platform.KeyMaster.Generation.*
//   See also  : FrameworkProfile (guardrails under test)
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;
using DeploymentVault.Platform.KeyMaster.Generation;
using FluentAssertions;

namespace FileHarvester.Tests.KeyMaster;

/// <summary>
/// Per-generator correctness tests.
/// </summary>
public class KeyMasterGeneratorTests
{
    private static GenerationSpec Spec(
        KeyKind kind,
        int length = 32,
        string algorithm = "",
        KeyFormat format = KeyFormat.Base64Url,
        string curve = "",
        int modulusBits = 2048,
        string prefix = "",
        string suffix = "",
        string exclusions = "",
        bool useUpper = true,
        bool useLower = true,
        bool useDigits = true,
        bool useSymbols = true) =>
        new(kind, length, algorithm, curve, modulusBits, format,
            useUpper, useLower, useDigits, useSymbols,
            exclusions, prefix, suffix, 1);

    // ---------------------------------------------------------------
    // Symmetric HMAC (JwtHmac / DotNetHmac / GoHmac / JavaHmac)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(KeyKind.JwtHmac, "HS256", 32)]
    [InlineData(KeyKind.JwtHmac, "HS384", 48)]
    [InlineData(KeyKind.JwtHmac, "HS512", 64)]
    [InlineData(KeyKind.DotNetHmac, "", 32)]
    [InlineData(KeyKind.GoHmac, "", 32)]
    [InlineData(KeyKind.JavaHmac, "", 32)]
    public async Task HmacEnforcesMinimumBytes(KeyKind kind, string alg, int minBytes)
    {
        var gen = new SymmetricSecretGenerator();
        var key = await gen.GenerateAsync(Spec(kind, length: 8, algorithm: alg));

        var padded = key.Value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var bytes = Convert.FromBase64String(padded);
        bytes.Length.Should().BeGreaterThanOrEqualTo(minBytes);
        // EntropyEstimate caps at 256 bits by design.
        key.EntropyBits.Should().Be(Math.Min(256.0, minBytes * 8.0));
    }

    [Fact]
    public async Task HmacHexFormatProducesHex()
    {
        var gen = new SymmetricSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtHmac, 32, "HS256", KeyFormat.Hex));
        key.Value.Should().MatchRegex("^[0-9a-f]+$");
        key.Value.Length.Should().Be(64); // 32 bytes hex
    }

    [Fact]
    public async Task HmacBase64UrlHasNoPaddingOrSpecialChars()
    {
        var gen = new SymmetricSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtHmac, 32, "HS256", KeyFormat.Base64Url));
        key.Value.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    // ---------------------------------------------------------------
    // Django
    // ---------------------------------------------------------------

    [Fact]
    public async Task DjangoDefaultCharsetUsesExactDjangoAlphabet()
    {
        var gen = new DjangoSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.DjangoSecret, 50, "Django", KeyFormat.Raw));
        key.Value.Length.Should().Be(50);
        const string charset = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*(-_=+)";
        key.Value.All(c => charset.Contains(c)).Should().BeTrue();
    }

    [Fact]
    public async Task DjangoUnder50IsClampedTo50()
    {
        var gen = new DjangoSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.DjangoSecret, 10, "Django", KeyFormat.Raw));
        key.Value.Length.Should().Be(50);
    }

    [Fact]
    public async Task DjangoEnvSafeExcludesDollar()
    {
        var gen = new DjangoSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.DjangoSecret, 50, "EnvSafe", KeyFormat.Raw));
        key.Value.Should().NotContain("$");
    }

    // ---------------------------------------------------------------
    // FastAPI / Flask
    // ---------------------------------------------------------------

    [Fact]
    public async Task FastApiProducesUrlSafeBase64()
    {
        var gen = new FastApiSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.FastApiSecret, 32));
        key.Value.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        key.Value.Length.Should().BeGreaterThanOrEqualTo(43); // 32 bytes -> 43 chars
    }

    [Fact]
    public async Task FlaskSessionDefaultsToHex()
    {
        var gen = new FlaskSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.FlaskSecret, 32, "Session", KeyFormat.Raw));
        key.Value.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public async Task FlaskApiDefaultsToBase64Url()
    {
        var gen = new FlaskSecretGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.FlaskSecret, 32, "Api", KeyFormat.Raw));
        key.Value.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    // ---------------------------------------------------------------
    // AES-GCM
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public async Task AesGcmAcceptsValidSizes(int size)
    {
        var gen = new AesGcmKeyGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.GoAesGcm, size));
        key.Value.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(64)]
    public async Task AesGcmRejectsInvalidSizes(int size)
    {
        var gen = new AesGcmKeyGenerator();
        var act = () => gen.GenerateAsync(Spec(KeyKind.GoAesGcm, size));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------------------------------------------------------------
    // RSA
    // ---------------------------------------------------------------

    [Fact]
    public async Task RsaProducesPkcs8PrivateAndSpkiPublic()
    {
        var gen = new RsaKeyPairGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtRsa, modulusBits: 2048));
        key.Value.Should().Contain("BEGIN PRIVATE KEY");
        key.PublicValue.Should().Contain("BEGIN PUBLIC KEY");
        key.Value.Should().NotContain("BEGIN RSA PRIVATE KEY"); // PKCS#8, not PKCS#1
    }

    [Fact]
    public async Task RsaPkcs1AlgorithmProducesRsaPrivateKey()
    {
        var gen = new RsaKeyPairGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtRsa, algorithm: "Pkcs1", format: KeyFormat.Pem, modulusBits: 2048));
        key.Value.Should().Contain("BEGIN RSA PRIVATE KEY");
    }

    [Fact]
    public async Task RsaJwkFormatProducesJson()
    {
        var gen = new RsaKeyPairGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtRsa, format: KeyFormat.Json, modulusBits: 2048));
        key.Value.Should().Contain("\"kty\":\"RSA\"");
        key.PublicValue.Should().Contain("\"kty\":\"RSA\"");
    }

    // ---------------------------------------------------------------
    // ECDSA
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("P-256")]
    [InlineData("P-384")]
    [InlineData("P-521")]
    public async Task EcdsaAllCurvesProducePem(string curve)
    {
        var gen = new EcdsaKeyPairGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtEcdsa, curve: curve));
        key.Value.Should().Contain("BEGIN PRIVATE KEY");
        key.PublicValue.Should().Contain("BEGIN PUBLIC KEY");
    }

    [Fact]
    public async Task EcdsaJwkProducesEcJson()
    {
        var gen = new EcdsaKeyPairGenerator();
        var key = await gen.GenerateAsync(Spec(KeyKind.JwtEcdsa, format: KeyFormat.Json, curve: "P-256"));
        key.Value.Should().Contain("\"kty\":\"EC\"");
        key.Value.Should().Contain("\"crv\":\"P-256\"");
    }

    // ---------------------------------------------------------------
    // Ed25519 — RFC 8032 test vector
    // ---------------------------------------------------------------

    [Fact]
    public void Ed25519MathRfc8032TestVector()
    {
        // RFC 8032 §7.1 TEST 1: seed = 9d61b19deffd5a60ba844af492ec2cc4
        // 4449c5697b326919703bac031cae7f60, public key =
        // d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a
        var seed = Convert.FromHexString(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        var (_, publicKey) = Ed25519Math.GenerateKeyPair(seed);
        Convert.ToHexString(publicKey).ToLowerInvariant().Should().Be(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
    }

    [Fact]
    public async Task Ed25519GeneratorProducesPem()
    {
        var gen = new Ed25519Generator();
        var key = await gen.GenerateAsync(Spec(KeyKind.GoEd25519));
        key.Value.Should().Contain("BEGIN PRIVATE KEY");
        key.PublicValue.Should().Contain("BEGIN PUBLIC KEY");
    }

    [Fact]
    public async Task Ed25519JwkProducesOkpJson()
    {
        var gen = new Ed25519Generator();
        var key = await gen.GenerateAsync(Spec(KeyKind.GoEd25519, format: KeyFormat.Json));
        key.Value.Should().Contain("\"kty\":\"OKP\"");
        key.Value.Should().Contain("\"crv\":\"Ed25519\"");
    }

    // ---------------------------------------------------------------
    // Pro mode
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProExclusionsAreAbsent()
    {
        var gen = new ProGenerator();
        var key = await gen.GenerateAsync(Spec(
            KeyKind.ProKey, 64, format: KeyFormat.Raw, exclusions: "0O1lI"));
        key.Value.Should().NotContain("0").And.NotContain("O")
            .And.NotContain("1").And.NotContain("l")
            .And.NotContain("I");
    }

    [Fact]
    public async Task ProPrefixSuffixApplied()
    {
        var gen = new ProGenerator();
        var key = await gen.GenerateAsync(Spec(
            KeyKind.ProKey, 32, format: KeyFormat.Raw, prefix: "sk_live_", suffix: "_prod"));
        key.Value.Should().StartWith("sk_live_").And.EndWith("_prod");
    }

    [Fact]
    public async Task ProEmptyPoolThrows()
    {
        var gen = new ProGenerator();
        var act = () => gen.GenerateAsync(Spec(
            KeyKind.ProKey, 32,
            useUpper: false, useLower: false, useDigits: false, useSymbols: false));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ProEntropyCountsBodyOnly()
    {
        var gen = new ProGenerator();
        var key = await gen.GenerateAsync(Spec(
            KeyKind.ProKey, 32, format: KeyFormat.Raw, prefix: "sk_", suffix: "_prod"));
        // Pool = 26+26+10+8 = 70 chars. Entropy = 32 * log2(70) ≈ 196 bits.
        key.EntropyBits.Should().BeGreaterThan(190).And.BeLessThan(200);
    }

    // ---------------------------------------------------------------
    // SampleKeyAnalyzer
    // ---------------------------------------------------------------

    [Fact]
    public void AnalyzerDetectsHex()
    {
        var profile = SampleKeyAnalyzer.Analyze("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");
        profile.Should().NotBeNull();
        profile!.IsHex.Should().BeTrue();
        profile.Length.Should().Be(32);
    }

    [Fact]
    public void AnalyzerDetectsUuid()
    {
        var profile = SampleKeyAnalyzer.Analyze("550e8400-e29b-41d4-a716-446655440000");
        profile.Should().NotBeNull();
        profile!.IsUuid.Should().BeTrue();
    }

    [Fact]
    public void AnalyzerDetectsBase64Url()
    {
        var profile = SampleKeyAnalyzer.Analyze("dGVzdC1rZXktd2l0aC1iYXNlNjQtdXJsLWVuY29kaW5n");
        profile.Should().NotBeNull();
        profile!.IsBase64Url.Should().BeTrue();
    }

    [Fact]
    public void AnalyzerNullForEmpty()
    {
        SampleKeyAnalyzer.Analyze("").Should().BeNull();
        SampleKeyAnalyzer.Analyze(null).Should().BeNull();
        SampleKeyAnalyzer.Analyze("   ").Should().BeNull();
    }

    // ---------------------------------------------------------------
    // FrameworkProfile
    // ---------------------------------------------------------------

    [Fact]
    public void ProfileAllKindsHaveProfiles()
    {
        foreach (KeyKind kind in Enum.GetValues<KeyKind>())
        {
            var profile = FrameworkProfile.For(kind);
            profile.DisplayName.Should().NotBeNullOrEmpty();
            profile.MinBytes.Should().BeGreaterThanOrEqualTo(0);
            profile.DefaultLength.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void ProfileNodeCategoryHasThreeKinds()
    {
        var kinds = FrameworkProfile.All
            .Where(p => p.Category == KeyCategory.NodeJs)
            .Select(p => p.Kind)
            .ToList();
        kinds.Should().HaveCount(3); // JwtHmac, JwtRsa, JwtEcdsa
    }

    // ---------------------------------------------------------------
    // Audit regression tests (KM-1, KM-2, KM-3, KM-7)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("P-256", 32)]
    [InlineData("P-384", 48)]
    [InlineData("P-521", 66)]
    public async Task EcdsaJwkCoordinatesArePaddedToCurveSize(string curve, int size)
    {
        var gen = new EcdsaKeyPairGenerator();
        for (var i = 0; i < 50; i++)
        {
            var key = await gen.GenerateAsync(Spec(KeyKind.JwtEcdsa, format: KeyFormat.Json, curve: curve));
            using var doc = System.Text.Json.JsonDocument.Parse(key.Value);
            var root = doc.RootElement;
            foreach (var name in new[] { "x", "y", "d" })
            {
                var b64 = root.GetProperty(name).GetString()!;
                var bytes = Convert.FromBase64String(
                    b64.Replace('-', '+').Replace('_', '/') + new string('=', (4 - b64.Length % 4) % 4));
                bytes.Length.Should().Be(size, $"{curve} {name} must be {size} bytes per RFC 7518");
            }
        }
    }

    [Fact]
    public async Task ProJsonEscapesControlCharacters()
    {
        var gen = new ProGenerator();
        var key = await gen.GenerateAsync(Spec(
            KeyKind.ProKey, 16, format: KeyFormat.Json, prefix: "a\nb\t", suffix: "\"q\""));
        using var doc = System.Text.Json.JsonDocument.Parse(key.Value);
        var value = doc.RootElement.GetProperty("key").GetString()!;
        value.Should().StartWith("a\nb\t").And.EndWith("\"q\"");
    }

    [Theory]
    [InlineData(KeyKind.JwtRsa, KeyFormat.Hex, KeyFormat.Pem)]
    [InlineData(KeyKind.JwtRsa, KeyFormat.Base64, KeyFormat.Pem)]
    [InlineData(KeyKind.JwtEcdsa, KeyFormat.Raw, KeyFormat.Pem)]
    [InlineData(KeyKind.GoEd25519, KeyFormat.Base64Url, KeyFormat.Pem)]
    [InlineData(KeyKind.GoEd25519, KeyFormat.Hex, KeyFormat.Hex)]
    public async Task GeneratedKeyFormatRecordsEffectiveFormat(
        KeyKind kind, KeyFormat requested, KeyFormat expected)
    {
        IKeyGenerator gen = kind switch
        {
            KeyKind.JwtRsa => new RsaKeyPairGenerator(),
            KeyKind.JwtEcdsa => new EcdsaKeyPairGenerator(),
            _ => new Ed25519Generator(),
        };
        var key = await gen.GenerateAsync(Spec(kind, format: requested, modulusBits: 2048));
        key.Format.Should().Be(expected);
    }

    [Theory]
    [InlineData(KeyKind.DjangoSecret, 200, 128)]
    [InlineData(KeyKind.FastApiSecret, 200, 64)]
    [InlineData(KeyKind.FlaskSecret, 200, 64)]
    public async Task CharacterAndByteSecretsClampToProfileMax(KeyKind kind, int requested, int expected)
    {
        IKeyGenerator gen = kind switch
        {
            KeyKind.DjangoSecret => new DjangoSecretGenerator(),
            KeyKind.FastApiSecret => new FastApiSecretGenerator(),
            _ => new FlaskSecretGenerator(),
        };
        // Flask "Api" mode + Raw format yields Base64URL; FastAPI Raw yields Base64URL.
        var key = await gen.GenerateAsync(Spec(
            kind, length: requested, algorithm: kind == KeyKind.FlaskSecret ? "Api" : "", format: KeyFormat.Raw));
        if (kind == KeyKind.DjangoSecret)
        {
            key.Value.Length.Should().Be(expected);
        }
        else
        {
            // Byte secrets: decode the Base64URL value back to bytes.
            var b64 = key.Value.Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            Convert.FromBase64String(b64).Length.Should().Be(expected);
        }
    }
}
