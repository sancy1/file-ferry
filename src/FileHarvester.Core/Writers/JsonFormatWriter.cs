// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/JsonFormatWriter.cs
// PURPOSE:  JSON output. Clean by default: an array of {path, content}
//           objects, or just the content for a single file. The full
//           envelope with metadata appears only when IncludeHeaders is true.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

public sealed class JsonFormatWriter : IFormatWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Extension => ".json";
    public string DisplayName => "JSON";
    public string MimeType => "application/json";
    public bool SupportsStreaming => true;

    public async Task WriteAsync(
        ComposedDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        if (document.Mode != OutputMode.SingleMerged)
        {
            throw new NotSupportedException(
                "JsonFormatWriter only renders SingleMerged documents.");
        }

        string json;

        if (document.IncludeHeaders)
        {
            // Full envelope with metadata.
            var payload = new BundlePayload
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Mode = document.Mode.ToString(),
                FileCount = document.Files.Count,
                TotalBytes = document.Files.Sum(f => (long)f.RawBytes.Length),
                Files = document.Files.Select(ToEntry).ToList(),
            };
            json = JsonSerializer.Serialize(payload, Options);
        }
        else if (document.Files.Count == 1)
        {
            // Single file, no header: just the content as a JSON string.
            var fc = document.Files[0];
            json = fc.IsUnreadable
                ? JsonSerializer.Serialize($"[unreadable: {fc.ReadError ?? "unknown"}]", Options)
                : JsonSerializer.Serialize(fc.DecodedText, Options);
        }
        else
        {
            // Multiple files, no header: an array of {path, content}.
            var items = document.Files.Select(fc => new
            {
                path = fc.File.AbsolutePath,
                content = fc.IsUnreadable
                    ? $"[unreadable: {fc.ReadError ?? "unknown"}]"
                    : (fc.IsBinary ? $"[binary: {fc.RawBytes.Length} bytes]" : fc.DecodedText),
            }).ToList();

            json = JsonSerializer.Serialize(items, Options);
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(json);

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static FileEntry ToEntry(FileContent fc)
    {
        var entry = new FileEntry
        {
            Path = fc.File.AbsolutePath,
            SizeBytes = fc.RawBytes.Length,
            Sha256 = fc.Sha256,
            Encoding = fc.Encoding.WebName,
            IsBinary = fc.IsBinary,
            IsUnreadable = fc.IsUnreadable,
            ReadError = fc.ReadError,
        };

        if (!fc.IsUnreadable && !fc.IsBinary)
        {
            entry.Content = fc.DecodedText;
        }

        return entry;
    }

    private sealed class BundlePayload
    {
        [JsonPropertyName("generatedAtUtc")] public DateTime GeneratedAtUtc { get; set; }
        [JsonPropertyName("mode")] public string Mode { get; set; } = string.Empty;
        [JsonPropertyName("fileCount")] public int FileCount { get; set; }
        [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
        [JsonPropertyName("files")] public List<FileEntry> Files { get; set; } = new();
    }

    private sealed class FileEntry
    {
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonPropertyName("encoding")] public string Encoding { get; set; } = string.Empty;
        [JsonPropertyName("isBinary")] public bool IsBinary { get; set; }
        [JsonPropertyName("isUnreadable")] public bool IsUnreadable { get; set; }
        [JsonPropertyName("readError")] public string? ReadError { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
