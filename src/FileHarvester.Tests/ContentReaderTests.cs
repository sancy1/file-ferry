// ============================================================================
// FILE:     src/FileHarvester.Tests/ContentReaderTests.cs
// PURPOSE:  Unit tests for ContentReader. Verifies byte-fidelity, encoding
//           detection, binary detection, SHA-256 computation, and graceful
//           handling of unreadable files. Uses the shared TestHarness.
// RELATION: Consumes TestHarness (Infrastructure) and IContentReader.
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests;

/// <summary>
/// Unit tests for <see cref="IContentReader"/>.
/// </summary>
public sealed class ContentReaderTests : IClassFixture<TestHarness>
{
    private readonly TestHarness _harness;

    public ContentReaderTests(TestHarness harness)
    {
        _harness = harness;
    }

    /// <summary>
    /// Builds a ResolvedFile pointing at the given absolute path.
    /// Static because it does not read instance state.
    /// </summary>
    private static ResolvedFile Resolved(string absolutePath, int index = 0)
    {
        var fi = new FileInfo(absolutePath);

        return new ResolvedFile(
            Request:         new FileRequest(Index: index, RawInput: absolutePath),
            AbsolutePath:    absolutePath,
            SizeBytes:       fi.Exists ? fi.Length : 0,
            LastModifiedUtc: DateTime.UtcNow,
            Confidence:      ResolutionConfidence.Exact);
    }

    // ---------------------------------------------------------------------
    // TEST 1 — UTF-8 without BOM round-trips correctly
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Utf8WithoutBomRoundTrips()
    {
        var path = _harness.WriteFile("utf8.txt", "Hello, File-Ferry!");

        var content = await _harness.Reader.ReadAsync(Resolved(path));

        content.IsUnreadable.Should().BeFalse();
        content.IsBinary.Should().BeFalse();
        content.DecodedText.Should().Be("Hello, File-Ferry!");
        content.Encoding.Should().BeEquivalentTo(new UTF8Encoding(false));
        content.RawBytes.Should().NotBeEmpty();
    }

    // ---------------------------------------------------------------------
    // TEST 2 — UTF-8 with BOM: BOM appears in bytes but not in text
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Utf8WithBomIsDetected()
    {
        var path = Path.GetFullPath(Path.Combine(_harness.TempRoot, "utf8-bom.txt"));
        var original = "with bom";
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(true));

        var content = await _harness.Reader.ReadAsync(Resolved(path));

        content.IsUnreadable.Should().BeFalse();
        content.DecodedText.Should().Be(original);
        content.RawBytes.Length.Should().Be(3 + Encoding.UTF8.GetByteCount(original));
    }

    // ---------------------------------------------------------------------
    // TEST 3 — UTF-16 LE with BOM: detected and decoded correctly
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Utf16LeWithBomIsDetected()
    {
        var path = Path.GetFullPath(Path.Combine(_harness.TempRoot, "utf16.txt"));
        var original = "unicode ünïcodé ユニコード";
        await File.WriteAllTextAsync(path, original, Encoding.Unicode);

        var content = await _harness.Reader.ReadAsync(Resolved(path));

        content.IsUnreadable.Should().BeFalse();
        content.DecodedText.Should().Be(original);
    }

    // ---------------------------------------------------------------------
    // TEST 4 — CRLF line endings are preserved exactly
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CrlfLineEndingsArePreserved()
    {
        var path = Path.GetFullPath(Path.Combine(_harness.TempRoot, "crlf.txt"));
        var original = "line1\r\nline2\r\nline3";
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(false));

        var content = await _harness.Reader.ReadAsync(Resolved(path));

        content.DecodedText.Should().Be(original);
        content.DecodedText.Should().Contain("\r\n");
        content.DecodedText.Should().NotContain("\n\n");
    }

    // ---------------------------------------------------------------------
    // TEST 5 — Binary content is detected (null byte in first 8 KB)
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BinaryContentIsDetected()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x0D, 0x0A, 0x1A };
        var path = _harness.WriteBytes("binary.bin", bytes);

        var content = await _harness.Reader.ReadAsync(Resolved(path));

        content.IsUnreadable.Should().BeFalse();
        content.IsBinary.Should().BeTrue();
        content.DecodedText.Should().BeEmpty();
        content.RawBytes.Should().Equal(bytes);
    }

    // ---------------------------------------------------------------------
    // TEST 6 — Missing file returns an unreadable placeholder
    // ---------------------------------------------------------------------
    [Fact]
    public async Task MissingFileReturnsUnreadablePlaceholder()
    {
        var missing = Path.Combine(_harness.TempRoot, "nope.txt");

        var content = await _harness.Reader.ReadAsync(Resolved(missing));

        content.IsUnreadable.Should().BeTrue();
        content.ReadError.Should().NotBeNullOrWhiteSpace();
        content.RawBytes.Should().BeEmpty();
        content.DecodedText.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // TEST 7 — SHA-256 hash matches the reference computation
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Sha256MatchesReference()
    {
        var path = _harness.WriteFile("hash.txt", "hash me");

        var content = await _harness.Reader.ReadAsync(Resolved(path));

        var expected = Convert
            .ToHexString(SHA256.HashData(content.RawBytes))
            .ToLowerInvariant();

        content.Sha256.Should().Be(expected);
        content.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
