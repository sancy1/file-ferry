// filepath: src/CodeViewer.Core/Domain/ExclusionKind.cs
// ============================================================================
// PURPOSE:  The two kinds of exclusion rules the CodeViewer exclusion manager
//           supports. Folder rules exclude an entire directory subtree; File
//           rules exclude individual files. The two kinds are independent, so
//           a folder named "x" and a file named "x" can coexist as separate
//           rules without conflict.
// RELATION: Used by ExclusionRule and GlobalExclusions in this folder.
//           Persisted by JsonGlobalExclusionStore as a string (JsonConverter).
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// The kind of filesystem entry an exclusion rule targets.
/// </summary>
public enum ExclusionKind
{
    /// <summary>
    /// A directory name. Matching directories are excluded together with all
    /// of their descendants; the enumerator never yields them, so their
    /// contents are never enumerated.
    /// </summary>
    Folder,

    /// <summary>
    /// A file name. Matching files are excluded individually; their siblings
    /// and ancestors are unaffected.
    /// </summary>
    File,
}