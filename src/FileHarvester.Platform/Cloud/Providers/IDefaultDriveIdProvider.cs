// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IDefaultDriveIdProvider.cs
// PURPOSE:  The narrow seam over the single Graph call that resolves the
//           signed-in user's default drive id. It exists so the OneDrive
//           drive-id resolution can be unit tested without a concrete
//           GraphServiceClient, whose generated request chain is
//           non-virtual and cannot be substituted.
// RELATION: Implemented by GraphDefaultDriveIdProvider. Consumed by
//           GraphDriveIdResolver.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Resolves the signed-in user's default drive id through Microsoft Graph.
/// </summary>
/// <remarks>
/// Returns null when Graph reports no default drive. The caller decides
/// whether a null id is an error; the seam does not throw for absence.
/// </remarks>
internal interface IDefaultDriveIdProvider
{
    /// <summary>
    /// Returns the signed-in user's default drive id, or null when Graph
    /// reports none.
    /// </summary>
    /// <param name="ct">Cancels the Graph call.</param>
    /// <returns>The drive id, or null.</returns>
    Task<string?> GetDefaultDriveIdAsync(CancellationToken ct);
}
