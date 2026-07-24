[CmdletBinding()]
param(
    [string]$Bundle = 'artifacts\LOKAL-Setup-x64.exe',
    [string]$ExpectedMsi = 'artifacts\LOKAL.PowerPoint.AddIn-x64.msi',
    [string]$ExpectedTrayMsi = 'artifacts\LOKAL.Server.Status-x64.msi',
    [string]$ExtractionDirectory = 'artifacts\setup-extracted'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$bundlePath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Bundle))
$expectedMsiPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $ExpectedMsi))
$expectedTrayMsiPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $ExpectedTrayMsi))
$extractionRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $ExtractionDirectory))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$packageOutput = Join-Path $extractionRoot 'packages'
$baOutput = Join-Path $extractionRoot 'bootstrapper-application'
$licenseText = Join-Path $repoRoot 'installer\bootstrapper\EULA.txt'
$licenseRtf = Join-Path $repoRoot 'installer\bootstrapper\EULA.rtf'

foreach ($requiredFile in @($bundlePath, $expectedMsiPath, $expectedTrayMsiPath, $licenseText, $licenseRtf)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required setup validation input not found: $requiredFile"
    }
}

if (-not $extractionRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Extraction directory must remain inside $artifactRoot."
}
if (Test-Path -LiteralPath $extractionRoot) {
    Remove-Item -LiteralPath $extractionRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageOutput, $baOutput | Out-Null

Push-Location $repoRoot
try {
    dotnet tool restore | Out-Host
    & dotnet wix burn extract $bundlePath -out $packageOutput -outba $baOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Burn bundle extraction failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$extractedMsis = @(Get-ChildItem $packageOutput -Recurse -File -Filter '*.msi')
if ($extractedMsis.Count -ne 2) {
    throw "The setup bundle should contain exactly two MSI payloads; found $($extractedMsis.Count)."
}
$expectedMsiHashes = @{}
foreach ($expectedPath in @($expectedMsiPath, $expectedTrayMsiPath)) {
    $hash = (Get-FileHash $expectedPath -Algorithm SHA256).Hash
    $expectedMsiHashes[$hash] = $expectedPath
}
foreach ($extractedMsi in $extractedMsis) {
    $extractedHash = (Get-FileHash $extractedMsi.FullName -Algorithm SHA256).Hash
    if (-not $expectedMsiHashes.ContainsKey($extractedHash)) {
        throw "Unexpected embedded MSI payload: $($extractedMsi.FullName) ($extractedHash)."
    }
    $expectedMsiHashes.Remove($extractedHash)
}
if ($expectedMsiHashes.Count -ne 0) {
    throw "One or more validated MSI packages were not embedded in the setup bundle."
}

$eula = Get-Content $licenseText -Raw
foreach ($requiredAttribution in @('Keith Renz D. Romblon', 'Camille R. Ramilo',
        'academic thesis software project')) {
    if ($eula -notmatch [regex]::Escape($requiredAttribution)) {
        throw "EULA attribution is missing: $requiredAttribution"
    }
}
$rtf = Get-Content $licenseRtf -Raw
if (-not $rtf.StartsWith('{\rtf1') -or $rtf -notmatch 'Keith Renz D\. Romblon' -or
        $rtf -notmatch 'Camille R\. Ramilo') {
    throw 'The installer RTF license is invalid or missing developer attribution.'
}

$signature = Get-AuthenticodeSignature $bundlePath
if (-not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -ne 'CN=LOKAL Development') {
    throw 'The setup executable is not signed with the expected development certificate.'
}

$versionInfo = (Get-Item $bundlePath).VersionInfo
if ($versionInfo.ProductName -ne 'LOKAL Setup') {
    throw "Unexpected setup product name: $($versionInfo.ProductName)"
}
if ($versionInfo.CompanyName -ne 'Keith Renz D. Romblon and Camille R. Ramilo') {
    throw "Unexpected setup company metadata: $($versionInfo.CompanyName)"
}

$logoPayload = Get-ChildItem $baOutput -Recurse -File |
    Where-Object { $_.Name -ieq 'logo.png' } |
    Select-Object -First 1
$licensePayload = Get-ChildItem $baOutput -Recurse -File |
    Where-Object { $_.Extension -ieq '.rtf' } |
    Select-Object -First 1
if (-not $logoPayload -or -not $licensePayload) {
    throw 'The bootstrapper application container is missing its LOKAL logo or RTF EULA.'
}

Write-Host "Validated: $bundlePath"
Write-Host "Product: $($versionInfo.ProductName) $($versionInfo.ProductVersion)"
Write-Host "Authors: $($versionInfo.CompanyName)"
foreach ($extractedMsi in $extractedMsis) {
    Write-Host "Embedded MSI: $($extractedMsi.FullName)"
    Write-Host "Embedded MSI SHA256: $((Get-FileHash $extractedMsi.FullName -Algorithm SHA256).Hash)"
}
Write-Host "Embedded branding: $($logoPayload.Name), $($licensePayload.Name)"
Write-Host "Signature: $($signature.Status) ($($signature.SignerCertificate.Subject))"
