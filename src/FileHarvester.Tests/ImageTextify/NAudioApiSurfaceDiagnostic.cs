// filepath: src/FileHarvester.Tests/ImageTextify/NAudioApiSurfaceDiagnostic.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.A
// purpose:  Diagnostic test that dumps the NAudio.Wasapi 3.3.0 public API
//           surface to the test output, so the adapter can be written
//           against the measured shape rather than a remembered one.
//
// RELATIONSHIPS
//   Implements : n/a (diagnostic test)
//   Depends on : NAudio (WasapiCapture, WasapiRecorder, WasapiRecorderBuilder)
//   Used by    : dotnet test --filter NAudioApiSurfaceDiagnostic
//   See also   : src/ImageTextify.Platform/Audio/NAudioAudioCaptureService.cs
//
// NOTES
//   - Disposable diagnostic. Run it once to see the API surface, then
//     delete or keep as documentation. It does not assert anything that
//     can break in future NAudio versions; it asserts only that the
//     reflection produced at least one line.
//   - Runs in the .NET 10 test host so the assembly resolves. A
//     PowerShell-hosted reflection over the same DLL fails with
//     ReflectionTypeLoadException because PowerShell 5.1 is .NET
//     Framework.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

public sealed class NAudioApiSurfaceDiagnostic
{
    private readonly ITestOutputHelper _out;

    public NAudioApiSurfaceDiagnostic(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void DumpNAudioWasapiSurface()
    {
        var output = new List<string>();
        var wasapi = typeof(NAudio.CoreAudioApi.MMDeviceEnumerator).Assembly;
        output.Add("Assembly: " + wasapi.FullName);

        var typeNames = new[] {
            "NAudio.Wave.WasapiRecorderBuilder",
            "NAudio.Wave.WasapiRecorder",
            "NAudio.Wave.WasapiCapture",
            "NAudio.Wave.WasapiLoopbackCapture",
            "NAudio.Wave.AudioBuffer",
            "NAudio.Wave.CaptureDataAvailableHandler",
            "NAudio.Wave.WaveFormat",
        };

        foreach (var typeName in typeNames)
        {
            var t = wasapi.GetType(typeName);
            if (t is null) { output.Add(typeName + " NOT FOUND"); continue; }
            output.Add("");
            output.Add("=== " + typeName + " ===");
            foreach (var c in t.GetConstructors())
                output.Add("  ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            foreach (var m in t.GetMethods().Where(m => m.IsPublic && !m.IsSpecialName && m.DeclaringType == t))
                output.Add("  " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            foreach (var p in t.GetProperties())
                output.Add("  PROP: " + p.PropertyType.Name + " " + p.Name);
            foreach (var e in t.GetEvents())
                output.Add("  EVENT: " + e.EventHandlerType?.Name + " " + e.Name);
        }

        foreach (var line in output) { _out.WriteLine(line); }
        output.Should().NotBeEmpty();
    }
}