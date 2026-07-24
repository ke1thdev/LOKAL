[CmdletBinding()]
param(
    [string]$MsiPath = 'artifacts\LOKAL.PowerPoint.AddIn-x64.msi',
    [string]$ExtractionDirectory = 'artifacts\msi-extracted'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedMsi = [IO.Path]::GetFullPath((Join-Path $repoRoot $MsiPath))
$extractRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $ExtractionDirectory))
$logPath = Join-Path (Split-Path $resolvedMsi -Parent) 'powerpoint-msi-administrative-install.log'

if (-not (Test-Path $resolvedMsi)) { throw "MSI not found: $resolvedMsi" }

function Read-MsiTable([string]$Query, [string[]]$Columns) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($resolvedMsi, 0)
    $view = $database.OpenView($Query)
    $view.Execute()
    $rows = @()
    while ($record = $view.Fetch()) {
        $row = [ordered]@{}
        for ($index = 1; $index -le $Columns.Count; $index++) {
            $row[$Columns[$index - 1]] = $record.StringData($index)
        }
        $rows += [pscustomobject]$row
    }
    $view.Close()
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
    return $rows
}

$registryRows = Read-MsiTable 'SELECT `Registry`,`Root`,`Key`,`Name`,`Value`,`Component_` FROM `Registry`' @('Registry','Root','Key','Name','Value','Component')
$fileRows = Read-MsiTable 'SELECT `File`,`FileName`,`Component_` FROM `File`' @('File','FileName','Component')
$launchRows = Read-MsiTable 'SELECT `Condition`,`Description` FROM `LaunchCondition`' @('Condition','Description')
$componentRows = Read-MsiTable 'SELECT `Component`,`Attributes` FROM `Component`' @('Component','Attributes')

$manifestRows = $registryRows | Where-Object { $_.Name -eq 'Manifest' -and $_.Value -like '*|vstolocal' }
$loadBehaviorRows = $registryRows | Where-Object { $_.Name -eq 'LoadBehavior' -and $_.Value -eq '#3' }
if ($manifestRows.Count -ne 2) { throw "Expected two Office Manifest registrations; found $($manifestRows.Count)." }
if ($loadBehaviorRows.Count -ne 2) { throw "Expected two LoadBehavior=3 registrations; found $($loadBehaviorRows.Count)." }
if (-not ($fileRows | Where-Object { $_.File -eq 'FileMainVsto' })) { throw 'The VSTO deployment manifest is missing from the MSI File table.' }
if ($launchRows.Count -lt 3) { throw 'Expected .NET, PowerPoint, and VSTO prerequisite launch conditions.' }
$registration64 = $componentRows | Where-Object Component -eq 'PowerPointRegistration64'
$registration32 = $componentRows | Where-Object Component -eq 'PowerPointRegistration32'
if (-not $registration64 -or (([int]$registration64.Attributes -band 256) -eq 0)) {
    throw 'The 64-bit Office registration component is not marked as a 64-bit component.'
}
if (-not $registration32 -or (([int]$registration32.Attributes -band 256) -ne 0)) {
    throw 'The 32-bit Office registration component is not marked as a 32-bit component.'
}
$signature = Get-AuthenticodeSignature $resolvedMsi
if (-not $signature.SignerCertificate) { throw 'The MSI does not contain an Authenticode signature.' }

if (Test-Path $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
$process = Start-Process msiexec.exe -ArgumentList @(
    '/a', "`"$resolvedMsi`"", '/qn', "TARGETDIR=`"$extractRoot`"", '/L*v', "`"$logPath`""
) -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Administrative MSI extraction failed with code $($process.ExitCode). See $logPath" }

$extractedManifest = Get-ChildItem $extractRoot -Filter 'LOKAL.PowerPoint.vsto' -Recurse | Select-Object -First 1
if (-not $extractedManifest) { throw 'Administrative extraction completed but the VSTO manifest was not extracted.' }
$extractedDll = Get-ChildItem $extractRoot -Filter 'LOKAL.PowerPoint.dll' -Recurse | Select-Object -First 1
if (-not $extractedDll) { throw 'Administrative extraction completed but the add-in assembly was not extracted.' }

$devRegistration = Get-ItemProperty 'HKCU:\Software\Microsoft\Office\PowerPoint\Addins\LOKAL.PowerPoint' -ErrorAction SilentlyContinue
Write-Host 'MSI validation passed.'
Write-Host "Files in MSI table: $($fileRows.Count)"
Write-Host "Office registration views: $($manifestRows.Count)"
Write-Host "Extracted manifest: $($extractedManifest.FullName)"
Write-Host "MSI signer: $($signature.SignerCertificate.Subject)"
if ($devRegistration -and $devRegistration.Manifest -match '\\bin\\(Debug|Release)\\') {
    Write-Warning 'A per-user development registration is present and will override the MSI registration for this Windows account. Use install-powerpoint-msi.ps1 to back it up and remove it before testing PowerPoint.'
}
