// ============================================================================
// filepath: src/ImageTextify.Core/Abstractions/IConversionService.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Top-level dispatch contract for the conversion app. The App layer
//           depends on this interface only; the platform-side implementation
//           dispatches to the per-format IFormatConverter implementations.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : ImageTextify.Core.Abstractions.IFormatConverter
//   Used by    : FileHarvester.App.ViewModels.ConversionViewModel;
//                ImageTextify.Platform.Conversion.ConversionService (impl)
//   See also   : IFormatConverter.cs; Phase 11 design plan
//
// NOTES
//   - SupportedPairs tells the UI exactly which (source, target) combinations
//     are proven to work. Anything absent from this list is rejected by
//     ConvertAsync with a specific named reason (F-030/031/032).
//   - EnumerateSupportedPairsAsync is sync-shaped (returns a Task) so an
//     implementation can later probe the filesystem without changing the
//     contract.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Top-level conversion dispatch. The single entry point the UI uses.
/// </summary>
public interface IConversionService
{
    /// <summary>
    /// Lists every (source extension, target format) pair this build can
    /// perform. The UI populates the target dropdown from this list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>
    /// The supported pairs. Never null; may be empty in theory, never in
    /// practice for a working build.
    /// </returns>
    Task<IReadOnlyList<ConversionPair>> EnumerateSupportedPairsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Converts one file. Dispatches to the per-format converter. Rejects
    /// pairs outside <see cref="EnumerateSupportedPairsAsync"/> with a specific
    /// reason (never a generic failure).
    /// </summary>
    /// <param name="request">The conversion request. Not null.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">
    /// Cancels the conversion. On cancellation no output file is left behind.
    /// </param>
    /// <returns>
    /// The conversion result. A failure carries a user-facing reason.
    /// </returns>
    Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// One supported conversion pair: a source extension and a target format.
/// </summary>
/// <param name="SourceExtension">
/// The source file extension, lowercase, with the leading dot (e.g. ".png").
/// </param>
/// <param name="TargetFormat">The target format.</param>
/// <param name="DisplayName">
/// A user-facing label ("PNG image to ICO icon"). Populated by the
/// implementation, not computed by the UI.
/// </param>
public sealed record ConversionPair(
    string SourceExtension,
    ConversionFormat TargetFormat,
    string DisplayName);
