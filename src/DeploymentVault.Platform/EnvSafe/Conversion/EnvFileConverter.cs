// ============================================================================
// filepath: src/DeploymentVault.Platform/EnvSafe/Conversion/EnvFileConverter.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.EnvSafe.Conversion
// since:    Phase A (EnvSafe)
// purpose:  Converts .env content to .env.example content line by line,
//           replacing every value with a deterministic placeholder derived
//           from the key name and the chosen style.
//
// RELATIONSHIPS
//   Implements : IEnvFileConverter (DeploymentVault.Core.EnvSafe.Abstractions)
//   Depends on : DeploymentVault.Core.EnvSafe.Domain (EnvLine, EnvLineKind,
//                PlaceholderStyle, EnvConversionResult)
//   Used by    : DeploymentVaultServiceCollectionExtensions, EnvSafeViewModel
//   See also   : EnvFileValidator, SafetyScanner
//
// NOTES
//   - Preserves: variable names, comments (#, ##), blank lines, quoting
//     style, export KEY=value prefix, inline comments, original ordering.
//     Only values change.
//   - Placeholder algorithm (deterministic, no ML):
//       1. Normalize the key: uppercase, split on '_'.
//       2. If the key ends with a known sensitive suffix (API_KEY,
//          SECRET_KEY, TOKEN, PASSWORD, JWT_SECRET, ACCESS_KEY,
//          CLIENT_SECRET, URL, DATABASE_URL, WEBHOOK_URL, HOST, PORT,
//          REGION, EMAIL, USERNAME, CLIENT_ID), the placeholder uses the
//          suffix segments (e.g. STRIPE_API_KEY -> your_api_key_here).
//       3. Any other key uses all segments lowercased
//          (e.g. MY_CUSTOM_VAR -> your_my_custom_var_here).
//       4. Style wraps the segments: your_x_here (default),
//          YOUR_X_HERE, <your_x_here>, CHANGE_ME.
//   - Quoted values stay quoted with the same quote character.
// ============================================================================

using System.Text;
using DeploymentVault.Core.EnvSafe.Abstractions;
using DeploymentVault.Core.EnvSafe.Domain;

namespace DeploymentVault.Platform.EnvSafe.Conversion;

/// <summary>
/// Converts .env content to .env.example content.
/// </summary>
public sealed class EnvFileConverter : IEnvFileConverter
{
    private static readonly string[][] KnownSuffixes =
    {
        new[] { "DATABASE", "URL" },
        new[] { "WEBHOOK", "URL" },
        new[] { "API", "KEY" },
        new[] { "SECRET", "KEY" },
        new[] { "JWT", "SECRET" },
        new[] { "ACCESS", "KEY" },
        new[] { "CLIENT", "SECRET" },
        new[] { "CLIENT", "ID" },
        new[] { "TOKEN" },
        new[] { "PASSWORD" },
        new[] { "URL" },
        new[] { "HOST" },
        new[] { "PORT" },
        new[] { "REGION" },
        new[] { "EMAIL" },
        new[] { "USERNAME" },
    };

    /// <inheritdoc/>
    public EnvConversionResult Convert(string content, PlaceholderStyle style)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = content.Split('\n');
        var output = new StringBuilder(content.Length);
        var variables = 0;
        var comments = 0;
        var replaced = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = ParseLine(i + 1, raw);

            switch (line.Kind)
            {
                case EnvLineKind.Blank:
                case EnvLineKind.Comment:
                case EnvLineKind.Other:
                    output.Append(raw);
                    if (line.Kind == EnvLineKind.Comment) comments++;
                    break;

                case EnvLineKind.KeyValue:
                    variables++;
                    replaced++;
                    output.Append(Rebuild(line, style));
                    break;
            }

            if (i < lines.Length - 1) output.Append('\n');
        }

        if (variables == 0)
        {
            return new EnvConversionResult(
                Success: false,
                Output: null,
                VariablesDetected: 0,
                CommentsPreserved: comments,
                ValuesReplaced: 0,
                Error: "No valid environment variables were detected.\nExpected format: KEY=value\nExample: API_KEY=your_api_key");
        }

        return new EnvConversionResult(
            Success: true,
            Output: output.ToString(),
            VariablesDetected: variables,
            CommentsPreserved: comments,
            ValuesReplaced: replaced,
            Error: null);
    }

    private static string Rebuild(EnvLine line, PlaceholderStyle style)
    {
        var sb = new StringBuilder();
        sb.Append(line.Raw.AsSpan(0, line.Raw.Length - line.Raw.TrimStart().Length)); // leading whitespace
        if (line.HasExportPrefix) sb.Append("export ");
        sb.Append(line.Key);
        sb.Append('=');

        var placeholder = BuildPlaceholder(line.Key!, style);
        if (line.ValueQuoted && line.QuoteChar.HasValue)
        {
            sb.Append(line.QuoteChar.Value);
            sb.Append(placeholder);
            sb.Append(line.QuoteChar.Value);
        }
        else
        {
            sb.Append(placeholder);
        }

        if (line.InlineComment is not null)
        {
            sb.Append(' ');
            sb.Append(line.InlineComment);
        }

        return sb.ToString();
    }

    private static string BuildPlaceholder(string key, PlaceholderStyle style)
    {
        if (style == PlaceholderStyle.ChangeMe) return "CHANGE_ME";

        var segments = key.ToUpperInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
        var matched = MatchKnownSuffix(segments);
        var chosen = matched ?? segments;

        var lower = new string[chosen.Length];
        for (var i = 0; i < chosen.Length; i++) lower[i] = chosen[i].ToLowerInvariant();
        var joined = string.Join('_', lower);

        return style switch
        {
            PlaceholderStyle.YourXHere => $"your_{joined}_here",
            PlaceholderStyle.UpperXHere => $"YOUR_{joined.ToUpperInvariant()}_HERE",
            PlaceholderStyle.AngleXHere => $"<your_{joined}_here>",
            _ => $"your_{joined}_here",
        };
    }

    private static string[]? MatchKnownSuffix(string[] segments)
    {
        foreach (var suffix in KnownSuffixes)
        {
            if (segments.Length < suffix.Length) continue;
            var offset = segments.Length - suffix.Length;
            var match = true;
            for (var i = 0; i < suffix.Length; i++)
            {
                if (!string.Equals(segments[offset + i], suffix[i], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }
            if (match) return suffix;
        }
        return null;
    }

    private static EnvLine ParseLine(int lineNumber, string raw)
    {
        var trimmedStart = raw.TrimStart();
        if (trimmedStart.Length == 0) return new EnvLine(lineNumber, EnvLineKind.Blank, raw);
        if (trimmedStart[0] == '#') return new EnvLine(lineNumber, EnvLineKind.Comment, raw);

        var span = raw.AsSpan();
        var pos = 0;
        while (pos < span.Length && (span[pos] == ' ' || span[pos] == '\t')) pos++;

        var hasExport = false;
        if (span.Slice(pos).StartsWith("export ", StringComparison.Ordinal))
        {
            hasExport = true;
            pos += "export ".Length;
        }

        var keyStart = pos;
        while (pos < span.Length && (char.IsLetterOrDigit(span[pos]) || span[pos] == '_')) pos++;
        if (pos == keyStart) return new EnvLine(lineNumber, EnvLineKind.Other, raw);

        var key = span.Slice(keyStart, pos - keyStart).ToString();
        if (key.Length == 0 || !(char.IsLetter(key[0]) || key[0] == '_'))
        {
            return new EnvLine(lineNumber, EnvLineKind.Other, raw);
        }

        while (pos < span.Length && (span[pos] == ' ' || span[pos] == '\t')) pos++;
        if (pos >= span.Length || span[pos] != '=') return new EnvLine(lineNumber, EnvLineKind.Other, raw);
        pos++; // consume '='

        while (pos < span.Length && (span[pos] == ' ' || span[pos] == '\t')) pos++;

        string? value = null;
        bool quoted = false;
        char? quoteChar = null;
        string? inlineComment = null;

        if (pos < span.Length && (span[pos] == '"' || span[pos] == '\''))
        {
            quoteChar = span[pos];
            quoted = true;
            pos++;
            var valueStart = pos;
            while (pos < span.Length && span[pos] != quoteChar.Value) pos++;
            value = span.Slice(valueStart, pos - valueStart).ToString();
            if (pos < span.Length) pos++; // consume closing quote
            var rest = span.Slice(pos).TrimStart();
            if (rest.Length > 0 && rest[0] == '#') inlineComment = rest.ToString();
        }
        else
        {
            var valueStart = pos;
            while (pos < span.Length)
            {
                if (span[pos] == '#')
                {
                    // inline comment: '#' preceded by whitespace
                    if (pos > valueStart && (span[pos - 1] == ' ' || span[pos - 1] == '\t'))
                    {
                        break;
                    }
                }
                pos++;
            }
            value = span.Slice(valueStart, pos - valueStart).TrimEnd().ToString();
            var rest = span.Slice(pos).TrimStart();
            if (rest.Length > 0 && rest[0] == '#') inlineComment = rest.ToString();
        }

        return new EnvLine(
            lineNumber,
            EnvLineKind.KeyValue,
            raw,
            Key: key,
            Value: value,
            HasExportPrefix: hasExport,
            ValueQuoted: quoted,
            QuoteChar: quoteChar,
            InlineComment: inlineComment);
    }
}
