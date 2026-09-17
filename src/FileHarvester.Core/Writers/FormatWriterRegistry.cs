// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/FormatWriterRegistry.cs
// PURPOSE:  Implementation of IFormatWriterRegistry. Collects every
//           registered IFormatWriter from DI, indexes them by extension,
//           and provides lookup for the dropdown and for the future
//           OutputSink.
// RELATION: Implements IFormatWriterRegistry. Registered as a singleton in
//           AddFileHarvester. Consumed by OutputOptionsViewModel and (in
//           Phase 6) OutputSink.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

/// <summary>
/// A registry that indexes IFormatWriter implementations by file extension.
/// </summary>
public sealed class FormatWriterRegistry : IFormatWriterRegistry
{
    private readonly Dictionary<string, IFormatWriter> _byExtension;

    public FormatWriterRegistry(IEnumerable<IFormatWriter> writers)
    {
        ArgumentNullException.ThrowIfNull(writers);

        var list = writers.ToList();

        _byExtension = new Dictionary<string, IFormatWriter>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var writer in list)
        {
            if (string.IsNullOrWhiteSpace(writer.Extension))
            {
                throw new InvalidOperationException(
                    $"Writer {writer.GetType().Name} has a null or empty Extension.");
            }

            if (!writer.Extension.StartsWith('.'))
            {
                throw new InvalidOperationException(
                    $"Writer {writer.GetType().Name} Extension '{writer.Extension}' " +
                    "must start with a dot (e.g. \".md\").");
            }

            if (_byExtension.TryGetValue(writer.Extension, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate extension '{writer.Extension}' claimed by " +
                    $"{existing.GetType().Name} and {writer.GetType().Name}.");
            }

            _byExtension[writer.Extension] = writer;
        }

        AllWriters = list;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IFormatWriter> AllWriters { get; }

    /// <inheritdoc/>
    public IFormatWriter? GetByExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return _byExtension.TryGetValue(extension, out var writer) ? writer : null;
    }
}
