# ----------------------------------------------------------------------------------------------------------

# The .git garbage cleanup — one copy-paste block

# RUN THIS SCRIPT ON POWERSHELL (READ/PROBE) - reports only, no changes

# ----------------------------------------------------------------------------------------------------------

Set-Location 'C:\Users\HP\Desktop\file-ferry'

Write-Host '=== BEFORE ==='
git count-objects -vH
$before = (Get-ChildItem '.git' -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
Write-Host ('Total .git size: ' + [math]::Round($before/1GB,2) + ' GB')

# ----------------------------------------------------------------------------------------------------------

# Read that first. If size-garbage is large or the .git folder is far bigger than the object store, run the cleanup:

# ----------------------------------------------------------------------------------------------------------

powershell

# RUN THIS SCRIPT ON POWERSHELL (WRITE) - prunes garbage, history untouched

Set-Location 'C:\Users\HP\Desktop\file-ferry'

Write-Host '=== GARBAGE BEFORE ==='
git count-objects -vH

# Prune unreachable objects and pack loose objects. History is not rewritten.

git gc --prune=now

# Remove orphaned temp object files git does not own (interrupted-write leftovers).

$tmp = Get-ChildItem '.git\objects' -Recurse -Filter 'tmp_obj_*' -File -ErrorAction SilentlyContinue
if ($tmp) {
$tmpSum = ($tmp | Measure-Object Length -Sum).Sum
Write-Host ('Deleting ' + $tmp.Count + ' orphaned temp files: ' + [math]::Round($tmpSum/1GB,2) + ' GB')
$tmp | Remove-Item -Force
} else {
Write-Host 'No orphaned temp files found.'
}

Write-Host '=== AFTER ==='
git count-objects -vH
$after = (Get-ChildItem '.git' -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
Write-Host ('Total .git size: ' + [math]::Round($after/1GB,2) + ' GB')
Write-Host ('Reclaimed: ' + [math]::Round(($before - $after)/1GB,2) + ' GB')

# ----------------------------------------------------------------------------------------------------------

# Run the app from source

# ----------------------------------------------------------------------------------------------------------

Push-Location 'C:\Users\HP\Desktop\file-ferry'
try { # Kill any stuck instance first
Get-Process -Name 'FileHarvester.App','File-Ferry' -ErrorAction SilentlyContinue | Stop-Process -Force

    # Run
    dotnet run --project .\src\FileHarvester.App\FileHarvester.App.csproj

} finally {
Pop-Location
}

# ----------------------------------------------------------------------------------------------------------
# OR USE THIS:
dotnet run --project 'C:\Users\HP\Desktop\file-ferry\src\FileHarvester.App\FileHarvester.App.csproj'
# ----------------------------------------------------------------------------------------------------------