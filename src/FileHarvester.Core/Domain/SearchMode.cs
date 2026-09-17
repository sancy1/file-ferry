// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/SearchMode.cs
// PURPOSE:  How the resolver interprets a bare filename during PC-wide
//           search. ExactName means the input must equal a file's full name
//           (including extension). BaseName means the input is treated as
//           the name without extension and matches any file with that base.
// RELATION: Consumed by IPathSearchService implementations and by
//           FileResolver when a bare filename is provided. Exposed to the
//           UI as a toggle in the Input panel.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// How the resolver interprets a bare filename.
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// The input must match a file's full name, including its extension.
    /// "note.txt" matches only files literally named "note.txt".
    /// This is the safest, most predictable mode.
    /// </summary>
    ExactName = 0,

    /// <summary>
    /// The input is treated as a base name without extension. "note"
    /// matches "note.txt", "note.pdf", "note.png", "notes.md", and any
    /// other file whose name starts with or equals "note" (case-insensitive
    /// on Windows/macOS, case-sensitive on Linux). Every match is returned
    /// as an ambiguous result the user picks from.
    /// </summary>
    BaseName = 1,
}
