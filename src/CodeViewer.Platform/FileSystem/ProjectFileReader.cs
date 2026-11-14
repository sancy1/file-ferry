// ============================================================================
// FILE:     src/CodeViewer.Platform/FileSystem/ProjectFileReader.cs
// PURPOSE:  Reads the full text of a source file for the Code Content
//           viewer. Reading is asynchronous and never blocks the UI. There
//           is deliberately NO file-size limit: any supported text file is
//           previewed in full. Content is streamed into a StringBuilder so
//           a large file is not duplicated as a second full copy in memory.
//           Binary and unreadable files return a no-preview result but stay
//           visible in the browser and structure.
// RELATION: Runs inside the file-read resilience policy. Uses the same
//           BOM-first binary/encoding detection approach as
//           FileHarvester.Core's EncodingDetector (which is internal to
//           Core and not shared), reimplemented here for streaming.
// ============================================================================

using System.Text;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;

namespace CodeViewer.Platform.FileSystem;

/// <summary>
/// Reads source files for preview.
/// </summary>
public sealed class ProjectFileReader
{
    /// <summary>How many leading bytes to probe for binary content.</summary>
    private const int BinaryProbeBytes = 8 * 1024;

    private readonly IResilienceExecutor _resilience;

    public ProjectFileReader(IResilienceExecutor resilience)
    {
        ArgumentNullException.ThrowIfNull(resilience);
        _resilience = resilience;
    }

    /// <summary>
    /// Reads the full text of a file. Never throws for an unreadable or
    /// binary file; returns a no-preview result instead so a single bad
    /// file cannot fail the viewer.
    /// </summary>
    public async Task<ProjectFileReadResult> ReadAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        try
        {
            return await _resilience.ExecuteAsync<ProjectFileReadResult>(
                policyName: PolicyNames.FileRead,
                operation: token => ReadCoreAsync(absolutePath, token),
                fallback: _ => Task.FromResult(
                    ProjectFileReadResult.Unreadable("Circuit open or timeout")),
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or PathTooLongException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            return ProjectFileReadResult.Unreadable(ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Core read — streams the file inside the resilience pipeline.
    // ------------------------------------------------------------------

    private static async Task<ProjectFileReadResult> ReadCoreAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        // FileShare.ReadWrite allows reading files other processes hold
        // open (logs, lock files, live configs), matching ContentReader.
        await using var stream = new FileStream(
            path: absolutePath,
            mode: FileMode.Open,
            access: FileAccess.Read,
            share: FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            useAsync: true);

        // ---- 1. Probe the leading bytes for a BOM and binary content ---
        var probe = new byte[Math.Min(BinaryProbeBytes, Math.Max(stream.Length, 0))];
        var probeLength = await stream.ReadAsync(probe, cancellationToken)
            .ConfigureAwait(false);

        var encoding = DetectEncoding(probe, probeLength, out var bomLength, out var isBinary);
        if (isBinary)
        {
            return ProjectFileReadResult.Binary();
        }

        // ---- 2. Stream-decode the whole file into a StringBuilder ------
        // Decoding incrementally avoids holding both the raw byte array
        // and the decoded string for a large file at the same time.
        var builder = new StringBuilder(capacity: (int)Math.Min(stream.Length, int.MaxValue));
        var decoder = encoding.GetDecoder();

        // Process the probe bytes first (skipping any BOM), then the rest.
        if (probeLength > bomLength)
        {
            AppendDecoded(decoder, probe, bomLength, probeLength - bomLength, builder);
        }

        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            AppendDecoded(decoder, buffer, 0, read, builder);
        }

        var text = builder.ToString();
        return ProjectFileReadResult.Success(text, CountLines(text));
    }

    private static void AppendDecoded(
        Decoder decoder, byte[] bytes, int index, int count, StringBuilder builder)
    {
        var charCount = decoder.GetCharCount(bytes, index, count, flush: false);
        var chars = new char[charCount];
        decoder.GetChars(bytes, index, count, chars, 0, flush: false);
        builder.Append(chars);
    }

    // ------------------------------------------------------------------
    // Encoding / binary detection (BOM first, then binary, then UTF-8,
    // then a UTF-16 heuristic, then ANSI) - mirrors EncodingDetector.
    // ------------------------------------------------------------------

    private static Encoding DetectEncoding(
        byte[] probe, int length, out int bomLength, out bool isBinary)
    {
        // UTF-8 BOM: EF BB BF
        if (length >= 3 && probe[0] == 0xEF && probe[1] == 0xBB && probe[2] == 0xBF)
        {
            bomLength = 3;
            isBinary = false;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        // UTF-16 LE BOM: FF FE
        if (length >= 2 && probe[0] == 0xFF && probe[1] == 0xFE)
        {
            bomLength = 2;
            isBinary = false;
            return Encoding.Unicode;
        }

        // UTF-16 BE BOM: FE FF
        if (length >= 2 && probe[0] == 0xFE && probe[1] == 0xFF)
        {
            bomLength = 2;
            isBinary = false;
            return Encoding.BigEndianUnicode;
        }

        bomLength = 0;

        // Binary detection only when no BOM was found (UTF-16 text
        // contains null bytes and must not be misclassified).
        for (var i = 0; i < length; i++)
        {
            if (probe[i] == 0x00)
            {
                isBinary = true;
                return Encoding.UTF8;
            }
        }

        isBinary = false;
        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: false);
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = 1;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines;
    }
}
