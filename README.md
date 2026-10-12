# File-Ferry

> Harvest files from anywhere on your PC. Copy them byte-for-byte. Deliver them in any format you choose.

File-Ferry is a Windows desktop toolkit for working with local files, cloud storage, text, paths, and PostgreSQL data. Its original file-harvesting workflow is joined by Cloud, SchemaLence, Link Joiner, and Extract workspaces.

Built on **.NET 10**, **Avalonia UI 12**, and **Portfolio.Resilience 0.8.0**.

---

## Workspaces and Features

### Harvest — find, collect, and deliver files

- Add files by pasting paths or names, using the file picker, or choosing a
  folder.
- Resolve bare filenames across the PC; when a name has multiple matches,
  select the files you want with **Newest 5**, **Largest 5**, **Select All**,
  or **Select None** presets.
- Write to nine formats: `.txt`, `.md`, `.json`, `.html`, `.docx`, `.pdf`,
  `.xlsx`, `.csv`, and `.zip`.
- Choose **Separate Files** or **Single Merged** output, customize names, and
  opt into headers, manifests, and other formatting where available.
- Review and confirm before replacing an existing output file.

### Cloud — connect storage and work with remote files

- Configure built-in profiles for AWS S3, Cloudflare R2, Backblaze B2,
  Wasabi, DigitalOcean Spaces, MinIO, Azure Blob Storage, Google Cloud
  Storage, Google Drive, Microsoft OneDrive, and Dropbox.
- Use provider-specific credentials or OAuth setup. Credentials are kept in
  the operating system credential store where supported; the app does not
  save cloud secrets in plain-text profile files.
- Select and download Google Drive files from the Cloud workspace. Google
  Docs, Sheets, and Slides are exported as PDF, XLSX, and PPTX respectively.
- **Testing status:** Google Drive is the only cloud provider whose download
  flow has been tested end-to-end, including single- and multi-file downloads.
  The other providers are not yet verified for the same download workflow;
  testing and completing those flows is planned for a future release.
- See the [user guide](docs/user-guide.md) for setup, permissions, and current
  provider workflow details. Cloud provider capabilities and account setup
  requirements differ.

### SchemaLence — inspect PostgreSQL safely

- Connect to PostgreSQL and browse accessible schemas, tables, and views.
- Inspect table definitions, constraints, indexes, triggers, and supported
  pgvector metadata.
- Preview role-visible rows in a read-only data grid and export table or view
  rows to Excel (`.xlsx`).
- Create a PostgreSQL custom-format backup (`.dump`) with `pg_dump`. Restore
  it with PostgreSQL's `pg_restore`; cluster roles and global settings are
  not included.
- The workspace does not provide arbitrary SQL execution or database
  editing. Backup requires PostgreSQL client tools, including `pg_dump`, on
  the app machine.

### Link Joiner — combine bases and paths

- Join one or more base folders to lists of relative paths in independent
  sections.
- Validate unsafe or ambiguous relative paths, optionally normalize
  separators or remove duplicates, then copy, save, or send results to
  Harvest.

### Extract — find useful text locally

- Extract links, file paths, email addresses, and phone-like strings from
  pasted text.
- Filter, copy, or save matches. Extraction runs locally and does not verify,
  open, or transmit the matched values.

### App-wide tools

- Light/dark appearance, a responsive Avalonia interface, live operation
  logging, and resilience diagnostics.
- Cancellable long-running exports and backups, with temporary-file cleanup
  so partial outputs are not presented as completed files.

---

## Quickstart

### For Users — Download and Run on Windows

1. Download the latest Windows x64 ZIP from
   [GitHub Releases](https://github.com/sancy1/file-ferry/releases/latest).
2. Extract the ZIP, keeping `FileFerry.exe` and the `LatoFont` folder together.
3. Run `FileFerry.exe`; no installer or separately installed .NET runtime is
   required.
4. Choose a workspace from the top bar: **Harvest**, **Cloud**, **Link
   Joiner**, **Extract**, or **SchemaLence**.

> **The published download is a self-contained Windows x64 build.** Cloud
> services may require your own provider account, credentials, or OAuth app
> configuration. SchemaLence connects to a PostgreSQL server you provide; it
> does not include a database server.

### For Developers — Build from Source

```powershell
git clone https://github.com/sancy1/file-ferry.git
cd file-ferry
dotnet restore FileHarvester.slnx
dotnet build FileHarvester.slnx
dotnet run --project src\FileHarvester.App\FileHarvester.App.csproj
```

### Publish a Windows x64 Executable

```powershell
dotnet publish src\FileHarvester.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o artifacts\FileFerry-v1.1.0-win-x64
```

Rename `FileHarvester.App.exe` to `FileFerry.exe` for the release package.
If the runtime package contains `LatoFont`, include it beside the executable.
The published app requires Windows x64; database backup additionally requires
the PostgreSQL client tool `pg_dump` to be installed and available on `PATH`.

The first public release was `v1.0.0`. This repository is preparing the
follow-up `v1.1.0`, which adds the workspaces and capabilities documented
above. Check [GitHub Releases](https://github.com/sancy1/file-ferry/releases)
for the currently published version and its release notes.

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
FileHarvester.App       (Avalonia UI — Harvest, Cloud, and utility workspaces)
FileHarvester.Core      (file workflows, cloud contracts, shared domain logic)
FileHarvester.Platform  (file-system and cloud-provider adapters)
SchemaLence.Core        (database contracts and metadata models)
SchemaLence.Platform    (PostgreSQL provider and local persistence)
FileHarvester.Tests     (xUnit — app, core, and platform behavior)
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

Network and file operations use named **Portfolio.Resilience** policies for
retry, circuit breaking, timeout, and rate limiting where appropriate.
Operations such as database backups have their own cancellation-aware
lifecycle rather than the short timeout used for ordinary local writes.

### Resilience Policies

| Policy                 | Used for                                              |
| ---------------------- | ----------------------------------------------------- |
| `file-read`            | Local file checks and reads                          |
| `path-search`          | PC-wide filename search                              |
| `bundle-write`         | Ordinary local output writes                         |
| `cloud-read` / `write` | Remote object reads and writes                       |
| `cloud-search`         | Remote search and listing                            |
| `cloud-auth`           | OAuth and credential validation                      |
| `credential-store-write` | Secure credential persistence                      |

See [`docs/resilience-usage.md`](docs/resilience-usage.md) for the current
policy configuration.

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
├── artifacts/                    Build output and release packages (git-ignored)
├── docs/                         User, architecture, and verification guides
├── src/
│   ├── FileHarvester.App/        Avalonia UI and workspaces
│   ├── FileHarvester.Core/       File and cloud domain logic
│   ├── FileHarvester.Platform/   File and cloud adapters
│   ├── SchemaLence.Core/         Database contracts
│   ├── SchemaLence.Platform/     PostgreSQL and local store
│   └── FileHarvester.Tests/      xUnit test suite
└── FileHarvester.slnx            Solution
```

---

## Testing

Run the complete test suite with:

```powershell
dotnet test FileHarvester.slnx
```

Current coverage includes:

- Harvest and output behavior
- Cloud providers, credentials, and downloads
- SchemaLence PostgreSQL and Excel export
- Link Joiner and Extract
- UI view models, documentation contracts, and resilience behavior

The tests are in [`src/FileHarvester.Tests/`](src/FileHarvester.Tests/).

---

## Release Notes

The first public release, [`v1.0.0`](https://github.com/sancy1/file-ferry/releases/tag/v1.0.0),
focused on local file harvesting. The in-progress `v1.1.0` update expands
File-Ferry with cloud connections and Google Drive downloads, PostgreSQL
inspection and export, Link Joiner, Extract, overwrite confirmation, and
related reliability and UI improvements.

See [`CHANGELOG.md`](CHANGELOG.md) and the
[release page](https://github.com/sancy1/file-ferry/releases) for the
versioned change history.

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
