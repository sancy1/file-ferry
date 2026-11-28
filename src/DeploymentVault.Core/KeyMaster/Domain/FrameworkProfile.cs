// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/FrameworkProfile.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Static descriptor per KeyKind: display name, nav category,
//           hard minimums, guardrail text, the common error the constraint
//           avoids, and the preferred output format. This is the "correct by
//           construction" table the UI renders in every guardrail panel.
// RELATIONSHIPS
//   Used by    : KeyMasterViewModel (guardrail panel, nav), generators (min checks)
//   See also   : KeyKind, EntropyEstimate
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>Top-level nav grouping for the left rail.</summary>
public enum KeyCategory
{
    /// <summary>Node.js / JWT ecosystem.</summary>
    NodeJs,

    /// <summary>Python (Django, FastAPI, Flask).</summary>
    Python,

    /// <summary>C# / ASP.NET Core.</summary>
    DotNet,

    /// <summary>Go (Golang).</summary>
    Go,

    /// <summary>Java (JVM).</summary>
    Java,

    /// <summary>Universal Pro mode.</summary>
    Pro,
}

/// <summary>
/// Per-kind framework descriptor: the hard rules and the guardrail copy.
/// </summary>
public sealed record FrameworkProfile(
    KeyKind Kind,
    KeyCategory Category,
    string DisplayName,
    string ShortName,
    int MinBytes,
    int MinChars,
    int DefaultLength,
    int MaxLength,
    KeyFormat PreferredFormat,
    string GuardrailTitle,
    string GuardrailBody,
    string CommonError)
{
    private static readonly IReadOnlyDictionary<KeyKind, FrameworkProfile> Profiles =
        new Dictionary<KeyKind, FrameworkProfile>
        {
            [KeyKind.JwtHmac] = new(KeyKind.JwtHmac, KeyCategory.NodeJs,
                "JWT Symmetric Secret", "HS256/384/512",
                32, 0, 32, 128, KeyFormat.Base64Url,
                "RFC 7518 minimum key length",
                "HS256 requires ≥ 32 bytes (256 bits), HS384 ≥ 48, HS512 ≥ 64. " +
                "The slider will not go below the algorithm minimum. Compatible with " +
                "jose.createSecretKey(secret) and jsonwebtoken.sign(payload, secret, { algorithm }).",
                "JsonWebTokenError: secret or private key must have a value"),

            [KeyKind.JwtRsa] = new(KeyKind.JwtRsa, KeyCategory.NodeJs,
                "JWT RSA Key Pair", "RSA 2048+",
                256, 0, 256, 512, KeyFormat.Pem,
                "RSA modulus minimum",
                "RSA ≥ 2048 bits. The private key signs; the public key verifies. " +
                "PEM PKCS#8 (private) and X.509 SubjectPublicKeyInfo (public) are the " +
                "formats jsonwebtoken and jose expect. Do not commit either to source.",
                "jsonwebtoken rejects RSA keys below 2048 bits"),

            [KeyKind.JwtEcdsa] = new(KeyKind.JwtEcdsa, KeyCategory.NodeJs,
                "JWT ECDSA Key Pair", "P-256/384/521",
                32, 0, 32, 66, KeyFormat.Pem,
                "Fixed NIST curves",
                "ECDSA curves are fixed: P-256, P-384, P-521. PEM export for " +
                "jwt.ParseECPrivateKeyFromPEM / jose importPKCS8. The private key " +
                "signs; the public key verifies.",
                "ES256 requires a P-256 key; curve mismatch fails verification"),

            [KeyKind.DjangoSecret] = new(KeyKind.DjangoSecret, KeyCategory.Python,
                "Django Secret Key", "SECRET_KEY",
                0, 50, 64, 128, KeyFormat.Raw,
                "Django check --deploy minimum",
                "Django's check --deploy fails on keys under 50 characters. Below 32 " +
                "characters is PYSEC-2025-183 — the DRF SimpleJWT short-key weakness. " +
                "Env-safe mode passes .env parsers without quoting.",
                "django.core.exceptions.ImproperlyConfigured: SECRET_KEY must not be empty"),

            [KeyKind.FastApiSecret] = new(KeyKind.FastApiSecret, KeyCategory.Python,
                "FastAPI Secret", "token_urlsafe",
                32, 0, 32, 64, KeyFormat.Base64Url,
                "FastAPI settings minimum",
                "FastAPI settings recommend ≥ 32 bytes. URL-safe Base64 output stores " +
                "in .env without quoting — the canonical secrets.token_urlsafe(32) form.",
                "Weak secret in .env; ImproperlyConfigured on startup"),

            [KeyKind.FlaskSecret] = new(KeyKind.FlaskSecret, KeyCategory.Python,
                "Flask Secret Key", "SECRET_KEY",
                24, 0, 32, 64, KeyFormat.Hex,
                "Flask session vs API secret",
                "Flask uses SECRET_KEY for session cookies and CSRF. Hex is canonical " +
                "for cookie signing; URL-safe Base64 for API secrets. Minimum 24 bytes; " +
                "32 recommended.",
                "Session cookie forgery with a weak or short SECRET_KEY"),

            [KeyKind.DotNetHmac] = new(KeyKind.DotNetHmac, KeyCategory.DotNet,
                "ASP.NET HMAC Signing Key", "HS256/384/512",
                32, 0, 32, 64, KeyFormat.Base64,
                "The 32-byte rule (IDX10720)",
                "Microsoft.AspNetCore.Authentication.JwtBearer requires ≥ 32 bytes " +
                "(256 bits) of UTF-8 or Base64-decoded key material. Base64 output — " +
                "use Convert.FromBase64String(secret) in C#, not Encoding.UTF8.GetBytes().",
                "IDX10720: The key is not of the required length"),

            [KeyKind.DotNetRsa] = new(KeyKind.DotNetRsa, KeyCategory.DotNet,
                "ASP.NET RSA Key Pair", "RSA 2048/4096",
                256, 0, 256, 512, KeyFormat.Pem,
                "IdentityModel.Tokens formats",
                "IdentityModel.Tokens expects PKCS#8 for the private key and X.509 " +
                "SubjectPublicKeyInfo for the public key. PEM headers are standard. " +
                "Do not use XML unless you are on .NET Framework 4.x.",
                "CryptographicException: key format not recognized"),

            [KeyKind.GoHmac] = new(KeyKind.GoHmac, KeyCategory.Go,
                "Go JWT HMAC Key", "HS256",
                32, 0, 32, 64, KeyFormat.Base64,
                "Go jwt signing method",
                "base64.StdEncoding.DecodeString(secret) or hex.DecodeString(secret) " +
                "gives the []byte for jwt.SigningMethodHS256. Decode the format you " +
                "chose — a raw string is not the key bytes.",
                "jwt: HMAC key length mismatch / weak key"),

            [KeyKind.GoAesGcm] = new(KeyKind.GoAesGcm, KeyCategory.Go,
                "Go AES-GCM Key", "16/24/32 bytes",
                16, 0, 32, 32, KeyFormat.Base64,
                "AES-GCM exact lengths",
                "AES-GCM keys must be exactly 16, 24, or 32 bytes. No other length is " +
                "accepted. Labeled \"AES Key for GCM Payload Encryption\".",
                "cipher: key size must be 16, 24, or 32 bytes"),

            [KeyKind.GoEd25519] = new(KeyKind.GoEd25519, KeyCategory.Go,
                "Go Ed25519 Key Pair", "Ed25519",
                32, 0, 32, 32, KeyFormat.Pem,
                "Fixed Ed25519 curve",
                "jwt.ParseEdPrivateKeyFromPEM(os.ReadFile(\"key.pem\")). Ed25519 has no " +
                "configurable length — the curve is fixed. PEM PKCS#8 (private) and " +
                "PKIX (public) are the standard exports.",
                "ed25519: invalid private key length"),

            [KeyKind.GoEcdsa] = new(KeyKind.GoEcdsa, KeyCategory.Go,
                "Go ECDSA Key Pair", "P-256/384/521",
                32, 0, 32, 66, KeyFormat.Pem,
                "Fixed NIST curves",
                "jwt.ParseECPrivateKeyFromPEM. Curves are fixed: P-256, P-384, P-521. " +
                "PEM PKCS#8 (private) and PKIX (public) exports.",
                "ecdsa: invalid curve or key format"),

            [KeyKind.JavaHmac] = new(KeyKind.JavaHmac, KeyCategory.Java,
                "Java HMAC Signing Key", "HS256",
                32, 0, 32, 64, KeyFormat.Base64,
                "JJWT/Nimbus 256-bit minimum",
                "JJWT and Nimbus require ≥ 256 bits. Keys.hmacShaKeyFor(" +
                "Base64.getDecoder().decode(secret)) or Decoders.BASE64.decode(secret). " +
                "Base64 output is ASCII-only — no Unicode surprises.",
                "Key byte array is 0 bits / not secure enough"),

            [KeyKind.ProKey] = new(KeyKind.ProKey, KeyCategory.Pro,
                "Pro Generator", "Custom",
                0, 8, 32, 256, KeyFormat.Raw,
                "Universal generator — no framework assumptions",
                "Pro mode does not know your framework. Read the guardrail table to " +
                "pick a safe length. Below 80 bits of entropy is red. Prefix and " +
                "suffix are added after generation — they do not add entropy.",
                "n/a — verify against your framework's requirements"),
        };

    /// <summary>Looks up the profile for a kind. Throws for unknown kinds.</summary>
    public static FrameworkProfile For(KeyKind kind) =>
        Profiles.TryGetValue(kind, out var p)
            ? p
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown KeyKind.");

    /// <summary>All profiles in declaration order (nav order).</summary>
    public static IReadOnlyList<FrameworkProfile> All { get; } =
        Profiles.Values.ToList();

    /// <summary>Profiles grouped by category, in nav order.</summary>
    public static IReadOnlyList<KeyCategory> CategoryOrder { get; } =
        new[] { KeyCategory.NodeJs, KeyCategory.Python, KeyCategory.DotNet, KeyCategory.Go, KeyCategory.Java, KeyCategory.Pro };
}
