// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Domain/DetectedKey.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyForge.Domain
// since:    Phase A (KeyForge)
// purpose:  A detected key with its shape-identical fake. Used by the
//           detected-keys panel and the real->fake map export.
// RELATIONSHIPS
//   Used by    : KeyForgeViewModel, DetectedKeyRowViewModel
//   See also   : KeySpan (the extraction result before forgery)
// ============================================================================

namespace DeploymentVault.Core.KeyForge.Domain;

/// <summary>
/// A detected key paired with its shape-identical fake.
/// </summary>
/// <param name="Start">The zero-based start index in the source text.</param>
/// <param name="Length">The length of the span in characters.</param>
/// <param name="Family">The detected key family.</param>
/// <param name="Original">The raw original value (the real key).</param>
/// <param name="Fake">The shape-identical fake value.</param>
public sealed record DetectedKey(int Start, int Length, KeyFamily Family, string Original, string Fake);
