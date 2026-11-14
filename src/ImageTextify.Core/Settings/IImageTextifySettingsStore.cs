// filepath: src/ImageTextify.Core/Settings/IImageTextifySettingsStore.cs
// layer:    Contracts
// module:   ImageTextify.Core.Settings
// since:    1.2.0
// purpose:  Read/write access to the current ImageTextify settings snapshot

namespace ImageTextify.Core.Settings;

/// <summary>
/// Mutable access to the current ImageTextify settings snapshot. Callers
/// read <see cref="Current"/> to obtain a snapshot and call
/// <see cref="Replace"/> to swap it. A snapshot obtained from
/// <see cref="Current"/> is immutable and safe to hold across operations.
/// </summary>
/// <remarks>
/// Thread-safety: implementations must make <see cref="Current"/> and
/// <see cref="Replace"/> safe to call concurrently. This type does NOT
/// persist; persistence is a separate concern (finding ITX-B9-003).
/// Naming note: members are Current/Replace, not Get/Set, because CA1716
/// (reserved keyword) is enforced as an error in this repository.
/// </remarks>
public interface IImageTextifySettingsStore
{
    /// <summary>
    /// Current settings snapshot. Never null.
    /// </summary>
    ImageTextifySettings Current { get; }

    /// <summary>
    /// Replaces the current snapshot atomically. Readers that already
    /// obtained the previous snapshot keep it; subsequent reads of
    /// <see cref="Current"/> see the new one.
    /// </summary>
    /// <param name="settings">Non-null replacement snapshot.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> is null.
    /// </exception>
    void Replace(ImageTextifySettings settings);
}
