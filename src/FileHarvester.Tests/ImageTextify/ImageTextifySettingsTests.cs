// filepath: src/FileHarvester.Tests/ImageTextify/ImageTextifySettingsTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    1.2.0
// purpose:  Contract tests for ImageTextifySettings and
//           InMemoryImageTextifySettingsStore

using ImageTextify.Core.Settings;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Contract tests for <see cref="ImageTextifySettings"/> and
/// <see cref="InMemoryImageTextifySettingsStore"/>. Proves the default
/// state (fallback off, threshold 0.3), the range check, and the store
/// swap semantics.
/// </summary>
public sealed class ImageTextifySettingsTests
{
    /// <summary>
    /// The default settings must have the Qwen printed fallback disabled.
    /// </summary>
    [Fact]
    public void DefaultHasFallbackDisabled()
    {
        Assert.False(ImageTextifySettings.Default.EnableQwenPrintedFallback);
    }

    /// <summary>
    /// The default settings must use threshold 0.3.
    /// </summary>
    [Fact]
    public void DefaultThresholdIsPointThree()
    {
        Assert.Equal(0.3, ImageTextifySettings.Default.LowConfidenceThreshold);
    }

    /// <summary>
    /// The constructor must reject thresholds outside [0, 1].
    /// </summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void ConstructorRejectsOutOfRangeThreshold(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImageTextifySettings(true, threshold));
    }

    /// <summary>
    /// Replace swaps the snapshot; a snapshot held before Replace
    /// remains the old instance.
    /// </summary>
    [Fact]
    public void StoreReplaceSwapsSnapshot()
    {
        // ARRANGE
        var store = new InMemoryImageTextifySettingsStore();
        var before = store.Current;

        // ACT
        var replacement = new ImageTextifySettings(true, 0.5);
        store.Replace(replacement);
        var after = store.Current;

        // ASSERT
        Assert.False(before.EnableQwenPrintedFallback);
        Assert.Equal(0.3, before.LowConfidenceThreshold);
        Assert.True(after.EnableQwenPrintedFallback);
        Assert.Equal(0.5, after.LowConfidenceThreshold);
    }
}
