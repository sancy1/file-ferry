// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/SampleProfile.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  The statistical profile detected from a pasted sample key, used
//           by Pro mode's "Match Sample" feature to auto-configure the
//           charset toggles and length. Pure analysis — no generation.
// RELATIONSHIPS
//   Used by    : SampleKeyAnalyzer, KeyMasterViewModel (Match Sample)
//   See also   : GenerationSpec, EntropyEstimate
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>
/// Statistical profile of a sample key: length, detected charset composition,
/// likely encoding, and estimated entropy.
/// </summary>
/// <param name="Length">Character count of the sample.</param>
/// <param name="HasUpper">Contains A-Z.</param>
/// <param name="HasLower">Contains a-z.</param>
/// <param name="HasDigits">Contains 0-9.</param>
/// <param name="HasSymbols">Contains characters outside [A-Za-z0-9].</param>
/// <param name="IsHex">Every character is a hex digit.</param>
/// <param name="IsBase64Url">Every character is base64url-safe.</param>
/// <param name="IsUuid">Matches the canonical UUID shape.</param>
/// <param name="EntropyBits">Estimated entropy of the sample.</param>
/// <param name="DistinctChars">Number of distinct characters observed.</param>
public sealed record SampleProfile(
    int Length,
    bool HasUpper,
    bool HasLower,
    bool HasDigits,
    bool HasSymbols,
    bool IsHex,
    bool IsBase64Url,
    bool IsUuid,
    double EntropyBits,
    int DistinctChars)
{
    /// <summary>Human-readable one-line summary for the UI.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Length} chars" };
            if (IsUuid) parts.Add("UUID");
            else if (IsHex) parts.Add("hex");
            else if (IsBase64Url) parts.Add("base64url");
            parts.Add($"{EntropyBits:F0} bits entropy");
            parts.Add($"{DistinctChars} distinct chars");
            return string.Join(" · ", parts);
        }
    }
}
