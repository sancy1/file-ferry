# ============================================================================
# FILE:     scripts/fetch-ffmpeg.ps1
# PURPOSE:  Download the pinned FFmpeg essentials build and install
#           ffmpeg.exe, ffprobe.exe and LICENSE into
#           src\FileHarvester.App\ffmpeg\ so dotnet build/publish carries
#           them automatically (see the csproj copy rule).
# RELATION: Required once per clone — the ffmpeg folder is gitignored
#           (~200 MB exceeds GitHub's per-file limit) and ships inside the
#           release asset instead.
# NOTES:    - URL and SHA-256 are pinned; the hash is verified before
#             extraction, matching the official .sha256 published by
#             gyan.dev. Bump both together when upgrading FFmpeg.
#           - Build: ffmpeg 9.0.2 essentials (GPLv3 static, win64).
#             License text is copied alongside the binaries.
#           - Requires: PowerShell 5.1+, Expand-Archive (built-in).
# ============================================================================

$ErrorActionPreference = 'Stop'

$Url      = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$Sha256   = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DestDir  = Join-Path $RepoRoot 'src\FileHarvester.App\ffmpeg'
$WorkDir  = Join-Path $env:TEMP 'fileferry-ffmpeg-fetch'
$ZipPath  = Join-Path $WorkDir 'ffmpeg-essentials.zip'

Write-Host "Downloading pinned FFmpeg build..."
Write-Host "  $Url"

New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
Invoke-WebRequest -Uri $Url -OutFile $ZipPath -UseBasicParsing

$actual = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $Sha256)
{
    Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue
    throw "SHA-256 mismatch.`n  expected: $Sha256`n  actual:   $actual"
}
Write-Host "SHA-256 verified."

$extract = Join-Path $WorkDir 'extracted'
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $ZipPath -DestinationPath $extract -Force

$pkg = Get-ChildItem $extract -Directory | Select-Object -First 1
$bin = Join-Path $pkg.FullName 'bin'

New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
Copy-Item (Join-Path $bin 'ffmpeg.exe')  $DestDir -Force
Copy-Item (Join-Path $bin 'ffprobe.exe') $DestDir -Force
Copy-Item (Join-Path $pkg.FullName 'LICENSE') $DestDir -Force

Write-Host "Installed to $DestDir"
Write-Host "  ffmpeg.exe, ffprobe.exe, LICENSE"
Write-Host "Next: dotnet publish src\FileHarvester.App\FileHarvester.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"
