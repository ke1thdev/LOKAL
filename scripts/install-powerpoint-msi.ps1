[CmdletBinding()]
param([string]$MsiPath = 'artifacts\LOKAL.PowerPoint.AddIn-x64.msi')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedMsi = [IO.Path]::GetFullPath((Join-Path $repoRoot $MsiPath))
if (-not (Test-Path $resolvedMsi)) { throw "MSI not found: $resolvedMsi" }
if (Get-Process POWERPNT -ErrorAction SilentlyContinue) {
    throw 'Close PowerPoint before installing or upgrading the LOKAL add-in.'
}

$devKey = 'HKCU\Software\Microsoft\Office\PowerPoint\Addins\LOKAL.PowerPoint'
$devKeyPs = 'HKCU:\Software\Microsoft\Office\PowerPoint\Addins\LOKAL.PowerPoint'
$devRegistration = Get-ItemProperty $devKeyPs -ErrorAction SilentlyContinue
if ($devRegistration -and $devRegistration.Manifest -match '\\bin\\(Debug|Release)\\') {
    $backup = Join-Path (Split-Path $resolvedMsi -Parent) 'LOKAL.PowerPoint-development-registration.reg'
    & reg.exe export $devKey $backup /y | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not back up the per-user development registration.' }
    Remove-Item $devKeyPs -Recurse -Force
    Write-Host "Backed up and removed the development registration: $backup"
}

$log = Join-Path (Split-Path $resolvedMsi -Parent) 'powerpoint-msi-install.log'
$process = Start-Process msiexec.exe -Verb RunAs -ArgumentList @('/i', "`"$resolvedMsi`"", '/L*v', "`"$log`"") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "MSI installation failed with code $($process.ExitCode). See $log" }
Write-Host 'LOKAL PowerPoint Add-in installed. Start PowerPoint and confirm LOKAL appears on the ribbon.'
