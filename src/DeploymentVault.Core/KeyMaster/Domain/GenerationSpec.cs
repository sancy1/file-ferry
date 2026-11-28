// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/GenerationSpec.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Immutable description of one generation request. Length is in
//           BYTES for binary kinds (secrets, AES, key pairs) and CHARACTERS
//           for character kinds (Django, Pro). Algorithm/Curve/ModulusBits
//           select the variant inside a kind. Charset toggles, exclusions,
//           prefix, and suffix apply to ProKey only.
// RELATIONSHIPS
//   Used by    : IKeyGenerator.GenerateAsync, KeyMasterViewModel
//   See also   : KeyKind, KeyFormat, GeneratedKey
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>
/// Immutable generation request.
/// </summary>
/// <param name="Kind">Which framework profile to generate for.</param>
/// <param name="Length">Bytes (binary kinds) or characters (Django/Pro).</param>
/// <param name="Algorithm">Variant selector: HS256/HS384/HS512, RSA, ECDSA, Ed25519, Session, Api.</param>
/// <param name="Curve">EC curve: P-256, P-384, P-521.</param>
/// <param name="ModulusBits">RSA modulus: 2048, 3072, 4096.</param>
/// <param name="Format">Output encoding.</param>
/// <param name="UseUpper">Pro: include A-Z.</param>
/// <param name="UseLower">Pro: include a-z.</param>
/// <param name="UseDigits">Pro: include 0-9.</param>
/// <param name="UseSymbols">Pro: include !@#$%^&amp;*().</param>
/// <param name="Exclusions">Pro: characters to remove from the pool.</param>
/// <param name="Prefix">Pro: fixed prefix appended after generation.</param>
/// <param name="Suffix">Pro: fixed suffix appended after generation.</param>
/// <param name="Count">Batch size (1-100). The registry generates one key per call; the VM loops.</param>
public sealed record GenerationSpec(
    KeyKind Kind,
    int Length,
    string Algorithm,
    string Curve,
    int ModulusBits,
    KeyFormat Format,
    bool UseUpper,
    bool UseLower,
    bool UseDigits,
    bool UseSymbols,
    string Exclusions,
    string Prefix,
    string Suffix,
    int Count)
{
    /// <summary>Creates a minimal spec for kinds that need only kind+length+format.</summary>
    public static GenerationSpec Simple(KeyKind kind, int length, KeyFormat format, int count = 1) =>
        new(kind, length, string.Empty, string.Empty, 0, format,
            true, true, true, true, string.Empty, string.Empty, string.Empty, count);
}
