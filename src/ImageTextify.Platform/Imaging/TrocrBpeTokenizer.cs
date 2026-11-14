// filepath: src/ImageTextify.Platform/Imaging/TrocrBpeTokenizer.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  Byte-Pair Encoding (BPE) tokenizer for TrOCR. Loads the
//           Hugging Face tokenizer.json (RoBERTa BPE) and decodes the
//           token IDs TrOCR's ONNX decoder produces into text.
//
// RELATIONSHIPS
//   Implements : n/a (helper)
//   Depends on : System.Text.Json
//   Used by    : ImageTextify.Platform.OnnxModelRunner.RunTrocrRecognitionAsync
//   See also   : OnnxModelRunner.cs
//
// NOTES
//   - TrOCR is a VisionEncoderDecoder; the "encoder" is a ViT (image
//     patches), not a text tokenizer. The BPE is used only to decode
//     token IDs from the ONNX decoder back to text. No encode direction
//     is needed at runtime.
//   - TrOCR uses RoBERTa-style BPE: byte-level BPE with 'G' (U+0120)
//     representing space and 'C' (U+010A) representing newline in the
//     vocabulary.
//   - Special tokens (<s>, </s>, <pad>, <unk>) are in the vocabulary
//     and are skipped during decode.
//   - Not thread-safe. Load is expected once at construction. Decode is
//     stateless after load.
//   - The tokenizer.json format is Hugging Face Transformers 4.x. The
//     reader below targets the fields TrOCR produces; it tolerates
//     missing optional fields.

using System.Text;
using System.Text.Json;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// BPE tokenizer for TrOCR. Decodes token IDs to text.
/// </summary>
public sealed class TrocrBpeTokenizer
{
    private readonly Dictionary<int, string> _idToToken;
    private readonly HashSet<int> _specialIds;

    private TrocrBpeTokenizer(Dictionary<int, string> idToToken, HashSet<int> specialIds)
    {
        _idToToken = idToToken;
        _specialIds = specialIds;
    }

    /// <summary>
    /// Loads a tokenizer from a Hugging Face tokenizer.json file.
    /// </summary>
    /// <param name="path">Path to tokenizer.json.</param>
    /// <returns>A loaded tokenizer ready to decode.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidOperationException">The file is not a valid Hugging Face tokenizer.json.</exception>
    public static TrocrBpeTokenizer Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Tokenizer file not found: {path}", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // Extract id->token map from model.vocab
        var idToToken = new Dictionary<int, string>();
        if (root.TryGetProperty("model", out var model)
            && model.TryGetProperty("vocab", out var vocab)
            && vocab.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in vocab.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    var id = prop.Value.GetInt32();
                    idToToken[id] = prop.Name;
                }
            }
        }

        if (idToToken.Count == 0)
            throw new InvalidOperationException($"No vocab entries found in {path}.");

        // Extract special token IDs from added_tokens (if present)
        var specialIds = new HashSet<int>();
        if (root.TryGetProperty("added_tokens", out var addedTokens)
            && addedTokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var at in addedTokens.EnumerateArray())
            {
                if (at.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.Number)
                {
                    specialIds.Add(idProp.GetInt32());
                }
            }
        }

        // Also mark common special tokens by name if present in vocab
        foreach (var kv in idToToken)
        {
            if (kv.Value is "<s>" or "</s>" or "<pad>" or "<unk>"
                or "<mask>" or "<sep>" or "<cls>")
            {
                specialIds.Add(kv.Key);
            }
        }

        return new TrocrBpeTokenizer(idToToken, specialIds);
    }

    /// <summary>
    /// Decodes a sequence of TrOCR decoder token IDs into text.
    /// </summary>
    /// <param name="tokenIds">The token IDs, in order.</param>
    /// <returns>The decoded text. Empty when all tokens are special.</returns>
    /// <remarks>
    /// RoBERTa-style BPE decoding:
    /// <list type="bullet">
    ///   <item>Special tokens (<s>, </s>, etc.) are skipped.</item>
    ///   <item>'G' (U+0120) in a token represents a space.</item>
    ///   <item>'C' (U+010A) in a token represents a newline.</item>
    ///   <item>Other tokens are appended as-is.</item>
    /// </list>
    /// The result is space-normalized (runs of spaces collapsed).
    /// </remarks>
    public string Decode(IEnumerable<int> tokenIds)
    {
        var sb = new StringBuilder();
        foreach (var id in tokenIds)
        {
            if (_specialIds.Contains(id)) continue;
            if (!_idToToken.TryGetValue(id, out var token)) continue;

            foreach (var ch in token)
            {
                if (ch == '\u0120') sb.Append(' ');
                else if (ch == '\u010A') sb.Append('\n');
                else sb.Append(ch);
            }
        }
        var text = sb.ToString();
        // Collapse whitespace runs to single spaces, trim.
        var trimmed = text.Replace("\r\n", " ").Replace("\n", " ");
        while (trimmed.Contains("  ")) trimmed = trimmed.Replace("  ", " ");
        return trimmed.Trim();
    }
}