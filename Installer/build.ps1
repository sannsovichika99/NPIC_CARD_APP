<#
  Builds Card Studio (Release) and the single-file installer.

    powershell -ExecutionPolicy Bypass -File Installer\build.ps1
    powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.2.0

  Output: <repo>\dist\CardStudio-Setup.exe
#>
param(
    [string]$Configuration = "Release",
    # Optional: stamps this version into ProductInfo.cs before building (shown in Settings > Apps).
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$setupProject = Join-Path $PSScriptRoot "CardStudio.Setup\CardStudio.Setup.csproj"

# Locate MSBuild from any Visual Studio / Build Tools installation.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Install Visual Studio or the Build Tools for Visual Studio." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild.exe not found." }
Write-Host "MSBuild: $msbuild"

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.0" }
    $info = Join-Path $PSScriptRoot "CardStudio.Setup\ProductInfo.cs"
    $text = [IO.File]::ReadAllText($info)
    $text = $text -replace 'AssemblyVersion\("[\d\.]+"\)', "AssemblyVersion(`"$Version.0`")"
    $text = $text -replace 'AssemblyFileVersion\("[\d\.]+"\)', "AssemblyFileVersion(`"$Version.0`")"
    [IO.File]::WriteAllText($info, $text)
    Write-Host "Version: $Version"
}

# Building the setup project builds NPIC_CARD_APP first (ProjectReference), then embeds its output.
& $msbuild $setupProject /t:Rebuild /p:Configuration=$Configuration /p:Platform=AnyCPU /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

$setup = Join-Path $repo "dist\CardStudio-Setup.exe"
if ($Configuration -eq "Release" -and (Test-Path $setup)) {
    $item = Get-Item $setup
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash
    Write-Host ""
    Write-Host "Installer : $($item.FullName)"
    Write-Host ("Size      : {0:N1} MB" -f ($item.Length / 1MB))
    Write-Host "SHA-256   : $hash"
}
