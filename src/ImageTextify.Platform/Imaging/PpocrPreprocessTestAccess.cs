// filepath: src/ImageTextify.Platform/Imaging/PpocrPreprocessTestAccess.cs
// layer:    Infrastructure (test-only access)
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2 (added during the resize fix)
// purpose:  Exposes PpocrPreprocess.ToDetectionTensor to the test project
//           without making the whole PpocrPreprocess class public.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : PpocrPreprocess (internal static class, same assembly)
//   Used by    : FileHarvester.Tests.PreprocessTests
//   See also   : OnnxModelRunner.cs, PpocrPreprocess
//
// NOTES
//   - Internal because PpocrPreprocess is internal. Tests see this class
//     via <InternalsVisibleTo Include="FileHarvester.Tests" /> on the
//     Platform csproj (already present).
//   - Only adds a pass-through method; does not change PpocrPreprocess.

using Microsoft.ML.OnnxRuntime.Tensors;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Test-only access to <see cref="PpocrPreprocess"/> methods that are
/// otherwise internal to the assembly.
/// </summary>
/// <remarks>
/// Not intended for production use. Exists so PreprocessTests can call
/// PpocrPreprocess.ToDetectionTensor without a compile-time dependency on
/// internals visibility attributes on the test project.
/// </remarks>
internal static class PpocrPreprocessTestAccess
{
    /// <summary>
    /// Forwards to <see cref="PpocrPreprocess.ToDetectionTensor"/>.
    /// </summary>
    /// <param name="rgba">RGBA bytes. Length = width*height*4.</param>
    /// <param name="width">Input width in pixels.</param>
    /// <param name="height">Input height in pixels.</param>
    /// <returns>The detection tensor.</returns>
    public static DenseTensor<float> ToDetectionTensor(System.ReadOnlySpan<byte> rgba, int width, int height)
        => PpocrPreprocess.ToDetectionTensor(rgba, width, height);

    /// <summary>
    /// Forwards to <see cref="PpocrPreprocess.FindConnectedComponents"/>.
    /// </summary>
    public static System.Collections.Generic.List<DetectedComponent> FindConnectedComponents(
        Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> probMap, float threshold, int minPixelCount)
        => PpocrPreprocess.FindConnectedComponents(probMap, threshold, minPixelCount);

    /// <summary>Forwards to <see cref="PpocrPreprocess.MergeBoxesIntoLines"/>.</summary>
    public static System.Collections.Generic.List<DetectionBox> MergeBoxesIntoLines(
        System.Collections.Generic.List<DetectionBox> boxes)
        => PpocrPreprocess.MergeBoxesIntoLines(boxes);
}
