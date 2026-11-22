// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Domain/PlaceholderStyle.cs
// layer:    Domain
// module:   DeploymentVault.Core.EnvSafe.Domain
// since:    Phase A (EnvSafe)
// purpose:  The global placeholder style chosen by the user for converted
//           .env.example values.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : IEnvFileConverter, ISafetyScanner, EnvSafeViewModel
//   See also   : EnvLine, EnvConversionResult
//
// NOTES
//   - Deterministic, no ML. The style only changes the wrapper around the
//     key-derived segments; the segments themselves come from the key name.
// ============================================================================

namespace DeploymentVault.Core.EnvSafe.Domain;

/// <summary>
/// The placeholder style applied to every converted value.
/// </summary>
public enum PlaceholderStyle
{
    /// <summary>your_api_key_here (default).</summary>
    YourXHere = 0,

    /// <summary>YOUR_API_KEY_HERE.</summary>
    UpperXHere = 1,

    /// <summary>&lt;your_api_key_here&gt;.</summary>
    AngleXHere = 2,

    /// <summary>CHANGE_ME (constant, no key segments).</summary>
    ChangeMe = 3,
}
