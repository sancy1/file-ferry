// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Domain/SafetyWarning.cs
// layer:    Domain
// module:   DeploymentVault.Core.EnvSafe.Domain
// since:    Phase A (EnvSafe)
// purpose:  A single safety-scan finding: a line in the converted output
//           that still looks sensitive.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : ISafetyScanner, EnvSafeViewModel
//   See also   : PlaceholderStyle
//
// NOTES
//   - Immutable record. `LineNumber` is 1-based in the scanned text.
//   - `Kind` is a stable string identifier (e.g. "api-key-shaped") so the
//     UI can group or style warnings without parsing free text.
// ============================================================================

namespace DeploymentVault.Core.EnvSafe.Domain;

/// <summary>
/// A single safety-scan finding.
/// </summary>
/// <param name="LineNumber">The 1-based line number in the scanned text.</param>
/// <param name="Kind">A stable identifier for the warning category.</param>
/// <param name="Detail">A short human-readable explanation.</param>
/// <param name="LineText">The offending line text (trimmed).</param>
public sealed record SafetyWarning(
    int LineNumber,
    string Kind,
    string Detail,
    string LineText);
