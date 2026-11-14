// filepath: src/FileHarvester.Tests/ImageTextify/FakeImageTextifySettingsStore.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    1.2.0
// purpose:  Deterministic IImageTextifySettingsStore for tests

using ImageTextify.Core.Settings;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Deterministic <see cref="IImageTextifySettingsStore"/> for tests.
/// Wraps <see cref="InMemoryImageTextifySettingsStore"/> so the test
/// uses the same concurrency semantics as production, not a mock.
/// </summary>
public sealed class FakeImageTextifySettingsStore : IImageTextifySettingsStore
{
    private readonly InMemoryImageTextifySettingsStore _inner;

    /// <summary>
    /// Initializes a new fake with the given initial settings, or
    /// <see cref="ImageTextifySettings.Default"/> when null.
    /// </summary>
    /// <param name="initial">Initial settings snapshot, or null for default.</param>
    public FakeImageTextifySettingsStore(ImageTextifySettings? initial = null)
    {
        _inner = new InMemoryImageTextifySettingsStore(initial);
    }

    /// <inheritdoc />
    public ImageTextifySettings Current => _inner.Current;

    /// <inheritdoc />
    public void Replace(ImageTextifySettings settings) => _inner.Replace(settings);
}
