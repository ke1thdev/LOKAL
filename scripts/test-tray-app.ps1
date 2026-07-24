[CmdletBinding()]
param(
    [string]$ArtifactPath = 'artifacts\LOKAL.Tray.exe',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedArtifact = [IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactPath))

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-tray-app.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Tray build failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath $resolvedArtifact -PathType Leaf)) {
    throw "Tray artifact not found: $resolvedArtifact"
}

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedArtifact)
if ($version.ProductName -ne 'LOKAL Server Status') {
    throw "Unexpected product name: $($version.ProductName)"
}
if ($version.CompanyName -ne 'Keith Renz D. Romblon and Camille R. Ramilo') {
    throw "Unexpected company name: $($version.CompanyName)"
}

$diagnosticPath = Join-Path ([IO.Path]::GetTempPath()) ("lokal-tray-diagnostic-{0}.json" -f [Guid]::NewGuid())
try {
    $process = Start-Process -FilePath $resolvedArtifact -ArgumentList @('--diagnose', $diagnosticPath) -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Tray diagnostic exited with code $($process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $diagnosticPath)) { throw 'Tray diagnostic did not create its output.' }
    $diagnostic = Get-Content -LiteralPath $diagnosticPath -Raw | ConvertFrom-Json
    if (-not $diagnostic.configPath.EndsWith('\LOKAL\config\server.json')) {
        throw "Unexpected config path: $($diagnostic.configPath)"
    }
    if (-not $diagnostic.logPath.EndsWith('\LOKAL\logs\lokal.log')) {
        throw "Unexpected log path: $($diagnostic.logPath)"
    }
}
finally {
    Remove-Item -LiteralPath $diagnosticPath -Force -ErrorAction SilentlyContinue
}

$signature = Get-AuthenticodeSignature -FilePath $resolvedArtifact
$hash = Get-FileHash -LiteralPath $resolvedArtifact -Algorithm SHA256
Write-Host 'LOKAL tray/status validation succeeded.'
Write-Host "Artifact: $resolvedArtifact"
Write-Host "Product: $($version.ProductName) $($version.ProductVersion)"
Write-Host "Signature: $($signature.Status)"
Write-Host "SHA256: $($hash.Hash)"
Write-Host "Service installed: $($diagnostic.serviceInstalled)"
Write-Host "Service state: $($diagnostic.serviceState)"
Write-Host "Server reachable: $($diagnostic.serverReachable)"
Write-Host "Mode: $($diagnostic.modeLabel)"
