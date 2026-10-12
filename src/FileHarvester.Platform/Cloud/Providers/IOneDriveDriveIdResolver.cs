// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IOneDriveDriveIdResolver.cs
// PURPOSE:  Resolves a usable OneDrive drive id for the OneDrive provider.
//           A resolver returns a non-empty id or throws; a missing drive
//           is a construction fault, not a silent null.
// RELATION: Implemented by GraphDriveIdResolver. Consumed by
//           ProviderConstruction.CreateOneDrive (F-136).
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>Resolves the OneDrive drive id for provider construction.</summary>
internal interface IOneDriveDriveIdResolver
{
    /// <summary>
    /// Resolves a non-empty drive id.
    /// </summary>
    /// <param name="driveIdProvider">
    /// The drive-id seam. Non-null. The resolver does not construct it;
    /// the caller owns its lifetime.
    /// </param>
    /// <param name="ct">Cancels the resolution.</param>
    /// <returns>A non-empty drive id.</returns>
    /// <exception cref="ArgumentNullException">
    /// The drive-id provider is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The provider returned null or an empty id.
    /// </exception>
    Task<string> ResolveAsync(
        IDefaultDriveIdProvider driveIdProvider,
        CancellationToken ct);
}
