<#
  Builds Card Studio (Release) and the single-file installer.

    powershell -ExecutionPolicy Bypass -File Installer\build.ps1
    powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.2.0
    powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.2.0 -Notes "Faster printing.`nNew RFID page."
    powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.2.0 -NotesFile release-notes.txt

  Output: <repo>\dist\CardStudio-Setup.exe
          <repo>\dist\update.json        (version, SHA-256 and notes, read by the app's Check for Updates)
  To publish an update, copy BOTH files to the update folder / web address the app checks.
#>
param(
    [string]$Configuration = "Release",
    # Optional: stamps this version into <repo>\Version.cs (app + setup) before building.
    [string]$Version,
    # Optional: release notes shown by Check for Updates ("What's new").
    [string]$Notes,
    [string]$NotesFile
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$setupProject = Join-Path $PSScriptRoot "CardStudio.Setup\CardStudio.Setup.csproj"
$versionFile = Join-Path $repo "Version.cs"

# Locate MSBuild from any Visual Studio / Build Tools installation.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Install Visual Studio or the Build Tools for Visual Studio." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild.exe not found." }
Write-Host "MSBuild: $msbuild"

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.0" }
    $text = [IO.File]::ReadAllText($versionFile)
    $text = $text -replace 'AssemblyVersion\("[\d\.]+"\)', "AssemblyVersion(`"$Version.0`")"
    $text = $text -replace 'AssemblyFileVersion\("[\d\.]+"\)', "AssemblyFileVersion(`"$Version.0`")"
    [IO.File]::WriteAllText($versionFile, $text)
}
$stamped = [regex]::Match([IO.File]::ReadAllText($versionFile), 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)')
if (-not $stamped.Success) { throw "No AssemblyVersion(""x.y.z.0"") found in $versionFile" }
$productVersion = $stamped.Groups[1].Value
Write-Host "Version: $productVersion"

if ($NotesFile) { $Notes = [IO.File]::ReadAllText((Resolve-Path $NotesFile)) }

# Building the setup project builds NPIC_CARD_APP first (ProjectReference), then embeds its output.
& $msbuild $setupProject /t:Rebuild /p:Configuration=$Configuration /p:Platform=AnyCPU /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

$setup = Join-Path $repo "dist\CardStudio-Setup.exe"
if ($Configuration -eq "Release" -and (Test-Path $setup)) {
    $item = Get-Item $setup
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash

    # update.json next to the setup: "url" is relative, so the two files can be copied anywhere together.
    $manifest = [ordered]@{
        version = $productVersion
        url     = $item.Name
        sha256  = $hash
        size    = $item.Length
        date    = (Get-Date).ToString("yyyy-MM-dd")
        notes   = if ($Notes) { $Notes.Trim() } else { "" }
    }
    $manifestPath = Join-Path $repo "dist\update.json"
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

    Write-Host ""
    Write-Host "Installer : $($item.FullName)"
    Write-Host ("Size      : {0:N1} MB" -f ($item.Length / 1MB))
    Write-Host "SHA-256   : $hash"
    Write-Host "Update    : $manifestPath"
}
