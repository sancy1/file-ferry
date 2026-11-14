// ============================================================================
// FILE:     src/Prompt.Core/Domain/ImportResult.cs
// PURPOSE:  Outcome of importing a source file as a prompt. Carries success,
//           the created prompt, and any error message.
// RELATION: Returned by IPromptImporter.ImportFileAsync (P3.5). Consumed by
//           PromptImportDialogViewModel (P3.6).
// NOTES:    Failures are values, not exceptions. On success the created
//           Prompt is populated; on failure it is null and ErrorMessage names
//           the reason. Follows the Ok/Fail factory pattern established by
//           SaveResult in FileHarvester.Core.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Outcome of importing a source file as a prompt.
/// </summary>
/// <param name="Success">True when the import completed.</param>
/// <param name="Prompt">The created prompt, or null on failure.</param>
/// <param name="ErrorMessage">Populated when <see cref="Success"/> is false.</param>
public sealed record ImportResult(
    bool Success,
    Prompt? Prompt,
    string? ErrorMessage = null)
{
    /// <summary>Builds a successful import result.</summary>
    public static ImportResult Ok(Prompt prompt) => new(true, prompt);

    /// <summary>Builds a failed import result.</summary>
    public static ImportResult Fail(string reason) => new(false, null, reason);
}