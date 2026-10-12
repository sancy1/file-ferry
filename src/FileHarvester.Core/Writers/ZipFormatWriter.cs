// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/ZipFormatWriter.cs
// PURPOSE:  ZIP archive output. Clean by default: just the source files,
//           byte-for-byte. manifest.json and bundle.md appear only when the
//           corresponding flags are set.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;

namespace FileHarvester.Core.Writers;

public sealed class ZipFormatWriter : IFormatWriter
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Extension => ".zip";
    public string DisplayName => "ZIP Archive";
    public string MimeType => "application/zip";
    public bool SupportsStreaming => false;

    public Task WriteAsync(
        ComposedDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        if (document.Mode != OutputMode.SingleMerged)
        {
            throw new NotSupportedException(
                "ZipFormatWriter only renders SingleMerged documents.");
        }

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var manifestEntries = new List<ManifestEntry>();
        var index = 1;

        foreach (var fc in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fc.IsUnreadable)
            {
                manifestEntries.Add(new ManifestEntry
                {
                    Index = index,
                    OriginalPath = fc.File.AbsolutePath,
                    ArchiveName = null,
                    SizeBytes = 0,
                    Sha256 = string.Empty,
                    Encoding = fc.Encoding.WebName,
                    IsBinary = fc.IsBinary,
                    IsUnreadable = true,
                    ReadError = fc.ReadError,
                });
                index++;
                continue;
            }

            var archiveName = BuildArchiveName(index, fc.File.AbsolutePath);
            WriteBytesEntry(archive, archiveName, fc.RawBytes);

            manifestEntries.Add(new ManifestEntry
            {
                Index = index,
                OriginalPath = fc.File.AbsolutePath,
                ArchiveName = archiveName,
                SizeBytes = fc.RawBytes.Length,
                Sha256 = fc.Sha256,
                Encoding = fc.Encoding.WebName,
                IsBinary = fc.IsBinary,
                IsUnreadable = false,
                ReadError = null,
            });

            index++;
        }

        // Optional manifest.json.
        if (document.IncludeManifest)
        {
            var manifest = new Manifest
            {
                GeneratedAtUtc = DateTime.UtcNow,
                FileCount = document.Files.Count,
                TotalBytes = document.Files.Sum(f => (long)f.RawBytes.Length),
                Entries = manifestEntries,
            };

            var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            WriteTextEntry(archive, "manifest.json", manifestJson);
        }

        // Optional bundle.md (only if headers are on — user is opting in to
        // a human-readable merged view).
        if (document.IncludeHeaders)
        {
            var bundleMd = BundleComposer.RenderMergedText(document);
            WriteTextEntry(archive, "bundle.md", bundleMd);
        }

        return Task.CompletedTask;
    }

    private static void WriteBytesEntry(ZipArchive archive, string entryName, byte[] content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(text);
        WriteBytesEntry(archive, entryName, bytes);
    }

    private static string BuildArchiveName(int index, string originalPath)
    {
        var fileName = CloudPath.GetFileName(originalPath);

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "file";
        }

        return $"{index:D3}-{fileName}";
    }

    private sealed class Manifest
    {
        [JsonPropertyName("generatedAtUtc")] public DateTime GeneratedAtUtc { get; set; }
        [JsonPropertyName("fileCount")] public int FileCount { get; set; }
        [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
        [JsonPropertyName("entries")] public List<ManifestEntry> Entries { get; set; } = new();
    }

    private sealed class ManifestEntry
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("originalPath")] public string OriginalPath { get; set; } = string.Empty;
        [JsonPropertyName("archiveName")] public string? ArchiveName { get; set; }
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonPropertyName("encoding")] public string Encoding { get; set; } = string.Empty;
        [JsonPropertyName("isBinary")] public bool IsBinary { get; set; }
        [JsonPropertyName("isUnreadable")] public bool IsUnreadable { get; set; }
        [JsonPropertyName("readError")] public string? ReadError { get; set; }
    }
}
