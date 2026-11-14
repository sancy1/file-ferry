// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/ITextCleaner.cs
// PURPOSE:  Contract for deterministic text cleanup. Synchronous
//           because cleanup is a bounded, CPU-only transformation.
//           AI-assisted cleanup, when it arrives in a separate
//           milestone, is a different interface with a different
//           failure profile.
// RELATION: Implemented in Phase 2 by the deterministic cleaner.
//           Consumed by every extraction pipeline after the engine.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Normalizes extracted text without changing its meaning.
/// </summary>
/// <remarks>
/// Implementations must be pure: same input, same output, no state.
/// They must not summarize, translate, or rewrite. They normalize
/// whitespace, collapse repeated blank lines, and restore paragraph
/// breaks where detectable. Uncertain words are preserved as written.
/// </remarks>
public interface ITextCleaner
{
    /// <summary>
    /// Normalizes the input text.
    /// </summary>
    /// <param name="text">
    /// The text to normalize. Null is rejected by the implementation.
    /// </param>
    /// <returns>
    /// The normalized text. Never null.
    /// </returns>
    string Clean(string text);
}