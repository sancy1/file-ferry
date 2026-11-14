// filepath: src/ImageTextify.Core/Settings/InMemoryImageTextifySettingsStore.cs
// layer:    Infrastructure
// module:   ImageTextify.Core.Settings
// since:    1.2.0
// purpose:  In-memory implementation of IImageTextifySettingsStore for the
//           current process lifetime. No persistence.

namespace ImageTextify.Core.Settings;

/// <summary>
/// In-memory <see cref="IImageTextifySettingsStore"/>. Holds a single
/// current snapshot and swaps it atomically on <see cref="Replace"/>.
/// No persistence: settings reset when the process restarts.
/// </summary>
/// <remarks>
/// WHY in-memory in Phase 2.4e: persistence is a separate concern; see
/// finding ITX-B9-003.
/// Thread-safety: a single volatile reference to an immutable record;
/// reads and writes are safe under the .NET memory model.
/// </remarks>
public sealed class InMemoryImageTextifySettingsStore : IImageTextifySettingsStore
{
    private volatile ImageTextifySettings _current;

    /// <summary>
    /// Initializes a new store with the given initial settings, or
    /// <see cref="ImageTextifySettings.Default"/> when
    /// <paramref name="initial"/> is null.
    /// </summary>
    /// <param name="initial">
    /// Initial settings snapshot. Null means use
    /// <see cref="ImageTextifySettings.Default"/>.
    /// </param>
    public InMemoryImageTextifySettingsStore(ImageTextifySettings? initial = null)
    {
        _current = initial ?? ImageTextifySettings.Default;
    }

    /// <inheritdoc />
    public ImageTextifySettings Current => _current;

    /// <inheritdoc />
    public void Replace(ImageTextifySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings;
    }
}
