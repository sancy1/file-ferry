// ============================================================================
// FILE:     src/FileHarvester.Core/Implementation/LlmPromptGenerator.cs
// PURPOSE:  Builds the copyable help prompt for a provider profile. The
//           generated text is displayed to the user; the user copies it
//           into any LLM (ChatGPT, Claude, Gemini) to get step-by-step
//           instructions for obtaining the provider credentials.
//           The application itself never calls an LLM.
// RELATION: Consumed by the C.8 Cloud Profile Form. Pure: no I/O, no
//           resilience, no network.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Implementation;

/// <summary>
/// Builds the copyable LLM help prompt for a provider profile.
/// </summary>
/// <remarks>
/// <para>
/// The prompt is a plain string that the user copies and pastes into
/// any LLM of their choice. The application never contacts an LLM.
/// </para>
/// <para>
/// The profile template contains the literal token <c>[DisplayName]</c>.
/// The generator replaces it with the profile's DisplayName. If the
/// template does not contain the token, the generator prepends a header
/// line naming the provider.
/// </para>
/// </remarks>
public static class LlmPromptGenerator
{
    private const string DisplayNameToken = "[DisplayName]";

    /// <summary>
    /// Generates the copyable help prompt for the given profile.
    /// </summary>
    /// <param name="profile">The profile. Never null.</param>
    /// <returns>
    /// The prompt text. Never null; never empty for any built-in profile.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// When <paramref name="profile"/> is null.
    /// </exception>
    public static string Generate(ProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var template = profile.HelpPromptTemplate ?? string.Empty;

        if (template.Contains(DisplayNameToken, StringComparison.Ordinal))
        {
            return template.Replace(
                DisplayNameToken,
                profile.DisplayName,
                StringComparison.Ordinal);
        }

        if (template.Length == 0)
        {
            return "I need to obtain credentials for " + profile.DisplayName + ".";
        }

        return "I need to obtain credentials for " + profile.DisplayName + ":" +
               System.Environment.NewLine +
               System.Environment.NewLine +
               template;
    }
}
