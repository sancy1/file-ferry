// filepath: src/FileHarvester.Tests/ImageTextify/ConfiguredFfmpegLocatorTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.B.1
// purpose:  Behavior tests for ConfiguredFfmpegLocator. Verifies that
//           missing directories and missing binaries fail loudly, and
//           that present binaries resolve to absolute paths.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : ImageTextify.Platform.Media.ConfiguredFfmpegLocator
//   Used by    : dotnet test --filter ConfiguredFfmpegLocatorTests
//   See also   : ImageTextify.Core.Abstractions.IFfmpegLocator
//
// NOTES
//   - These tests do not invoke FFmpeg. They test path resolution only.
//   - Tests write to a throwaway directory under Path.GetTempPath().

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Platform.Media;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class ConfiguredFfmpegLocatorTests
{
    [Fact]
    public void ConfiguredFfmpegLocatorThrowsWhenDirectoryMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "imagetextify-no-such-dir-" + Guid.NewGuid().ToString("N"));

        Action ctor = () =>
        {
            var _unused = new ConfiguredFfmpegLocator(missing);
        };

        ctor.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void ConfiguredFfmpegLocatorThrowsWhenFfmpegExeMissing()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "imagetextify-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var locator = new ConfiguredFfmpegLocator(emptyDir);

            Action act = () => locator.GetFfmpegPath();

            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            try { Directory.Delete(emptyDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ConfiguredFfmpegLocatorResolvesBothBinariesWhenPresent()
    {
        var binDir = Path.Combine(Path.GetTempPath(), "imagetextify-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(binDir);
        var ffmpeg = Path.Combine(binDir, "ffmpeg.exe");
        var ffprobe = Path.Combine(binDir, "ffprobe.exe");
        File.WriteAllText(ffmpeg, "stub");
        File.WriteAllText(ffprobe, "stub");
        try
        {
            var locator = new ConfiguredFfmpegLocator(binDir);

            locator.GetFfmpegPath().Should().Be(ffmpeg);
            locator.GetFfprobePath().Should().Be(ffprobe);
        }
        finally
        {
            try { Directory.Delete(binDir, recursive: true); } catch { }
        }
    }
}