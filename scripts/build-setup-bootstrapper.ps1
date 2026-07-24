[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts',
    [switch]$SkipPowerPointMsiBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$bootstrapperRoot = Join-Path $repoRoot 'installer\bootstrapper'
$objectRoot = Join-Path $bootstrapperRoot 'obj'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$powerPointMsi = Join-Path $outputRoot 'LOKAL.PowerPoint.AddIn-x64.msi'
$trayMsi = Join-Path $outputRoot 'LOKAL.Server.Status-x64.msi'
$unsignedBundle = Join-Path $objectRoot 'LOKAL-Setup-x64.unsigned.exe'
$detachedEngine = Join-Path $objectRoot 'LOKAL-Setup.engine.exe'
$signedEngine = Join-Path $objectRoot 'LOKAL-Setup.engine.signed.exe'
$bundlePath = Join-Path $outputRoot 'LOKAL-Setup-x64.exe'
$licenseFile = Join-Path $bootstrapperRoot 'EULA.rtf'
$logoFile = Join-Path $repoRoot 'assets\android-chrome-512x512.png'
$iconFile = Join-Path $repoRoot 'assets\favicon.ico'

if (-not $SkipPowerPointMsiBuild) {
    & (Join-Path $PSScriptRoot 'build-powerpoint-msi.ps1') `
        -Configuration $Configuration `
        -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "PowerPoint MSI build failed with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'build-tray-msi.ps1') `
    -Configuration $Configuration `
    -OutputDirectory $OutputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Tray status MSI build failed with exit code $LASTEXITCODE."
}

foreach ($requiredFile in @($powerPointMsi, $trayMsi, $licenseFile, $logoFile, $iconFile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required bootstrapper input not found: $requiredFile"
    }
}

$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq 'CN=LOKAL Development'
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) {
    throw 'The LOKAL Development signing certificate was not found in CurrentUser\My.'
}

New-Item -ItemType Directory -Force -Path $objectRoot, $outputRoot | Out-Null
Remove-Item -LiteralPath $unsignedBundle, $detachedEngine, $signedEngine, $bundlePath `
    -Force -ErrorAction SilentlyContinue

Push-Location $repoRoot
try {
    dotnet tool restore | Out-Host
    & dotnet wix extension add WixToolset.BootstrapperApplications.wixext/5.0.2 | Out-Host
    & dotnet wix build `
        (Join-Path $bootstrapperRoot 'Bundle.wxs') `
        -arch x64 `
        -ext WixToolset.BootstrapperApplications.wixext `
        -d "PowerPointMsi=$powerPointMsi" `
        -d "TrayMsi=$trayMsi" `
        -d "LicenseFile=$licenseFile" `
        -d "LokalLogo=$logoFile" `
        -d "LokalIcon=$iconFile" `
        -intermediatefolder $objectRoot `
        -pdbtype none `
        -out $unsignedBundle
    if ($LASTEXITCODE -ne 0) {
        throw "WiX bootstrapper build failed with exit code $LASTEXITCODE."
    }

    # Burn bundles have two signatures: the detached engine and the final
    # reattached bundle. Signing both preserves verification during elevation.
    & dotnet wix burn detach $unsignedBundle -engine $detachedEngine `
        -intermediateFolder $objectRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Burn engine detach failed with exit code $LASTEXITCODE."
    }

    Copy-Item -LiteralPath $detachedEngine -Destination $signedEngine -Force
    $engineSignature = Set-AuthenticodeSignature -FilePath $signedEngine `
        -Certificate $certificate -HashAlgorithm SHA256
    if (-not $engineSignature.SignerCertificate) {
        throw "The Burn engine could not be signed: $($engineSignature.StatusMessage)"
    }

    & dotnet wix burn reattach $unsignedBundle -engine $signedEngine `
        -intermediateFolder $objectRoot -out $bundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "Burn engine reattach failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$bundleSignature = Set-AuthenticodeSignature -FilePath $bundlePath `
    -Certificate $certificate -HashAlgorithm SHA256
if (-not $bundleSignature.SignerCertificate) {
    throw "The setup bundle could not be signed: $($bundleSignature.StatusMessage)"
}

$hash = Get-FileHash $bundlePath -Algorithm SHA256
$msiHash = Get-FileHash $powerPointMsi -Algorithm SHA256
$trayMsiHash = Get-FileHash $trayMsi -Algorithm SHA256
Write-Host "Built: $bundlePath"
Write-Host "SHA256: $($hash.Hash)"
Write-Host "Embedded PowerPoint MSI SHA256: $($msiHash.Hash)"
Write-Host "Embedded tray MSI SHA256: $($trayMsiHash.Hash)"
Write-Host "Authors: Keith Renz D. Romblon and Camille R. Ramilo"
Write-Host "Bundle signature: $($bundleSignature.Status) (development certificate)"
