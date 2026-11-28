// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/Ed25519Math.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Minimal Ed25519 primitive over System.Numerics.BigInteger:
//           scalar multiplication on edwards25519, public-key derivation,
//           and point encoding per RFC 8032. Used only for key generation —
//           no signing. Verified against the RFC 8032 test vector in tests.
// RELATIONSHIPS
//   Used by    : Ed25519Generator
//   See also   : RFC 8032 (Edwards-Curve Digital Signature Algorithm)
// ============================================================================

using System.Numerics;
using System.Security.Cryptography;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Ed25519 key-pair generation primitives (RFC 8032). Affine point arithmetic
/// with BigInteger modular reduction — one-time keygen cost only.
/// </summary>
public static class Ed25519Math
{
    // p = 2^255 - 19
    private static readonly BigInteger P = (BigInteger.One << 255) - 19;

    // d = -121665/121666 mod p (edwards25519 curve constant)
    private static readonly BigInteger D =
        Mod(-121665 * ModInv(121666));

    // Base point B. By = 4/5 mod p; Bx is the even-x root (RFC 8032 §5.1).
    private static readonly BigInteger By = Mod(4 * ModInv(5));
    private static readonly BigInteger Bx = BigInteger.Parse(
        "15112221349535400772501151409588531511454012693041857206046113283949847762202",
        System.Globalization.CultureInfo.InvariantCulture);

    private sealed class Point
    {
        public BigInteger X;
        public BigInteger Y;
        public bool Inf;

        public static readonly Point Identity = new() { Inf = true, X = BigInteger.Zero, Y = BigInteger.One };
    }

    private static readonly Point BasePoint = new() { X = Bx, Y = By, Inf = false };

    /// <summary>
    /// Generates an Ed25519 key pair from 32 random seed bytes.
    /// </summary>
    /// <param name="seed">32-byte private seed.</param>
    /// <returns>(privateSeed, publicKey32) — both raw bytes.</returns>
    public static (byte[] PrivateSeed, byte[] PublicKey) GenerateKeyPair(byte[] seed)
    {
        if (seed.Length != 32) throw new ArgumentException("Ed25519 seed must be 32 bytes.", nameof(seed));

        // h = SHA512(seed); clamp the scalar a = h[0..32].
        var h = SHA512.HashData(seed);
        var a = new byte[32];
        Array.Copy(h, a, 32);
        a[0] &= 248;
        a[31] &= 127;
        a[31] |= 64;

        var scalar = FromLittleEndian(a);
        var aPoint = ScalarMult(scalar, BasePoint);
        var publicKey = EncodePoint(aPoint);
        return (seed, publicKey);
    }

    /// <summary>Generates a fresh Ed25519 key pair from the OS CSPRNG.</summary>
    public static (byte[] PrivateSeed, byte[] PublicKey) GenerateKeyPair()
    {
        var seed = new byte[32];
        RandomNumberGenerator.Fill(seed);
        return GenerateKeyPair(seed);
    }

    // ------------------------------------------------------------------
    // Point arithmetic (affine, mod p)
    // ------------------------------------------------------------------

    private static Point ScalarMult(BigInteger scalar, Point point)
    {
        var result = Point.Identity;
        var addend = point;
        for (var i = 0; i < 256; i++)
        {
            if (((scalar >> i) & BigInteger.One) == BigInteger.One)
            {
                result = Add(result, addend);
            }
            addend = Double(addend);
        }
        return result;
    }

    // Edwards25519 addition (twisted Edwards, a = -1):
    //   x3 = (x1*y2 + y1*x2) / (1 + d*x1*x2*y1*y2)
    //   y3 = (y1*y2 + x1*x2) / (1 - d*x1*x2*y1*y2)
    // The unified formula also doubles a point, so Double delegates to Add.
    private static Point Add(Point p1, Point p2)
    {
        if (p1.Inf) return p2;
        if (p2.Inf) return p1;
        if (p1.X == Mod(-p2.X) && p1.Y == p2.Y) return Point.Identity;

        var x1y2 = Mod(p1.X * p2.Y);
        var y1x2 = Mod(p1.Y * p2.X);
        var x1x2 = Mod(p1.X * p2.X);
        var y1y2 = Mod(p1.Y * p2.Y);
        var dxy = Mod(D * x1x2 * y1y2);

        var x3 = Mod((x1y2 + y1x2) * ModInv(Mod(1 + dxy)));
        var y3 = Mod((y1y2 + x1x2) * ModInv(Mod(1 - dxy)));
        if (x3 == BigInteger.Zero && y3 == BigInteger.One) return Point.Identity;
        return new Point { X = x3, Y = y3, Inf = false };
    }

    private static Point Double(Point p) => Add(p, p);

    // ------------------------------------------------------------------
    // Encoding (RFC 8032 §5.1.2 / §5.1.3)
    // ------------------------------------------------------------------

    private static byte[] EncodePoint(Point p)
    {
        var yBytes = ToLittleEndian32(p.Y);
        if ((p.X & BigInteger.One) == BigInteger.One)
        {
            yBytes[31] |= 0x80;
        }
        return yBytes;
    }

    // ------------------------------------------------------------------
    // BigInteger helpers
    // ------------------------------------------------------------------

    private static BigInteger Mod(BigInteger value)
    {
        var r = value % P;
        return r < BigInteger.Zero ? r + P : r;
    }

    private static BigInteger ModInv(BigInteger value) =>
        BigInteger.ModPow(Mod(value), P - 2, P);

    private static BigInteger FromLittleEndian(byte[] bytes)
    {
        var result = BigInteger.Zero;
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            result = (result << 8) | bytes[i];
        }
        return result;
    }

    private static byte[] ToLittleEndian32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var result = new byte[32];
        Array.Copy(bytes, result, Math.Min(bytes.Length, 32));
        return result;
    }
}
