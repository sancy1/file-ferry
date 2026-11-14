// filepath: src/FileHarvester.Tests/ImageTextify/SileroStreamingApiDiagnostic.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.B pre-work
// purpose:  Reflection-based diagnostic that prints the real public API
//           surface of the Whisper.net VAD types loaded at runtime, so
//           Phase 10.B's streaming segmenter can be written against the
//           measured shape instead of a remembered one (F-AV-094 rule).
//
// RELATIONSHIPS
//   Implements : n/a (diagnostic test)
//   Depends on : Whisper.net (WhisperVadFactory and the assembly it lives in)
//   Used by    : dotnet test --filter SileroStreamingApiDiagnostic
//   See also   : ImageTextify.Platform.Speech.WhisperNetSileroSegmenter,
//                ImageTextify/NAudioApiSurfaceDiagnostic (the pattern source)
//
// NOTES
//   - This test does not need a model file, ffmpeg, or a microphone. It
//     only needs the Whisper.net assembly to load, which referencing
//     typeof(WhisperVadFactory) guarantees.
//   - It asserts that the assembly loads and that at least one type
//     whose name contains "Vad" exists. It does not assert the content
//     of the surface; the surface is printed for human reading and
//     recorded in the session log.
//   - Fails loudly if the assembly or the VAD types are absent. This is
//     the opposite of the false-green early-return pattern (F-AV-035).
//   - Keep this file on disk after Phase 10.B ships. It documents the
//     measured API at the version Phase 10 was built against.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Whisper.net;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Prints the public API surface of the Whisper.net VAD types at the
/// version the test project references. Used to settle ITX-10.B-006
/// (does Whisper.net 1.9.1 expose a streaming VAD API?) with measured
/// evidence rather than recollection.
/// </summary>
public sealed class SileroStreamingApiDiagnostic
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Constructs the diagnostic with the xUnit output sink.
    /// </summary>
    /// <param name="output">The xUnit test output helper. Not null.</param>
    public SileroStreamingApiDiagnostic(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Reflects the Whisper.net assembly, finds every type whose name
    /// contains "Vad", and prints every public member of each.
    /// </summary>
    [Fact]
    public void DumpSileroVadApiSurface()
    {
        // WHAT: Obtain the assembly that owns WhisperVadFactory.
        // WHY:  typeof(...).Assembly is the assembly actually referenced
        //       by this test project; no probing, no path guessing.
        var asm = typeof(WhisperVadFactory).Assembly;

        _output.WriteLine("Assembly: " + asm.FullName);

        // WHAT: Enumerate every type whose name contains "Vad".
        // WHY:  The VAD surface may span WhisperVadFactory, a builder, a
        //       session/vad type, and per-segment data. Filtering by
        //       name finds them all without hard-coding a list I cannot
        //       prove is complete.
        var vadTypes = asm.GetTypes()
            .Where(t => t.Name.Contains("Vad", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        // Assert non-empty first so the loop cannot pass vacuously.
        vadTypes.Should().NotBeEmpty(
            "Whisper.net must expose at least one VAD type; zero means the assembly or the package changed shape.");

        foreach (var t in vadTypes)
        {
            _output.WriteLine("");
            _output.WriteLine("=== " + t.FullName + " (sealed=" + t.IsSealed + ", abstract=" + t.IsAbstract + ") ===");

            foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                                .OrderBy(c => c.GetParameters().Length))
            {
                _output.WriteLine("  ctor(" + JoinParams(ctor.GetParameters()) + ")");
            }

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                                .Where(m => !m.IsSpecialName)
                                .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var ret = m.ReturnType.Name;
                _output.WriteLine("  " + ret + " " + m.Name + "(" + JoinParams(m.GetParameters()) + ")");
            }

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                                .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                _output.WriteLine("  PROP " + p.PropertyType.Name + " " + p.Name);
            }

            foreach (var e in t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                .OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                _output.WriteLine("  EVENT " + e.EventHandlerType!.Name + " " + e.Name);
            }
        }
    }

    /// <summary>
    /// Formats a parameter list as "Type name, Type name".
    /// </summary>
    /// <param name="ps">The parameters to format. Not null.</param>
    /// <returns>A single-line rendering of the parameters.</returns>
    private static string JoinParams(ParameterInfo[] ps)
    {
        if (ps.Length == 0) { return string.Empty; }
        return string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name));
    }
}