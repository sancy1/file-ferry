// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GraphDriveIdResolver.cs
// PURPOSE:  The default IOneDriveDriveIdResolver. Reads the default drive
//           id through the IDefaultDriveIdProvider seam and rejects an
//           absent or empty id.
// RELATION: Implements IOneDriveDriveIdResolver. Consumed by
//           ProviderConstruction.CreateOneDrive (F-136).
// NOTES
//   The resolver is pure and offline-testable: it depends only on the
//   IDefaultDriveIdProvider seam, which is substituted in tests. The
//   Graph call itself lives in GraphDefaultDriveIdProvider, which is
//   exercised by live validation.
//   Cancellation propagates unchanged; no exception is swallowed.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>The default drive-id resolver over the Graph seam.</summary>
internal sealed class GraphDriveIdResolver : IOneDriveDriveIdResolver
{
    /// <inheritdoc />
    public async Task<string> ResolveAsync(
        IDefaultDriveIdProvider driveIdProvider,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(driveIdProvider);

        var driveId = await driveIdProvider
            .GetDefaultDriveIdAsync(ct)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(driveId))
        {
            throw new InvalidOperationException(
                "Graph returned no default drive id.");
        }

        return driveId;
    }
}
