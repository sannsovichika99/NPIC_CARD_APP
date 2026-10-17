<#
.SYNOPSIS
    Makes Card Studio serial numbers (one per PC). For the administrator only: never ship this file's key.

.DESCRIPTION
    Card Studio asks for a serial number when it opens. The activation window shows the PC's Machine ID;
    this script turns that Machine ID into the serial number for that PC.

    The serial is signed with the private key in Installer\serial-private.key (git-ignored, keep a backup!).
    The app only holds the matching public key (NPIC_CARD_APP\Services\LicenseService.cs).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Installer\serial.ps1 -MachineId 7K2Q-M9XD-4TRB-0HCA -Name "Office PC 1"
    Prints the serial for that PC, copies it to the clipboard and adds a line to Installer\serials.csv.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Installer\serial.ps1 -NewKey
    First-time setup: makes a new key pair, saves the private key and writes the public key into LicenseService.cs.
    Rebuild the app afterwards. Every serial made with an older key stops working.
#>
[CmdletBinding(DefaultParameterSetName = "Serial")]
param(
    [Parameter(ParameterSetName = "Serial", Mandatory = $true, Position = 0)]
    [string]$MachineId,

    # Who / which PC the serial is for; only written to serials.csv.
    [Parameter(ParameterSetName = "Serial")]
    [string]$Name = "",

    [Parameter(ParameterSetName = "NewKey", Mandatory = $true)]
    [switch]$NewKey,

    # Replace an existing private key (all serials made with it stop working).
    [Parameter(ParameterSetName = "NewKey")]
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Core

# Must match LicenseService.cs exactly.
$Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
$FormatVersion = "1"
$SignedPrefix = "CardStudio-Serial-v1|"

$keyFile = Join-Path $PSScriptRoot "serial-private.key"
$logFile = Join-Path $PSScriptRoot "serials.csv"
$sourceFile = Join-Path (Split-Path $PSScriptRoot -Parent) "NPIC_CARD_APP\Services\LicenseService.cs"

function Normalize([string]$text) {
    $sb = New-Object Text.StringBuilder
    foreach ($ch in $text.ToCharArray()) {
        $c = [char]::ToUpperInvariant($ch)
        if ($c -eq 'O') { $c = '0' } elseif ($c -eq 'I' -or $c -eq 'L') { $c = '1' }
        if ($Alphabet.IndexOf($c) -ge 0) { [void]$sb.Append($c) }
        elseif (-not [char]::IsWhiteSpace($c) -and $c -ne '-') { [void]$sb.Append('?') }
    }
    $sb.ToString()
}

function ToBase32([byte[]]$data) {
    $sb = New-Object Text.StringBuilder
    $buffer = 0; $bits = 0
    foreach ($b in $data) {
        $buffer = ($buffer -shl 8) -bor $b
        $bits += 8
        while ($bits -ge 5) {
            [void]$sb.Append($Alphabet[($buffer -shr ($bits - 5)) -band 31])
            $bits -= 5
        }
        $buffer = $buffer -band ((1 -shl $bits) - 1)
    }
    if ($bits -gt 0) { [void]$sb.Append($Alphabet[($buffer -shl (5 - $bits)) -band 31]) }
    $sb.ToString()
}

function CheckChar([string]$body) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $Alphabet[$sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($body))[0] % 32] } finally { $sha.Dispose() }
}

function Join-Groups([string]$text, [int]$size) {
    $parts = for ($i = 0; $i -lt $text.Length; $i += $size) { $text.Substring($i, [Math]::Min($size, $text.Length - $i)) }
    $parts -join "-"
}

# ---------------------------------------------------------------- New key pair
if ($NewKey) {
    if ((Test-Path $keyFile) -and -not $Force) {
        throw "$keyFile already exists. Use -Force to replace it: every serial made with the old key will stop working."
    }
    $p = New-Object Security.Cryptography.CngKeyCreationParameters
    $p.ExportPolicy = [Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
    $key = [Security.Cryptography.CngKey]::Create([Security.Cryptography.CngAlgorithm]::ECDsaP256, $null, $p)
    try {
        $private = [Convert]::ToBase64String($key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob))
        $public = [Convert]::ToBase64String($key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob))
    } finally { $key.Dispose() }

    [IO.File]::WriteAllText($keyFile, $private + [Environment]::NewLine)

    $text = [IO.File]::ReadAllText($sourceFile)
    $pattern = '((?:private|internal) const string PublicKey = ")[^"]*(";)'
    if ($text -notmatch $pattern) { throw "No PublicKey constant found in $sourceFile" }
    $text = [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $public + $m.Groups[2].Value })
    [IO.File]::WriteAllText($sourceFile, $text)

    Write-Host "Private key saved:  $keyFile" -ForegroundColor Green
    Write-Host "Public key written: $sourceFile"
    Write-Host ""
    Write-Host "Keep a backup of serial-private.key somewhere safe and NEVER commit it." -ForegroundColor Yellow
    Write-Host "Without it no new serials can be made for this build. Rebuild the app now."
    return
}

# ---------------------------------------------------------------- Serial for one PC
if (-not (Test-Path $keyFile)) {
    throw "No private key at $keyFile. Copy your backup there, or run with -NewKey for first-time setup."
}

$id = Normalize $MachineId
if ($id.Length -ne 16 -or $id.Contains("?")) {
    throw "'$MachineId' is not a Machine ID. It has 16 letters and digits, like 7K2Q-M9XD-4TRB-0HCA (shown in Card Studio's activation window)."
}

$blob = [Convert]::FromBase64String((Get-Content $keyFile -Raw).Trim())
$key = [Security.Cryptography.CngKey]::Import($blob, [Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)
$ecdsa = [Security.Cryptography.ECDsaCng]::new($key)
try {
    $data = [Text.Encoding]::UTF8.GetBytes($SignedPrefix + $id)
    $signature = $ecdsa.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256)
    if (-not $ecdsa.VerifyData($data, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)) { throw "Signature check failed." }
} finally { $ecdsa.Dispose(); $key.Dispose() }

$body = $FormatVersion + (ToBase32 $signature)
$serial = Join-Groups ($body + (CheckChar $body)) 5

[pscustomobject]@{
    Date      = (Get-Date).ToString("yyyy-MM-dd HH:mm")
    MachineId = Join-Groups $id 4
    Name      = $Name
    Serial    = $serial
} | Export-Csv -Path $logFile -Append -NoTypeInformation -Encoding UTF8

try { Set-Clipboard -Value $serial; $copied = " (copied to the clipboard)" } catch { $copied = "" }

Write-Host ""
Write-Host "Machine ID: $(Join-Groups $id 4)$(if ($Name) { "  - $Name" })"
Write-Host "Serial number$copied`:" -ForegroundColor Green
Write-Host $serial
Write-Host ""
Write-Host "Logged to $logFile"
