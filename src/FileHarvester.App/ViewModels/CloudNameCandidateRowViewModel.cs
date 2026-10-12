// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CloudNameCandidateRowViewModel.cs
// PURPOSE:  One row in the discovery dialog result list. Wraps a
//           CloudNameCandidate and exposes the display name and a detail
//           line (provider id, source, confidence).
// RELATION: Created by CloudDiscoveryDialog. Bound by CloudDiscoveryDialog.axaml.
// ============================================================================

using System;
using FileHarvester.Platform.Cloud.Discovery;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One candidate row in the discovery dialog.
/// </summary>
public sealed class CloudNameCandidateRowViewModel
{
    /// <summary>
    /// Creates the row over a candidate.
    /// </summary>
    /// <param name="candidate">The candidate. Not null.</param>
    public CloudNameCandidateRowViewModel(CloudNameCandidate candidate)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    }

    /// <summary>The underlying candidate.</summary>
    public CloudNameCandidate Candidate { get; }

    /// <summary>The user-visible provider name.</summary>
    public string DisplayName => Candidate.CanonicalName;

    /// <summary>
    /// The detail line: the provider id, the source, and the confidence.
    /// </summary>
    public string DetailText =>
        Candidate.ProviderId
        + "  ·  " + Candidate.Source.ToString()
        + "  ·  " + Candidate.Confidence.ToString();
}
