[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts',
    [switch]$SkipTrayBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $repoRoot 'installer\tray'
$objectRoot = Join-Path $installerRoot 'obj'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$trayExecutable = Join-Path $outputRoot 'LOKAL.Tray.exe'
$msiPath = Join-Path $outputRoot 'LOKAL.Server.Status-x64.msi'

if (-not $SkipTrayBuild) {
    & (Join-Path $PSScriptRoot 'build-tray-app.ps1') -Configuration $Configuration -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Tray build failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath $trayExecutable -PathType Leaf)) {
    throw "Tray executable not found: $trayExecutable"
}

New-Item -ItemType Directory -Force -Path $objectRoot, $outputRoot | Out-Null
Push-Location $repoRoot
try {
    dotnet tool restore | Out-Host
    & dotnet wix build (Join-Path $installerRoot 'Package.wxs') `
        -arch x64 `
        -d "TrayExecutable=$trayExecutable" `
        -intermediatefolder $objectRoot `
        -pdbtype none `
        -out $msiPath
    if ($LASTEXITCODE -ne 0) { throw "Tray MSI build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }

$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq 'CN=LOKAL Development'
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if ($certificate) {
    $signature = Set-AuthenticodeSignature -FilePath $msiPath -Certificate $certificate -HashAlgorithm SHA256
    if (-not $signature.SignerCertificate) { throw "Tray MSI signing failed: $($signature.StatusMessage)" }
    Write-Host "Signature: $($signature.Status) (development certificate)"
}

$hash = Get-FileHash $msiPath -Algorithm SHA256
Write-Host "Built: $msiPath"
Write-Host "SHA256: $($hash.Hash)"
