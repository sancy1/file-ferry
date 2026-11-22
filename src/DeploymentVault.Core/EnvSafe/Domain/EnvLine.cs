// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Domain/EnvLine.cs
// layer:    Domain
// module:   DeploymentVault.Core.EnvSafe.Domain
// since:    Phase A (EnvSafe)
// purpose:  A single parsed line of .env content. The converter preserves
//           every non-value aspect of the line and replaces only the value.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : IEnvFileConverter, EnvFileConverter
//   See also   : PlaceholderStyle, EnvConversionResult
//
// NOTES
//   - Immutable record. The parser produces one EnvLine per input line.
//   - `Kind` classifies the line: Blank, Comment, KeyValue, or Other.
//     Comments include section headings (they are # lines in .env).
//   - For KeyValue lines, `Key`, `Value`, `HasExportPrefix`,
//     `ValueQuoted`, and `InlineComment` carry the parts the converter
//     needs to rebuild the line with a placeholder value.
// ============================================================================

namespace DeploymentVault.Core.EnvSafe.Domain;

/// <summary>
/// The classification of a parsed .env line.
/// </summary>
public enum EnvLineKind
{
    /// <summary>An empty or whitespace-only line.</summary>
    Blank = 0,

    /// <summary>A comment line (starts with # after optional whitespace).</summary>
    Comment = 1,

    /// <summary>A KEY=value assignment (optionally with export prefix).</summary>
    KeyValue = 2,

    /// <summary>Anything else (preserved as-is, not converted).</summary>
    Other = 3,
}

/// <summary>
/// A single parsed line of .env content.
/// </summary>
/// <param name="LineNumber">The 1-based line number in the source.</param>
/// <param name="Kind">The line classification.</param>
/// <param name="Raw">The original line text, preserved verbatim.</param>
/// <param name="Key">The variable name for KeyValue lines; null otherwise.</param>
/// <param name="Value">The raw value text for KeyValue lines; null otherwise.</param>
/// <param name="HasExportPrefix">True when the line starts with "export ".</param>
/// <param name="ValueQuoted">True when the value was wrapped in quotes.</param>
/// <param name="QuoteChar">The quote character ('"' or '\'') when ValueQuoted; null otherwise.</param>
/// <param name="InlineComment">The inline comment after the value, including the #; null when absent.</param>
public sealed record EnvLine(
    int LineNumber,
    EnvLineKind Kind,
    string Raw,
    string? Key = null,
    string? Value = null,
    bool HasExportPrefix = false,
    bool ValueQuoted = false,
    char? QuoteChar = null,
    string? InlineComment = null);
