// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/EntropyEstimate.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Entropy estimate for a generated key or a live Pro-mode
//           configuration. Bits = length * log2(pool size), capped at 256.
//           Level drives the meter color: Secure >= 128, Warning 80-127,
//           Critical < 80.
// RELATIONSHIPS
//   Used by    : GeneratedKey, KeyMasterViewModel (live meter), SampleProfile
//   See also   : EntropyCalculator (Platform)
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>Entropy strength band for the meter color.</summary>
public enum EntropyLevel
{
    /// <summary>128 bits or more — green.</summary>
    Secure,

    /// <summary>80-127 bits — amber.</summary>
    Warning,

    /// <summary>Below 80 bits — red.</summary>
    Critical,
}

/// <summary>
/// An entropy estimate: bit strength, band, and a display label.
/// </summary>
/// <param name="Bits">Estimated entropy in bits (capped at 256).</param>
/// <param name="Level">The strength band.</param>
/// <param name="Label">Short display label, e.g. "256 bits".</param>
public sealed record EntropyEstimate(double Bits, EntropyLevel Level, string Label)
{
    /// <summary>Computes an estimate from output length and charset pool size.</summary>
    /// <param name="length">Number of characters (or bytes for binary keys).</param>
    /// <param name="poolSize">Number of distinct symbols in the pool.</param>
    /// <returns>The estimate. Bits are capped at 256.</returns>
    public static EntropyEstimate FromPool(int length, int poolSize)
    {
        if (length <= 0 || poolSize <= 1)
        {
            return new EntropyEstimate(0, EntropyLevel.Critical, "0 bits");
        }

        var rawBits = length * Math.Log2(poolSize);
        var bits = Math.Min(256.0, rawBits);
        var level = bits >= 128 ? EntropyLevel.Secure
            : bits >= 80 ? EntropyLevel.Warning
            : EntropyLevel.Critical;
        // KM-4: the meter caps at 256, but the label reports the true estimate.
        return new EntropyEstimate(bits, level, $"{rawBits:F0} bits");
    }

    /// <summary>Computes an estimate for a fixed byte length (binary keys).</summary>
    /// <param name="byteCount">Number of random bytes.</param>
    /// <returns>Entropy = byteCount * 8, capped at 256.</returns>
    public static EntropyEstimate FromBytes(int byteCount)
    {
        var rawBits = byteCount * 8.0;
        var bits = Math.Min(256.0, rawBits);
        var level = bits >= 128 ? EntropyLevel.Secure
            : bits >= 80 ? EntropyLevel.Warning
            : EntropyLevel.Critical;
        // KM-4: the meter caps at 256, but the label reports the true estimate.
        return new EntropyEstimate(bits, level, $"{rawBits:F0} bits");
    }
}
