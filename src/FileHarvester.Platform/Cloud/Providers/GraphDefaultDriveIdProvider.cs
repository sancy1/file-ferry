// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GraphDefaultDriveIdProvider.cs
// PURPOSE:  The live implementation of IDefaultDriveIdProvider. Makes one
//           Graph call (Me.Drive.GetAsync) and returns the drive id, or null.
//           The call shape is probe-confirmed: Me.Drive exists,
//           GetAsync(cancellationToken:) exists, Models.Drive.Id is a string.
// RELATION: Implements IDefaultDriveIdProvider. Constructed per OAuth
//           instance by ProviderConstruction.CreateOneDrive.
// NOTES
//   This type is not unit-tested offline: GraphServiceClient is a
//   concrete Kiota class whose fluent request chain cannot be
//   substituted. The narrow seam IDefaultDriveIdProvider is the test
//   boundary; this type is exercised by live validation.
//   Cancellation propagates unchanged. No exception is swallowed.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>The Graph-backed default drive id provider.</summary>
internal sealed class GraphDefaultDriveIdProvider : IDefaultDriveIdProvider
{
    private readonly GraphServiceClient _graph;

    /// <summary>Creates the provider over a Graph client.</summary>
    /// <param name="graph">The Graph client. Not null.</param>
    public GraphDefaultDriveIdProvider(GraphServiceClient graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <inheritdoc />
    public async Task<string?> GetDefaultDriveIdAsync(CancellationToken ct)
    {
        var drive = await _graph.Me.Drive
            .GetAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        return drive?.Id;
    }
}
