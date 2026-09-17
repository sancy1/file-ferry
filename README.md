# File-Ferry

> Harvest files from anywhere on your PC. Copy them byte-for-byte. Deliver them in any format you choose.

File-Ferry is a modern, cross-platform-ready desktop application that accepts a list of file paths or bare filenames, resolves them across your entire machine, reads their contents with byte-level fidelity, and writes them out — either as **separate per-file documents** or as **one merged bundle** — in the format and destination of your choice.

Built on **.NET 10**, **Avalonia UI 12**, and **Portfolio.Resilience 0.8.0**.

---

## Features

- **Three input methods** — Paste paths or filenames, use the native file picker, or use the folder picker.
- **Universal search** — Type `note` and the app finds `note.txt`, `note.pdf`, `note.png`, `notes.md`, and other matching files anywhere on the PC.
- **Ambiguity picker** — When a bare name matches multiple files, choose which ones to include using presets such as **Newest 5**, **Largest 5**, **Select All**, and **Select None**.
- **Nine output formats** — `.txt` · `.md` · `.json` · `.html` · `.docx` · `.pdf` · `.xlsx` · `.csv` · `.zip`
- **Two output modes** — **Separate Files** (one file per source) or **Single Merged** (one combined file).
- **Clean output by default** — Content is preserved exactly as it is, with no decorations unless you explicitly enable them.
- **Custom file naming** — For merged output, name the file yourself. For separate output, specify a filename prefix.
- **Resilience pipeline** — Every file read and write runs through retry, circuit breaker, timeout, and fallback policies from Portfolio.Resilience.
- **Live log drawer** — Every pipeline event streams into a collapsible overlay with color-coded status.
- **Diagnostics tab** — Monitor live p50/p95/p99 latency, error rates, in-flight counts, and circuit states per policy.
- **Light and dark themes** — Automatic system-aware theme or manual theme toggle.
- **Windows 11-native feel** — Mica backdrop, rounded corners, and Fluent-inspired design.

---

## Quickstart

### For Users — Download and Run

1. Download `FileFerry.exe` (approximately 72 MB).
2. Double-click the executable.
3. Paste a file path, click **Add Files…**, or add files using the folder picker.
4. Click **Parse Paste** to add paths to the list.
5. Click **Resolve** to locate every file.
6. Choose a format and destination in the **Output Options** panel.
7. Click **Harvest Now**.
8. Click **Open Folder** to view the output.

> **No installer required. No .NET runtime required. The application is self-contained.**

### For Developers — Build from Source

```powershell
git clone <repository-url>
cd file-ferry
dotnet restore FileHarvester.slnx
dotnet build FileHarvester.slnx
dotnet run --project src\FileHarvester.App
```

### Publish a Single-File Windows Executable

```powershell
dotnet publish src\FileHarvester.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o artifacts\FileFerry-win-x64
```

The published executable will be located at:

```text
artifacts\FileFerry-win-x64\FileHarvester.App.exe
```

Rename it to `FileFerry.exe` if desired.

---

## Technology Stack

| Layer                | Technology                                    |
| -------------------- | --------------------------------------------- |
| Runtime              | .NET 10                                       |
| Language             | C# 14                                         |
| UI                   | Avalonia UI 12.1.2                            |
| Resilience           | Portfolio.Resilience 0.8.0                    |
| Resilience Analyzers | Portfolio.Resilience.Analyzers 0.8.0          |
| Format Writers       | DocumentFormat.OpenXml · QuestPDF · ClosedXML |
| Dependency Injection | Microsoft.Extensions.DependencyInjection 10.0 |
| Testing              | xUnit · FluentAssertions                      |

---

## Architecture

```text
FileHarvester.App       (Avalonia UI — presentation)
FileHarvester.Core      (domain logic — OS-agnostic, UI-agnostic)
FileHarvester.Platform  (OS-specific adapters behind interfaces)
FileHarvester.Tests     (xUnit — all layers)
```

### Data Flow

```text
User input
    ↓
FileRequest[]
    ↓
IFileResolver
    ↓
ResolvedFile[]
    ↓
IContentReader
    ↓
FileContent[]
    ↓
IBundleComposer
    ↓
ComposedDocument
    ↓
IFormatWriter
    ↓
bytes
    ↓
IOutputSink
    ↓
files on disk
```

Every filesystem call and search operation runs through **Portfolio.Resilience** policies.

### Resilience Policies

| Policy         | Configuration                                                                                              |
| -------------- | ---------------------------------------------------------------------------------------------------------- |
| `file-read`    | Retry 4× with exponential backoff; circuit opens after 10 transient failures; 3-second per-attempt timeout |
| `path-search`  | Retry 2×; circuit opens after 3 failures; 180-second timeout                                               |
| `bundle-write` | Retry 3×; circuit opens after 5 failures; 60-second timeout                                                |

---

## Repository Layout

```text
file-ferry/
├── .editorconfig
├── .gitignore
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── FileHarvester.slnx
├── README.md
├── CHANGELOG.md
├── LICENSE
├── artifacts/                    Build output (git-ignored)
├── docs/                         Architecture, resilience usage, user guide
├── src/
│   ├── FileHarvester.App/       Avalonia UI shell
│   ├── FileHarvester.Core/      Domain logic + format writers
│   ├── FileHarvester.Platform/  OS-specific search adapters
│   └── FileHarvester.Tests/     xUnit test suite
└── external/                    Reserved for future extension
```

---

## Testing

Run the complete test suite with:

```powershell
dotnet test FileHarvester.slnx
```

Current coverage includes:

- Domain logic
- File resolver
- Content reader
- Bundle composer
- Format writers
- Path search
- Pipeline integration

The coverage baseline is recorded in `docs/architecture.md`.

---

## Roadmap

### v1.0 — Current Release

- Nine output formats
- Single-merged and separate-files modes
- PC-wide search with ambiguity picker
- Light/dark themes
- Diagnostics tab
- Windows single-file executable

### v1.1 — Cloud Integration _(Planned)_

- AWS S3
- Google Cloud Storage
- Azure Blob Storage
- Google Drive
- OAuth credential management
- Cross-provider transfer:
  - PC ↔ Cloud
  - Cloud ↔ Cloud

- See `docs/architecture.md` for the reserved cloud-readiness rules.

### v1.2 — Extended Search and Polish

- Windows Search Index integration for faster PC-wide search
- Search history and saved lists
- Custom skip lists per user

---

## License

MIT License — see [`LICENSE`](LICENSE).

---

## Author

**Alexander Sanchez Cyril**


---

## Screenshot

<p align="center">
  <img
    src="docs/screenshots/Screenshot.png"
    alt="File-Ferry Screenshot"
    width="900"
  >
</p>