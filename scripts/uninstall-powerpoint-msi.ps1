[CmdletBinding()]
param([string]$MsiPath = 'artifacts\LOKAL.PowerPoint.AddIn-x64.msi')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedMsi = [IO.Path]::GetFullPath((Join-Path $repoRoot $MsiPath))
if (-not (Test-Path $resolvedMsi)) { throw "MSI not found: $resolvedMsi" }
if (Get-Process POWERPNT -ErrorAction SilentlyContinue) { throw 'Close PowerPoint before uninstalling the LOKAL add-in.' }
$log = Join-Path (Split-Path $resolvedMsi -Parent) 'powerpoint-msi-uninstall.log'
$process = Start-Process msiexec.exe -Verb RunAs -ArgumentList @('/x', "`"$resolvedMsi`"", '/L*v', "`"$log`"") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "MSI uninstall failed with code $($process.ExitCode). See $log" }
Write-Host 'LOKAL PowerPoint Add-in uninstalled.'
