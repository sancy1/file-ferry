// ============================================================================
// FILE:     src/CodeViewer.Core/Domain/FileRoleClassifier.cs
// PURPOSE:  Maps a file's extension to the human-readable role shown on
//           its code card (for example "Python Source", "C# Source").
//           Unknown extensions fall back to "Text File" so every file
//           keeps a role label.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// Classifies files into the role labels displayed on code cards.
/// </summary>
public static class FileRoleClassifier
{
    private static readonly Dictionary<string, string> RolesByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"]      = "C# Source",
            [".csproj"]  = "C# Project File",
            [".sln"]     = "Solution File",
            [".slnx"]    = "Solution File",
            [".py"]      = "Python Source",
            [".js"]      = "JavaScript Source",
            [".jsx"]     = "JavaScript Source",
            [".ts"]      = "TypeScript Source",
            [".tsx"]     = "TypeScript Source",
            [".java"]    = "Java Source",
            [".go"]      = "Go Source",
            [".rs"]      = "Rust Source",
            [".cpp"]     = "C++ Source",
            [".cc"]      = "C++ Source",
            [".cxx"]     = "C++ Source",
            [".c"]       = "C Source",
            [".h"]       = "C/C++ Header",
            [".hpp"]     = "C++ Header",
            [".fs"]      = "F# Source",
            [".vb"]      = "Visual Basic Source",
            [".html"]    = "HTML Document",
            [".htm"]     = "HTML Document",
            [".css"]     = "CSS Stylesheet",
            [".scss"]    = "SCSS Stylesheet",
            [".less"]    = "Less Stylesheet",
            [".json"]    = "JSON Document",
            [".xml"]     = "XML Document",
            [".axaml"]   = "Avalonia XAML",
            [".xaml"]    = "XAML Document",
            [".yaml"]    = "YAML Document",
            [".yml"]     = "YAML Document",
            [".toml"]    = "TOML Document",
            [".md"]      = "Markdown Document",
            [".txt"]     = "Text File",
            [".sql"]     = "SQL Script",
            [".sh"]      = "Shell Script",
            [".bash"]    = "Shell Script",
            [".ps1"]     = "PowerShell Script",
            [".psm1"]    = "PowerShell Module",
            [".bat"]     = "Batch Script",
            [".cmd"]     = "Batch Script",
            [".ini"]     = "INI Configuration",
            [".cfg"]     = "Configuration File",
            [".config"]  = "Configuration File",
            [".csv"]     = "CSV Data",
            [".razor"]   = "Razor View",
            [".cshtml"]  = "Razor View",
            [".php"]     = "PHP Source",
            [".rb"]      = "Ruby Source",
            [".swift"]   = "Swift Source",
            [".kt"]      = "Kotlin Source",
            [".kts"]     = "Kotlin Script",
            [".lua"]     = "Lua Source",
            [".r"]       = "R Source",
            [".dart"]    = "Dart Source",
            [".vue"]     = "Vue Component",
            [".svelte"]  = "Svelte Component",
            [".dockerfile"] = "Dockerfile",
            [".gitignore"]  = "Git Ignore Rules",
            [".gitattributes"] = "Git Attributes",
            [".editorconfig"]  = "EditorConfig",
        };

    /// <summary>
    /// Returns the role label for a file name or path. Files without an
    /// extension, and extensions not in the table, are "Text File".
    /// </summary>
    public static string Classify(string fileNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrPath);

        // Well-known extensionless file names.
        var name = Path.GetFileName(fileNameOrPath);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
        {
            return "Dockerfile";
        }

        var extension = Path.GetExtension(name);
        if (extension.Length == 0)
        {
            return "Text File";
        }

        return RolesByExtension.TryGetValue(extension, out var role)
            ? role
            : "Text File";
    }
}
