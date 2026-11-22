// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Abstractions/IEnvFileValidator.cs
// layer:    Abstractions
// module:   DeploymentVault.Core.EnvSafe.Abstractions
// since:    Phase A (EnvSafe)
// purpose:  Contract for detecting whether .env content contains valid
//           KEY=value pairs. The validation gate runs before conversion.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : EnvFileValidator, EnvSafeViewModel
//   See also   : IEnvFileConverter, ISafetyScanner
//
// NOTES
//   - Stateless. The same instance is safe to share.
// ============================================================================

namespace DeploymentVault.Core.EnvSafe.Abstractions;

/// <summary>
/// Detects whether .env content contains valid KEY=value pairs.
/// </summary>
public interface IEnvFileValidator
{
    /// <summary>
    /// Returns true when the content contains at least one valid KEY=value
    /// line (comments and blank lines do not count).
    /// </summary>
    /// <param name="content">The raw .env content. Never null.</param>
    bool HasKeyValuePairs(string content);

    /// <summary>
    /// Counts the valid KEY=value lines in the content.
    /// </summary>
    /// <param name="content">The raw .env content. Never null.</param>
    int CountKeyValuePairs(string content);
}
