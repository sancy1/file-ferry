// ============================================================================
// FILE:     src/FileHarvester.Core/Implementation/DelimiterTemplate.cs
// PURPOSE:  Expands placeholder tokens in a delimiter template string. Used
//           by BundleComposer (Phase 1.3) in SingleMerged mode.
// RELATION: Consumed by BundleComposer. Pure logic: no I/O, no resilience,
//           no dependencies. Placeholders: {path}, {filename}, {index},
//           {size}, {hash}, {modified}.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Implementation;

/// <summary>
/// Expands delimiter templates like "===== {path} =====" into the actual
/// delimiter text for one file.
/// </summary>
internal static class DelimiterTemplate
{
    /// <summary>
    /// Expands the template for one file content record.
    /// </summary>
    /// <param name="template">
    /// The template string. May contain any of the placeholders listed in
    /// the class doc. Any text not inside a placeholder is copied through
    /// unchanged.
    /// </param>
    /// <param name="content">The file content to expand for.</param>
    /// <param name="index">
    /// Zero-based position of this file in the composed output. Used by
    /// the {index} placeholder.
    /// </param>
    /// <returns>The expanded delimiter string.</returns>
    internal static string Expand(
        string template,
        FileContent content,
        int index)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(content);

        if (template.Length == 0)
        {
            return string.Empty;
        }

        var result = template;

        result = result.Replace("{path}",     content.File.AbsolutePath, StringComparison.Ordinal);
        result = result.Replace("{filename}", Path.GetFileName(content.File.AbsolutePath), StringComparison.Ordinal);
        result = result.Replace("{index}",    index.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        result = result.Replace("{size}",     content.RawBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        result = result.Replace("{hash}",     content.Sha256, StringComparison.Ordinal);
        result = result.Replace("{modified}", content.File.LastModifiedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return result;
    }
}
