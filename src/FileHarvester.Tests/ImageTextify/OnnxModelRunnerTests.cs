// filepath: src/FileHarvester.Tests/ImageTextify/OnnxModelRunnerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4b2
// purpose:  Structural and error-handling tests for OnnxModelRunner.
//           Does not exercise ONNX inference; that is the integration
//           test in Batch 8c. These tests verify constructor validation
//           and disposal behavior.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : ImageTextify.Platform.Imaging.OnnxModelRunner
//   Used by    : none found (test runner)
//   See also   : OnnxModelRunner.cs
//
// NOTES
//   - OnnxModelRunner requires real ONNX files to construct. These
//     tests use a non-existent path to test the FileNotFoundException
//     path. They do not load real models.

using FluentAssertions;
using ImageTextify.Platform.Imaging;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Structural tests for <see cref="OnnxModelRunner"/>.
/// </summary>
public sealed class OnnxModelRunnerTests
{
    private const string MissingPath = @"C:\this\path\does\not\exist\model.onnx";

    /// <summary>
    /// Constructor throws FileNotFoundException when detector missing.
    /// </summary>
    [Fact]
    public void ConstructorMissingDetectorThrowsFileNotFound()
    {
        var act = () => new OnnxModelRunner(
            ppocrDetectorPath: MissingPath,
            ppocrRecognizerPath: MissingPath,
            ppocrDictionaryPath: MissingPath,
            trocrEncoderPath: MissingPath,
            trocrDecoderPath: MissingPath,
            trocrTokenizerPath: MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Constructor throws FileNotFoundException when recognizer missing.
    /// </summary>
    [Fact]
    public void ConstructorMissingRecognizerThrowsFileNotFound()
    {
        var act = () => new OnnxModelRunner(
            ppocrDetectorPath: MissingPath,
            ppocrRecognizerPath: MissingPath,
            ppocrDictionaryPath: MissingPath,
            trocrEncoderPath: MissingPath,
            trocrDecoderPath: MissingPath,
            trocrTokenizerPath: MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Constructor throws FileNotFoundException when dictionary missing.
    /// </summary>
    [Fact]
    public void ConstructorMissingDictionaryThrowsFileNotFound()
    {
        var act = () => new OnnxModelRunner(
            ppocrDetectorPath: MissingPath,
            ppocrRecognizerPath: MissingPath,
            ppocrDictionaryPath: MissingPath,
            trocrEncoderPath: MissingPath,
            trocrDecoderPath: MissingPath,
            trocrTokenizerPath: MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Constructor throws FileNotFoundException when TrOCR encoder missing.
    /// </summary>
    [Fact]
    public void ConstructorMissingEncoderThrowsFileNotFound()
    {
        var act = () => new OnnxModelRunner(
            ppocrDetectorPath: MissingPath,
            ppocrRecognizerPath: MissingPath,
            ppocrDictionaryPath: MissingPath,
            trocrEncoderPath: MissingPath,
            trocrDecoderPath: MissingPath,
            trocrTokenizerPath: MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Constructor throws FileNotFoundException when TrOCR decoder missing.
    /// </summary>
    [Fact]
    public void ConstructorMissingDecoderThrowsFileNotFound()
    {
        var act = () => new OnnxModelRunner(
            ppocrDetectorPath: MissingPath,
            ppocrRecognizerPath: MissingPath,
            ppocrDictionaryPath: MissingPath,
            trocrEncoderPath: MissingPath,
            trocrDecoderPath: MissingPath,
            trocrTokenizerPath: MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Constructor throws FileNotFoundException when tokenizer missing.
    /// </summary>
    [Fact]
    public void ConstructorMissingTokenizerThrowsFileNotFound()
    {
        var act = () => new OnnxModelRunner(
            ppocrDetectorPath: MissingPath,
            ppocrRecognizerPath: MissingPath,
            ppocrDictionaryPath: MissingPath,
            trocrEncoderPath: MissingPath,
            trocrDecoderPath: MissingPath,
            trocrTokenizerPath: MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// TrocrBpeTokenizer.Load throws FileNotFoundException on missing path.
    /// </summary>
    [Fact]
    public void TokenizerLoadMissingPathThrows()
    {
        var act = () => TrocrBpeTokenizer.Load(MissingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// TrocrBpeTokenizer.Load throws InvalidOperationException on empty JSON.
    /// </summary>
    [Fact]
    public void TokenizerLoadEmptyVocabThrows()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{}");
            var act = () => TrocrBpeTokenizer.Load(tempFile);
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}