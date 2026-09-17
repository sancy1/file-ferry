// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/FileRequest.cs
// PURPOSE:  Domain record representing one line of user input before any
//           resolution has happened. Carries the raw text the user pasted or
//           selected, plus an optional hint about where it came from.
// RELATION: Produced by the UI intake panel (Phase 3.1). Consumed by
//           IFileResolver (Phase 1.1) which turns each FileRequest into
//           zero or more ResolvedFile records. The Index field preserves
//           user-visible ordering even when resolution reorders work.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// One user-supplied path or filename, before resolution.
/// </summary>
/// <param name="Index">
/// Zero-based position in the user's original list. Preserved through
/// resolution so the UI can show results in the order the user supplied.
/// </param>
/// <param name="RawInput">
/// The exact text the user provided, trimmed of leading/trailing
/// whitespace. May be an absolute path, a relative path, or a bare
/// filename. Never null, never empty (the intake panel filters empties).
/// </param>
/// <param name="HintedDirectory">
/// Optional: the directory the input came from, if known. Set when the
/// user drags a file from a folder or uses "Add Folder" — the resolver
/// tries this directory first before falling back to whole-PC search.
/// Null when the user pasted a raw path with no context.
/// </param>
public sealed record FileRequest(
    int Index,
    string RawInput,
    string? HintedDirectory = null);
