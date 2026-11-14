// filepath: bench/gate/Program.cs
// purpose:  C7 Part A — D5.4 CER gate. Runs form_1/form_2/form_3 through
//           the 7-arg engine with the Qwen printed fallback enabled,
//           computes CER against the label full_text, prints gate result.
//           Also runs form_1 with the fallback disabled (regression guard).

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Settings;
using ImageTextify.Platform.Imaging;

namespace D54Gate;

internal static class Program
{
    private const string RepoRoot = @"C:\Users\HP\Desktop\file-ferry";
    private const string ImagesDir = @"C:\Users\HP\Desktop\exracts\Images\sharp";
    private static readonly string LabelsDir = Path.Combine(RepoRoot, "bench", "eval-set", "labels");

    private static readonly string Det   = Path.Combine(RepoRoot, "bench", "models-onnx", "ppocrv6-medium", "det", "PP-OCRv6_medium_det_onnx", "inference.onnx");
    private static readonly string Rec   = Path.Combine(RepoRoot, "bench", "models-onnx", "ppocrv6-medium", "rec", "PP-OCRv6_medium_rec_0515_onnx", "inference.onnx");
    private static readonly string Dict  = Path.Combine(RepoRoot, "bench", "models-onnx", "ppocrv6_medium", "ppocr_dict.txt");
    private static readonly string TEnc  = Path.Combine(RepoRoot, "bench", "models-onnx", "trocr-base-handwritten", "encoder_model.onnx");
    private static readonly string TDec  = Path.Combine(RepoRoot, "bench", "models-onnx", "trocr-base-handwritten", "decoder_model.onnx");
    private static readonly string TTok  = Path.Combine(RepoRoot, "bench", "models-onnx", "trocr-base-handwritten", "tokenizer.json");
    private static readonly string QText = Path.Combine(RepoRoot, "bench", "models-qwen", "Qwen2.5-VL-3B-Instruct-Q4_K_M.gguf");
    private static readonly string QMmproj = Path.Combine(RepoRoot, "bench", "models-qwen", "Qwen2.5-VL-3B-Instruct-mmproj-f16.gguf");

    public static async Task<int> Main()
    {
        Console.WriteLine("=== D5.4 Gate Harness ===");
        Console.WriteLine();
        var inputs = new[] { Det, Rec, Dict, TEnc, TDec, TTok, QText, QMmproj };
        foreach (var p in inputs)
        {
            if (!File.Exists(p)) { Console.WriteLine("MISSING INPUT: " + p); return 1; }
        }

        // Load Qwen once. ~30-60s on CPU.
        Console.WriteLine("Loading Qwen model (this takes 30-60 seconds)...");
        await using var qwen = new QwenVisionService(QText, QMmproj);
        var printedFallback = new QwenPrintedFallbackRecognizer(qwen);
        Console.WriteLine("Qwen model loaded.");
        Console.WriteLine();

        // ----- Run 1: form_1 with fallback OFF (regression guard) -----
        var offStore = new InMemoryImageTextifySettingsStore(
            new ImageTextifySettings(false, 0.3));
        var form1CerOff = await RunOne("form_document_1", "Images_sharp_form_document_1_png.json", offStore, printedFallback, qwen);
        Console.WriteLine();

        // ----- Runs 2-4: form_1, form_2, form_3 with fallback ON -----
        var onStore = new InMemoryImageTextifySettingsStore(
            new ImageTextifySettings(true, 0.3));
        var form1CerOn = await RunOne("form_document_1", "Images_sharp_form_document_1_png.json", onStore, printedFallback, qwen);
        Console.WriteLine();
        var form2Cer = await RunOne("form_document_2", "Images_sharp_form_document_2_png.json", onStore, printedFallback, qwen);
        Console.WriteLine();
        var form3Cer = await RunOne("form_document_3", "Images_sharp_form_document_3_png.json", onStore, printedFallback, qwen);
        Console.WriteLine();

        // ----- Verdict -----
        Console.WriteLine("=== VERDICT ===");
        Console.WriteLine("form_1 OFF: " + form1CerOff.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "  (regression guard: expect 0.0393)");
        Console.WriteLine("form_1 ON:  " + form1CerOn.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "  (must not regress beyond 0.05)");
        Console.WriteLine("form_2 ON:  " + form2Cer.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "  (gate: < 0.10)");
        Console.WriteLine("form_3 ON:  " + form3Cer.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "  (gate: < 0.20)");
        Console.WriteLine();
        var gatePass = form2Cer < 0.10 && form3Cer < 0.20;
        var regressionPass = form1CerOff < 0.05;
        if (gatePass && regressionPass) { Console.WriteLine("C7 GATE: PASS"); }
        else { Console.WriteLine("C7 GATE: FAIL"); }
        return (gatePass && regressionPass) ? 0 : 2;
    }

    private static async Task<double> RunOne(
        string imageStem, string labelFile, IImageTextifySettingsStore store,
        QwenPrintedFallbackRecognizer printedFallback, IQwenVisionService qwen)
    {
        var imagePath = Path.Combine(ImagesDir, imageStem + ".png");
        var labelPath = Path.Combine(LabelsDir, labelFile);
        if (!File.Exists(imagePath)) { throw new FileNotFoundException(imagePath); }
        if (!File.Exists(labelPath)) { throw new FileNotFoundException(labelPath); }

        var fullText = ReadFullText(labelPath);
        await using var runner = new OnnxModelRunner(Det, Rec, Dict, TEnc, TDec, TTok);
        var preprocessor = new ImagePreprocessor();
        var router = new OcrLineRouter();
        var merger = new OcrResultMerger();
        await using var engine = new OnnxOcrEngine(
            runner, preprocessor, router, merger, qwen, printedFallback, store);

        await using var stream = File.OpenRead(imagePath);
        var options = new ImageOcrOptions { Enhance = true };
        var result = await engine.ExtractAsync(stream, options, CancellationToken.None);

        var predicted = (result.Text ?? string.Empty).Replace("\r\n", "\n");
        var reference = (fullText ?? string.Empty).Replace("\r\n", "\n");
        var cer = ComputeCer(predicted, reference);
        Console.WriteLine("  " + imageStem + ": CER=" + cer.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + " (ref_len=" + reference.Length + ", pred_len=" + predicted.Length + ")");
        return cer;
    }

    private static string ReadFullText(string labelPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(labelPath));
        if (doc.RootElement.TryGetProperty("full_text", out var ft))
        {
            return ft.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static double ComputeCer(string predicted, string reference)
    {
        if (reference.Length == 0) return predicted.Length == 0 ? 0.0 : 1.0;
        var dist = Levenshtein(predicted, reference);
        return (double)dist / reference.Length;
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length; var m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;
        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var min = prev[j] + 1;
                var ins = curr[j - 1] + 1;
                if (ins < min) min = ins;
                var sub = prev[j - 1] + cost;
                if (sub < min) min = sub;
                curr[j] = min;
            }
            var tmp = prev; prev = curr; curr = tmp;
        }
        return prev[m];
    }
}

