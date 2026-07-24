[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'desktop\LOKAL.Tray\LOKAL.Tray.csproj'
$buildOutput = Join-Path $repoRoot "desktop\LOKAL.Tray\bin\$Configuration"
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$artifactPath = Join-Path $artifactRoot 'LOKAL.Tray.exe'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer (vswhere.exe) was not found.'
}
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    throw 'MSBuild was not found. Install the Visual Studio .NET desktop build tools.'
}

& $msbuild $projectPath /t:Rebuild "/p:Configuration=$Configuration" `
    /p:Platform=AnyCPU /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "LOKAL tray build failed with exit code $LASTEXITCODE." }

$builtPath = Join-Path $buildOutput 'LOKAL.Tray.exe'
if (-not (Test-Path -LiteralPath $builtPath -PathType Leaf)) {
    throw "Tray executable was not created: $builtPath"
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Copy-Item -LiteralPath $builtPath -Destination $artifactPath -Force

$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq 'CN=LOKAL Development'
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if ($certificate) {
    $signature = Set-AuthenticodeSignature -FilePath $artifactPath `
        -Certificate $certificate -HashAlgorithm SHA256
    if (-not $signature.SignerCertificate) {
        throw "Tray executable signing failed: $($signature.StatusMessage)"
    }
    Write-Host "Signature: $($signature.Status) (development certificate)"
} else {
    Write-Warning 'LOKAL Development certificate was not found; the tray artifact is unsigned.'
}

$hash = Get-FileHash $artifactPath -Algorithm SHA256
Write-Host "Built: $artifactPath"
Write-Host "SHA256: $($hash.Hash)"
Write-Host 'Authors: Keith Renz D. Romblon and Camille R. Ramilo'
