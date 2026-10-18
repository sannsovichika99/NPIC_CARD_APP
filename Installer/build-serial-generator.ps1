<#
  Builds the Card Studio Serial Generator (Release) and its single-file installer.

    powershell -ExecutionPolicy Bypass -File Installer\build-serial-generator.ps1
    powershell -ExecutionPolicy Bypass -File Installer\build-serial-generator.ps1 -Version 1.1.0

  Output: <repo>\dist\admin\SerialGenerator-Setup.exe

  For the administrator's PC only: do NOT attach it to a Card Studio release. It does not contain the private key
  (serial-private.key); after installing, open the generator and click Open Key... once.
#>
param(
    [string]$Configuration = "Release",
    # Optional: stamps this version into Tools\SerialGenerator\GeneratorVersion.cs (generator + its setup) before building.
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$setupProject = Join-Path $PSScriptRoot "SerialGenerator.Setup\SerialGenerator.Setup.csproj"
$versionFile = Join-Path $repo "Tools\SerialGenerator\GeneratorVersion.cs"

# Locate MSBuild from any Visual Studio / Build Tools installation.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Install Visual Studio or the Build Tools for Visual Studio." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild.exe not found." }
Write-Host "MSBuild: $msbuild"

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Version must look like 1.2.0 or 1.2.0.1" }
    $full = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }
    $text = [IO.File]::ReadAllText($versionFile)
    $text = $text -replace 'AssemblyVersion\("[\d\.]+"\)', "AssemblyVersion(`"$full`")"
    $text = $text -replace 'AssemblyFileVersion\("[\d\.]+"\)', "AssemblyFileVersion(`"$full`")"
    [IO.File]::WriteAllText($versionFile, $text)
}
$stamped = [regex]::Match([IO.File]::ReadAllText($versionFile), 'AssemblyVersion\("(\d+\.\d+\.\d+)\.(\d+)"\)')
if (-not $stamped.Success) { throw "No AssemblyVersion(""x.y.z.w"") found in $versionFile" }
$productVersion = if ($stamped.Groups[2].Value -eq "0") { $stamped.Groups[1].Value } else { "$($stamped.Groups[1].Value).$($stamped.Groups[2].Value)" }
Write-Host "Version: $productVersion"

# Building the setup project builds the generator first (ProjectReference), then embeds its output.
& $msbuild $setupProject /t:Rebuild /p:Configuration=$Configuration /p:Platform=AnyCPU /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

$setup = Join-Path $repo "dist\admin\SerialGenerator-Setup.exe"
if ($Configuration -eq "Release" -and (Test-Path $setup)) {
    $item = Get-Item $setup
    Write-Host ""
    Write-Host "Installer : $($item.FullName)"
    Write-Host ("Size      : {0:N1} MB" -f ($item.Length / 1MB))
    Write-Host "SHA-256   : $((Get-FileHash $setup -Algorithm SHA256).Hash)"
    Write-Host ""
    Write-Host "Admin tool: keep it out of the Card Studio release. The private key is NOT inside." -ForegroundColor Yellow
}
