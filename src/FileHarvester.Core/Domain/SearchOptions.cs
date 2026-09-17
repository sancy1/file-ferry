// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/SearchOptions.cs
// PURPOSE:  Configuration for a PC-wide filename search. Carries the mode,
//           result cap, per-tier timeout, and skip list.
// RELATION: Consumed by IPathSearchService implementations. Created by
//           FileResolver using universal (BaseName) matching by default.
// ============================================================================

namespace FileHarvester.Core.Domain;

public sealed record SearchOptions(
    SearchMode Mode = SearchMode.BaseName,
    int MaxResults = 2000,
    TimeSpan? TimeoutPerTier = null,
    IReadOnlyList<string>? SkipList = null)
{
    public static SearchOptions Default { get; } = new();

    public TimeSpan EffectiveTimeoutPerTier =>
        TimeoutPerTier ?? TimeSpan.FromSeconds(10);

    public IReadOnlyList<string> EffectiveSkipList =>
        SkipList ?? DefaultSkipList;

    /// <summary>
    /// Directories that are almost never what the user wants and that
    /// dramatically slow recursive searches. Program Files is NOT skipped:
    /// it contains many legitimate README files, docs, and examples.
    /// </summary>
    public static IReadOnlyList<string> DefaultSkipList { get; } = new[]
    {
        // Windows system directories (system files, huge)
        @"C:\Windows",
        @"C:\ProgramData\Package Cache",
        @"C:\$Recycle.Bin",
        @"C:\System Volume Information",

        // Developer / build noise
        "node_modules",
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        "bin",
        "obj",

        // Caches and temp
        "AppData\\Local\\Temp",
        "AppData\\Local\\Packages",
        "AppData\\Local\\Microsoft\\Windows\\INetCache",
    };
}
