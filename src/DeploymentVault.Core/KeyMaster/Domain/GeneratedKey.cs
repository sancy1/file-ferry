// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/GeneratedKey.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  One generated key (or key pair). Value holds the private/signing
//           material for pairs; PublicValue holds the verification material.
//           EntropyBits is the estimate at generation time. Nothing here is
//           persisted — the session store is in-memory only.
// RELATIONSHIPS
//   Used by    : IKeyGenerator, IKeyMasterStore, KeyMasterViewModel
//   See also   : GenerationSpec, EntropyEstimate
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>
/// A generated key. For asymmetric kinds, <see cref="PublicValue"/> carries
/// the public half (PEM or JWK JSON) and <see cref="Value"/> the private half.
/// </summary>
/// <remarks>
/// Values are immutable <see cref="string"/>s: they cannot be zeroed and remain
/// in managed memory until garbage collection. Copies placed on the OS
/// clipboard persist beyond the app. This is an accepted limitation of the
/// local key-generation feature; do not treat the in-memory store as a secure
/// vault.
/// </remarks>
/// <param name="Value">The primary value (secret string, private key PEM/JWK).</param>
/// <param name="PublicValue">The public half for key pairs; null for secrets.</param>
/// <param name="Kind">The framework profile used.</param>
/// <param name="Format">The output encoding.</param>
/// <param name="ByteCount">Random byte count behind the value (0 for char secrets).</param>
/// <param name="EntropyBits">Estimated entropy in bits.</param>
/// <param name="GeneratedAtUtc">Generation timestamp (UTC).</param>
/// <param name="ProfileLabel">Short descriptor, e.g. "HS256 · 32 bytes".</param>
public sealed record GeneratedKey(
    string Value,
    string? PublicValue,
    KeyKind Kind,
    KeyFormat Format,
    int ByteCount,
    double EntropyBits,
    DateTimeOffset GeneratedAtUtc,
    string ProfileLabel)
{
    /// <summary>True when this is a key pair (has a public half).</summary>
    public bool IsKeyPair => PublicValue is not null;

    /// <summary>Masked display form for lists and history.</summary>
    public string Masked => new string('•', Math.Min(24, Math.Max(8, Value.Length)));
}
