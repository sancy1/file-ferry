// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Domain/EnvConversionResult.cs
// layer:    Domain
// module:   DeploymentVault.Core.EnvSafe.Domain
// since:    Phase A (EnvSafe)
// purpose:  The outcome of converting .env content to .env.example content.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : IEnvFileConverter, EnvSafeViewModel
//   See also   : EnvLine, PlaceholderStyle
//
// NOTES
//   - Immutable record. `Success` is false when the input had no KEY=value
//     lines; `Error` carries the user-facing message in that case.
//   - The safety scan is NOT part of this result; it runs separately via
//     ISafetyScanner on the output text.
// ============================================================================

namespace DeploymentVault.Core.EnvSafe.Domain;

/// <summary>
/// The outcome of converting .env content to .env.example content.
/// </summary>
/// <param name="Success">True when at least one KEY=value line was converted.</param>
/// <param name="Output">The converted .env.example text; null on failure.</param>
/// <param name="VariablesDetected">The number of KEY=value lines found.</param>
/// <param name="CommentsPreserved">The number of comment lines preserved.</param>
/// <param name="ValuesReplaced">The number of values replaced with placeholders.</param>
/// <param name="Error">The user-facing error message when Success is false.</param>
public sealed record EnvConversionResult(
    bool Success,
    string? Output,
    int VariablesDetected,
    int CommentsPreserved,
    int ValuesReplaced,
    string? Error);
