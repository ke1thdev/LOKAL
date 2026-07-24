[CmdletBinding()]
param(
    [string]$MsiPath = 'artifacts\LOKAL.Server.Status-x64.msi',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedMsi = [IO.Path]::GetFullPath((Join-Path $repoRoot $MsiPath))
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-tray-msi.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Tray MSI build failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath $resolvedMsi -PathType Leaf)) { throw "Tray MSI not found: $resolvedMsi" }

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($resolvedMsi, 0))
function Read-MsiValue([string]$query) {
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($query))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if (-not $record) { return $null }
    return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
}

$productName = Read-MsiValue "SELECT `Value` FROM `Property` WHERE `Property`='ProductName'"
$manufacturer = Read-MsiValue "SELECT `Value` FROM `Property` WHERE `Property`='Manufacturer'"
$trayFile = Read-MsiValue "SELECT `FileName` FROM `File` WHERE `File`='TrayExecutable'"
$runValue = Read-MsiValue "SELECT `Value` FROM `Registry` WHERE `Name`='LOKAL Server Status'"
if ($productName -ne 'LOKAL Server Status') { throw "Unexpected product name: $productName" }
if ($manufacturer -ne 'Keith Renz D. Romblon and Camille R. Ramilo') { throw "Unexpected manufacturer: $manufacturer" }
if ($trayFile -notmatch 'LOKAL\.Tray\.exe') { throw "Tray payload missing: $trayFile" }
if ($runValue -notmatch 'LOKAL\.Tray\.exe') { throw "Startup registration missing: $runValue" }

$signature = Get-AuthenticodeSignature -FilePath $resolvedMsi
$hash = Get-FileHash -LiteralPath $resolvedMsi -Algorithm SHA256
Write-Host 'LOKAL tray MSI validation succeeded.'
Write-Host "Product: $productName"
Write-Host "Manufacturer: $manufacturer"
Write-Host "Payload: $trayFile"
Write-Host "Startup: $runValue"
Write-Host "Signature: $($signature.Status)"
Write-Host "SHA256: $($hash.Hash)"
