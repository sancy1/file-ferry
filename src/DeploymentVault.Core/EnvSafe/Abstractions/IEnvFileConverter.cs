// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Abstractions/IEnvFileConverter.cs
// layer:    Abstractions
// module:   DeploymentVault.Core.EnvSafe.Abstractions
// since:    Phase A (EnvSafe)
// purpose:  Contract for converting .env content to .env.example content by
//           replacing every value with a deterministic placeholder.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : DeploymentVault.Core.EnvSafe.Domain (PlaceholderStyle,
//                EnvConversionResult)
//   Used by    : EnvFileConverter, EnvSafeViewModel
//   See also   : IEnvFileValidator, ISafetyScanner
//
// NOTES
//   - Stateless. The same instance is safe to share.
//   - The converter preserves variable names, comments, blank lines,
//     quoting style, export prefixes, inline comments, and ordering.
//     Only values change.
// ============================================================================

using DeploymentVault.Core.EnvSafe.Domain;

namespace DeploymentVault.Core.EnvSafe.Abstractions;

/// <summary>
/// Converts .env content to .env.example content.
/// </summary>
public interface IEnvFileConverter
{
    /// <summary>
    /// Converts the input .env content, replacing every KEY=value value with
    /// a placeholder derived from the key name and the chosen style.
    /// </summary>
    /// <param name="content">The raw .env content. Never null.</param>
    /// <param name="style">The placeholder style. Never null.</param>
    /// <returns>The conversion result. Never null.</returns>
    EnvConversionResult Convert(string content, PlaceholderStyle style);
}
