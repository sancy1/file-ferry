// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/IFormatWriterRegistry.cs
// PURPOSE:  Contract for a service that holds every registered IFormatWriter
//           and looks them up by file extension. The Output Options dropdown
//           queries the registry to populate its list. The future OutputSink
//           (Phase 6) uses the registry to dispatch by extension.
// RELATION: Consumed by OutputOptionsViewModel (to populate the format
//           dropdown) and by OutputSink (to select the writer for a job).
//           Implemented by FormatWriterRegistry in the same folder.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

/// <summary>
/// A registry of every IFormatWriter available in the application.
/// </summary>
public interface IFormatWriterRegistry
{
    /// <summary>
    /// Every registered writer, in registration order.
    /// </summary>
    IReadOnlyList<IFormatWriter> AllWriters { get; }

    /// <summary>
    /// Looks up a writer by file extension. The extension must include the
    /// leading dot and is compared case-insensitively.
    /// </summary>
    /// <param name="extension">Example: ".md"</param>
    /// <returns>The writer, or null if no writer claims this extension.</returns>
    IFormatWriter? GetByExtension(string extension);
}
