// ============================================================================
// filepath: src/FileHarvester.Tests/ImageTextify/ConversionViewModelTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 11 (Document/Image Conversion)
// purpose:  ViewModel tests for the conversion workspace. Prove the output
//           path always carries the target extension regardless of what the
//           save picker returns (Phase 11 Defect 2 regression).
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : ConversionViewModel, IConversionService, ConversionRequest,
//                ConversionResult, ConversionFormat, ConversionPair
//   Used by    : test runner
//   See also   : ConversionTests.cs (service-level tests)
// NOTES
//   - The picker delegates are fakes; the service fake captures the request
//     and returns success with the requested output path.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.App.ViewModels;
using ImageTextify.Core.Abstractions;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for <see cref="ConversionViewModel"/> output-path handling.
/// </summary>
public sealed class ConversionViewModelTests
{
    [Fact]
    public async Task ConvertAppendsTargetExtensionWhenPickerReturnsNone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "convvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "photo.jpg");
            File.WriteAllBytes(input, new byte[] { 1, 2, 3 });

            string? capturedOutput = null;
            var svc = new FakeConversionService((req, _) =>
            {
                capturedOutput = req.OutputPath;
                File.WriteAllBytes(req.OutputPath, new byte[] { 1 });
                return Task.FromResult(ConversionResult.Ok(req.OutputPath));
            });

            // Picker returns a path with NO extension, as the real picker did.
            var vm = new ConversionViewModel(
                svc,
                onPickInputFile: _ => Task.FromResult<string?>(input),
                onPickOutputFile: (_, _) => Task.FromResult<string?>(Path.Combine(dir, "out")));

            await vm.SelectFileAsync();
            vm.TargetFormat = ConversionFormat.Ico;
            await vm.ConvertAsync();

            Assert.NotNull(capturedOutput);
            Assert.EndsWith(".ico", capturedOutput!, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(capturedOutput!));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task ConvertReplacesWrongExtensionWithTargetExtension()
    {
        var dir = Path.Combine(Path.GetTempPath(), "convvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "photo.jpg");
            File.WriteAllBytes(input, new byte[] { 1, 2, 3 });

            string? capturedOutput = null;
            var svc = new FakeConversionService((req, _) =>
            {
                capturedOutput = req.OutputPath;
                File.WriteAllBytes(req.OutputPath, new byte[] { 1 });
                return Task.FromResult(ConversionResult.Ok(req.OutputPath));
            });

            // Picker returns a path with the WRONG extension.
            var vm = new ConversionViewModel(
                svc,
                onPickInputFile: _ => Task.FromResult<string?>(input),
                onPickOutputFile: (_, _) => Task.FromResult<string?>(Path.Combine(dir, "out.png")));

            await vm.SelectFileAsync();
            vm.TargetFormat = ConversionFormat.Ico;
            await vm.ConvertAsync();

            Assert.NotNull(capturedOutput);
            Assert.EndsWith(".ico", capturedOutput!, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task SuggestedNameIncludesTargetExtension()
    {
        var dir = Path.Combine(Path.GetTempPath(), "convvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "photo.jpg");
            File.WriteAllBytes(input, new byte[] { 1, 2, 3 });

            string? suggested = null;
            var svc = new FakeConversionService((req, _) =>
            {
                File.WriteAllBytes(req.OutputPath, new byte[] { 1 });
                return Task.FromResult(ConversionResult.Ok(req.OutputPath));
            });

            var vm = new ConversionViewModel(
                svc,
                onPickInputFile: _ => Task.FromResult<string?>(input),
                onPickOutputFile: (name, _) =>
                {
                    suggested = name;
                    return Task.FromResult<string?>(Path.Combine(dir, name));
                });

            await vm.SelectFileAsync();
            vm.TargetFormat = ConversionFormat.Png;
            await vm.ConvertAsync();

            Assert.NotNull(suggested);
            Assert.EndsWith(".png", suggested!, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private sealed class FakeConversionService : IConversionService
    {
        private readonly Func<ConversionRequest, IProgress<ConversionProgress>?, Task<ConversionResult>> _convert;

        public FakeConversionService(Func<ConversionRequest, IProgress<ConversionProgress>?, Task<ConversionResult>> convert)
        {
            _convert = convert;
        }

        public Task<IReadOnlyList<ConversionPair>> EnumerateSupportedPairsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ConversionPair>>(Array.Empty<ConversionPair>());

        public Task<ConversionResult> ConvertAsync(
            ConversionRequest request,
            IProgress<ConversionProgress>? progress,
            CancellationToken cancellationToken)
            => _convert(request, progress);
    }
}
