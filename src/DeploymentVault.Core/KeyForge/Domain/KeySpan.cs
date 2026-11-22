// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Domain/KeySpan.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyForge.Domain
// since:    Phase A (KeyForge)
// purpose:  A single key-shaped span located in the source text by the
//           extractor. Carries the position, length, family, and the raw
//           original value. The fake is produced later by IKeyForger.
// RELATIONSHIPS
//   Used by    : IKeyExtractor.Extract, KeyForgeViewModel
//   See also   : DetectedKey (the span plus its fake)
// ============================================================================

namespace DeploymentVault.Core.KeyForge.Domain;

/// <summary>
/// A key-shaped span found in the source text.
/// </summary>
/// <param name="Start">The zero-based start index in the source text.</param>
/// <param name="Length">The length of the span in characters.</param>
/// <param name="Family">The detected key family.</param>
/// <param name="Original">The raw original value (the real key).</param>
public sealed record KeySpan(int Start, int Length, KeyFamily Family, string Original);
