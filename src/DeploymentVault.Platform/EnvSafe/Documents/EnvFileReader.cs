// ============================================================================
// filepath: src/DeploymentVault.Platform/EnvSafe/Documents/EnvFileReader.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.EnvSafe.Documents
// since:    Phase A (EnvSafe)
// purpose:  Reads an imported source file (.env, .txt, .md, .docx) into
//           plain text for the EnvSafe converter.
//
// RELATIONSHIPS
//   Implements : IEnvFileReader (DeploymentVault.Core.EnvSafe.Abstractions)
//   Depends on : DocumentFormat.OpenXml (for .docx), System.IO
//   Used by    : DeploymentVaultServiceCollectionExtensions, EnvSafeViewModel
//   See also   : PromptImporter.ExtractDocxText (same OpenXml pattern)
//
// NOTES
//   - .env/.txt/.md are read as UTF-8 text.
//   - .docx is parsed via WordprocessingDocument; paragraphs are joined
//     with newlines (Body.InnerText does not preserve paragraph breaks).
//   - Unsupported extensions throw ArgumentException; the VM catches and
//     shows a user-facing message.
// ============================================================================

using System.Text;
using DeploymentVault.Core.EnvSafe.Abstractions;
using DocumentFormat.OpenXml.Packaging;

namespace DeploymentVault.Platform.EnvSafe.Documents;

/// <summary>
/// Reads an imported source file into plain text.
/// </summary>
public sealed class EnvFileReader : IEnvFileReader
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".txt", ".md",
    };

    /// <inheritdoc/>
    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path);

        if (TextExtensions.Contains(extension))
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDocxText(path);
        }

        throw new ArgumentException($"Unsupported file type: {extension}", nameof(path));
    }

    private static string ExtractDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var text = paragraph.InnerText;
            if (text.Length > 0) sb.AppendLine(text);
        }
        return sb.ToString();
    }
}
