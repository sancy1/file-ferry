// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Abstractions/ISafetyScanner.cs
// layer:    Abstractions
// module:   DeploymentVault.Core.EnvSafe.Abstractions
// since:    Phase A (EnvSafe)
// purpose:  Contract for scanning converted .env.example output for values
//           that still look sensitive.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : DeploymentVault.Core.EnvSafe.Domain (SafetyWarning,
//                PlaceholderStyle)
//   Used by    : SafetyScanner, EnvSafeViewModel
//   See also   : IEnvFileConverter, IEnvFileValidator
//
// NOTES
//   - Stateless. The same instance is safe to share.
//   - The scanner runs on the OUTPUT of the converter, not the input.
//   - The chosen placeholder style is passed so the scanner can recognize
//     legitimate placeholders and not flag them.
// ============================================================================

using DeploymentVault.Core.EnvSafe.Domain;

namespace DeploymentVault.Core.EnvSafe.Abstractions;

/// <summary>
/// Scans converted .env.example output for remaining sensitive values.
/// </summary>
public interface ISafetyScanner
{
    /// <summary>
    /// Scans the converted output and returns every line that still looks
    /// sensitive. An empty list means the output is clean.
    /// </summary>
    /// <param name="output">The converted .env.example text. Never null.</param>
    /// <param name="style">The placeholder style used for the conversion.</param>
    /// <returns>The warnings. Never null; empty when clean.</returns>
    IReadOnlyList<SafetyWarning> Scan(string output, PlaceholderStyle style);
}
