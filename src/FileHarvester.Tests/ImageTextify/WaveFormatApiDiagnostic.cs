// filepath: src/FileHarvester.Tests/ImageTextify/WaveFormatApiDiagnostic.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.B pre-work
// purpose:  Reflection-based diagnostic that prints the real public
//           constructor and static-factory surface of NAudio.Wave.WaveFormat
//           at the version the test project references, so the capture
//           adapter's WithFormat call can be written against the measured
//           shape instead of a remembered one (F-AV-094 rule).
//
// RELATIONSHIPS
//   Implements : n/a (diagnostic test)
//   Depends on : NAudio.Core or NAudio.Wave (whichever assembly owns WaveFormat)
//   Used by    : dotnet test --filter WaveFormatApiDiagnostic
//   See also   : src/ImageTextify.Platform/Audio/NAudioAudioCaptureService.cs,
//                NAudioApiSurfaceDiagnostic (sibling diagnostic over Wasapi types)
//
// NOTES
//   - The sibling diagnostic NAudioApiSurfaceDiagnostic searches only the
//     assembly that owns MMDeviceEnumerator (NAudio.Wasapi). WaveFormat
//     lives in a different NAudio assembly, so typeof(WaveFormat).Assembly
//     is used here. This was established by observation at 21:49 this
//     session, when WaveFormat printed as NOT FOUND from the Wasapi dump.
//   - Does not need a microphone or a model file. Only needs the NAudio
//     WaveFormat type to resolve, which requires only the referenced
//     NAudio package already present in this test project.
//   - Fails loudly if zero constructors and zero static factories are
//     found. No early-return (F-AV-035 pattern avoidance).

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NAudio.Wave;
using Xunit;
using Xunit.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Prints the public constructor and static-factory surface of
/// <see cref="WaveFormat"/>. Used to settle the exact form of the
/// WithFormat argument for NAudioAudioCaptureService with measured
/// evidence rather than recollection.
/// </summary>
public sealed class WaveFormatApiDiagnostic
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Constructs the diagnostic with the xUnit output sink.
    /// </summary>
    /// <param name="output">The xUnit test output helper. Not null.</param>
    public WaveFormatApiDiagnostic(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Reflects over the assembly that owns WaveFormat and prints every
    /// public constructor and every public Create* or As* static method.
    /// </summary>
    [Fact]
    public void DumpWaveFormatSurface()
    {
        // WHAT: Obtain the assembly that owns WaveFormat directly.
        // WHY:  The sibling diagnostic searched MMDeviceEnumerator's assembly
        //       (NAudio.Wasapi) and WaveFormat resolved to null there. WaveFormat
        //       lives in a different NAudio assembly. typeof(WaveFormat).Assembly
        //       is the assembly that actually defines the type this test needs.
        // NOT:  Do not search Assembly.GetEntryAssembly or LoadFrom paths; both
        //       guess which assembly to use and both are wrong for this purpose.
        var asm = typeof(WaveFormat).Assembly;
        _output.WriteLine("Assembly: " + asm.FullName);
        _output.WriteLine("Type: " + typeof(WaveFormat).FullName);

        var ctors = typeof(WaveFormat)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length)
            .ThenBy(c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name)), StringComparer.Ordinal)
            .ToArray();

        var factories = typeof(WaveFormat)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal)
                     || m.Name.StartsWith("As", StringComparison.Ordinal))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.GetParameters().Length)
            .ToArray();

        // Assert non-empty before iterating so the loop cannot pass vacuously.
        (ctors.Length + factories.Length).Should().BeGreaterThan(0,
            "WaveFormat must expose at least one constructor or static factory; zero means the type moved or the package changed shape.");

        _output.WriteLine("");
        _output.WriteLine("=== PUBLIC CONSTRUCTORS ===");
        foreach (var c in ctors)
        {
            _output.WriteLine("  ctor(" + JoinParams(c.GetParameters()) + ")");
        }

        _output.WriteLine("");
        _output.WriteLine("=== PUBLIC STATIC FACTORIES (Create* / As*) ===");
        foreach (var m in factories)
        {
            _output.WriteLine("  " + m.ReturnType.Name + " " + m.Name + "(" + JoinParams(m.GetParameters()) + ")");
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