// ============================================================================
// FILE:     src/FileHarvester.Core/Implementation/EncodingDetector.cs
// PURPOSE:  Detects the encoding of a byte array and decodes it to a string.
//           BOM sniffing runs FIRST so UTF-16 content (which contains null
//           bytes) is not misclassified as binary. Falls back to strict
//           UTF-8, a UTF-16 heuristic, then system ANSI.
// RELATION: Used by ContentReader (Phase 1.2) to produce the DecodedText
//           and Encoding fields on FileContent. Pure logic: no I/O, no
//           resilience, no dependencies.
// ============================================================================

using System.Text;

namespace FileHarvester.Core.Implementation;

/// <summary>
/// Stateless encoding detection and decoding for file content.
/// </summary>
internal static class EncodingDetector
{
    /// <summary>
    /// How many leading bytes to inspect when looking for binary content.
    /// </summary>
    private const int BinaryProbeBytes = 8 * 1024;

    /// <summary>
    /// The Unicode BOM character. Stripped from decoded text if present.
    /// </summary>
    private const char BomChar = '\uFEFF';

    /// <summary>
    /// The result of decoding a byte array.
    /// </summary>
    /// <param name="Text">The decoded text; empty when binary.</param>
    /// <param name="Encoding">The encoding used to decode.</param>
    /// <param name="IsBinary">True when the content appears binary.</param>
    internal readonly record struct Decoded(string Text, Encoding Encoding, bool IsBinary);

    /// <summary>
    /// Decodes a byte array using the best-guess encoding.
    /// </summary>
    internal static Decoded Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // ---- 1. BOM sniffing runs FIRST --------------------------------
        // UTF-16 encoded text contains null bytes for every ASCII character.
        // If we checked for binary first, every UTF-16 file would be
        // misclassified. BOM detection must precede binary detection.
        if (TryDetectByBom(bytes, out var bomEncoding, out var bomLength))
        {
            var bomText = bomEncoding.GetString(bytes, bomLength, bytes.Length - bomLength);
            return new Decoded(
                Text: StripBomChar(bomText),
                Encoding: bomEncoding,
                IsBinary: false);
        }

        // ---- 2. Binary detection (only when no BOM was found) ----------
        if (IsBinary(bytes))
        {
            return new Decoded(
                Text: string.Empty,
                Encoding: Encoding.UTF8,
                IsBinary: true);
        }

        // ---- 3. Strict UTF-8 validation --------------------------------
        if (IsValidUtf8(bytes))
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return new Decoded(
                Text: StripBomChar(utf8.GetString(bytes)),
                Encoding: utf8,
                IsBinary: false);
        }

        // ---- 4. UTF-16 heuristic (no BOM, but bytes look UTF-16) -------
        var heuristic = GuessUtf16(bytes);
        if (heuristic is not null)
        {
            return new Decoded(
                Text: StripBomChar(heuristic.GetString(bytes)),
                Encoding: heuristic,
                IsBinary: false);
        }

        // ---- 5. System ANSI fallback -----------------------------------
        return new Decoded(
            Text: Encoding.Latin1.GetString(bytes),
            Encoding: Encoding.Latin1,
            IsBinary: false);
    }

    // -----------------------------------------------------------------
    // BOM detection.
    // -----------------------------------------------------------------

    private static bool TryDetectByBom(
        byte[] bytes,
        out Encoding encoding,
        out int bomLength)
    {
        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            bomLength = 3;
            return true;
        }

        // UTF-16 LE BOM: FF FE
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            bomLength = 2;
            return true;
        }

        // UTF-16 BE BOM: FE FF
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            bomLength = 2;
            return true;
        }

        encoding = Encoding.UTF8;
        bomLength = 0;
        return false;
    }

    // -----------------------------------------------------------------
    // Binary check.
    // -----------------------------------------------------------------

    private static bool IsBinary(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, BinaryProbeBytes);

        for (var i = 0; i < limit; i++)
        {
            if (bytes[i] == 0x00)
            {
                return true;
            }
        }

        return false;
    }

    // -----------------------------------------------------------------
    // Strict UTF-8 validation.
    // -----------------------------------------------------------------

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            var strict = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

            _ = strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    // -----------------------------------------------------------------
    // UTF-16 heuristic for BOM-less UTF-16.
    // -----------------------------------------------------------------

    private static Encoding? GuessUtf16(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return null;
        }

        var sampleCount = Math.Min(bytes.Length, 512);
        var oddZeros = 0;
        var evenZeros = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            if (bytes[i] == 0x00)
            {
                if (i % 2 == 0) evenZeros++;
                else            oddZeros++;
            }
        }

        var half = sampleCount / 2;

        if (oddZeros > half)  return Encoding.Unicode;          // LE
        if (evenZeros > half) return Encoding.BigEndianUnicode; // BE

        return null;
    }

    // -----------------------------------------------------------------
    // BOM character stripping (defense in depth).
    // -----------------------------------------------------------------

    private static string StripBomChar(string text)
    {
        if (text.Length > 0 && text[0] == BomChar)
        {
            return text[1..];
        }

        return text;
    }
}
