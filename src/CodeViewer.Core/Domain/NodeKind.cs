// ============================================================================
// FILE:     src/CodeViewer.Core/Domain/NodeKind.cs
// PURPOSE:  Distinguishes directory nodes from file nodes in the project
//           tree. Directories can be expanded and lazily loaded; files can
//           be previewed in the Code Content viewer.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// The kind of a <see cref="ProjectNode"/>.
/// </summary>
public enum NodeKind
{
    /// <summary>A directory. May contain children loaded on demand.</summary>
    Directory,

    /// <summary>A file. May be previewed when it is a supported text file.</summary>
    File,
}
