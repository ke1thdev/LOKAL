[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts',
    [switch]$SkipAddinBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'addin\LOKAL.PowerPoint\LOKAL.PowerPoint.csproj'
$payloadRoot = Join-Path $repoRoot "addin\LOKAL.PowerPoint\bin\$Configuration"
$installerRoot = Join-Path $repoRoot 'installer\powerpoint'
$objectRoot = Join-Path $installerRoot 'obj'
$payloadSource = Join-Path $objectRoot 'Payload.wxs'
$certificatePath = Join-Path $objectRoot 'LOKAL.Development.cer'
$outputRoot = Join-Path $repoRoot $OutputDirectory
$msiPath = Join-Path $outputRoot 'LOKAL.PowerPoint.AddIn-x64.msi'

function Get-StableGuid([string]$Value) {
    $namespace = [Guid]'89E480A7-1CF9-4BB3-9B46-645439BE6888'
    $namespaceBytes = $namespace.ToByteArray()
    [Array]::Reverse($namespaceBytes, 0, 4)
    [Array]::Reverse($namespaceBytes, 4, 2)
    [Array]::Reverse($namespaceBytes, 6, 2)
    $valueBytes = [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $allBytes = New-Object byte[] ($namespaceBytes.Length + $valueBytes.Length)
    [Array]::Copy($namespaceBytes, 0, $allBytes, 0, $namespaceBytes.Length)
    [Array]::Copy($valueBytes, 0, $allBytes, $namespaceBytes.Length, $valueBytes.Length)
    $hash = [Security.Cryptography.SHA1]::Create().ComputeHash($allBytes)
    [byte[]]$guidBytes = $hash[0..15]
    $guidBytes[6] = ($guidBytes[6] -band 0x0f) -bor 0x50
    $guidBytes[8] = ($guidBytes[8] -band 0x3f) -bor 0x80
    [Array]::Reverse($guidBytes, 0, 4)
    [Array]::Reverse($guidBytes, 4, 2)
    [Array]::Reverse($guidBytes, 6, 2)
    return New-Object System.Guid -ArgumentList (,$guidBytes)
}

function Get-WixId([string]$Prefix, [string]$Value) {
    $bytes = [Security.Cryptography.SHA256]::Create().ComputeHash(
        [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant()))
    return $Prefix + ([BitConverter]::ToString($bytes[0..7]) -replace '-', '')
}

function Get-RelativePath([string]$BasePath, [string]$TargetPath) {
    $baseFullPath = [IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = [Uri]$baseFullPath
    $targetUri = [Uri][IO.Path]::GetFullPath($TargetPath)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

if (-not $SkipAddinBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw 'Visual Studio Installer (vswhere.exe) was not found.'
    }
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) {
        throw 'MSBuild was not found. Install Visual Studio with the Office/SharePoint development workload.'
    }
    & $msbuild $projectPath /t:Rebuild "/p:Configuration=$Configuration" /p:Platform=AnyCPU /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "VSTO build failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path (Join-Path $payloadRoot 'LOKAL.PowerPoint.vsto'))) {
    throw "The VSTO deployment manifest was not found in $payloadRoot."
}

New-Item -ItemType Directory -Force -Path $objectRoot, $outputRoot | Out-Null

$manifestText = Get-Content (Join-Path $payloadRoot 'LOKAL.PowerPoint.vsto') -Raw
$thumbprintMatch = [regex]::Match($manifestText, 'publicKeyToken="([0-9a-f]+)"', 'IgnoreCase')
$manifestCert = [regex]::Match($manifestText, '<publisherIdentity[^>]+name="([^"]+)"', 'IgnoreCase')
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq 'CN=LOKAL Development'
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) {
    throw 'The LOKAL Development manifest-signing certificate was not found in CurrentUser\My.'
}
[IO.File]::WriteAllBytes($certificatePath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))

$excludedExtensions = @('.pdb', '.xml')
$files = Get-ChildItem $payloadRoot -File -Recurse | Where-Object {
    $excludedExtensions -notcontains $_.Extension.ToLowerInvariant()
} | Sort-Object FullName

$directoryMap = @{}
$directoryMap[''] = 'INSTALLFOLDER'
$relativeDirectories = $files | ForEach-Object {
    $relative = Get-RelativePath $payloadRoot $_.FullName
    $parent = Split-Path $relative -Parent
    if ($parent -eq '.') { '' } else { $parent }
} | Where-Object { $_ } | Sort-Object -Unique
foreach ($relativeDirectory in $relativeDirectories) {
    $directoryMap[$relativeDirectory] = Get-WixId 'Dir' $relativeDirectory
}

$builder = [Text.StringBuilder]::new()
[void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
foreach ($relativeDirectory in $relativeDirectories) {
    $segments = $relativeDirectory -split '[\\/]'
    $parentRelative = if ($segments.Count -gt 1) { ($segments[0..($segments.Count - 2)] -join '\') } else { '' }
    $indent = '      '
    if ($parentRelative -eq '') {
        [void]$builder.AppendLine("$indent<Directory Id=`"$($directoryMap[$relativeDirectory])`" Name=`"$($segments[-1])`" />")
    }
}
[void]$builder.AppendLine('    </DirectoryRef>')

# The current payload only has one nested level (sounds). Fail clearly if that changes
# so installer layout cannot silently become incorrect.
if ($relativeDirectories | Where-Object { ($_ -split '[\\/]').Count -gt 1 }) {
    throw 'Nested payload directories deeper than one level need to be added to the MSI generator.'
}

[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <ComponentGroup Id="AddinPayload">')
foreach ($file in $files) {
    $relative = Get-RelativePath $payloadRoot $file.FullName
    $parent = Split-Path $relative -Parent
    if ($parent -eq '.') { $parent = '' }
    $componentId = Get-WixId 'Cmp' $relative
    $fileId = if ($relative -ieq 'LOKAL.PowerPoint.vsto') { 'FileMainVsto' } else { Get-WixId 'File' $relative }
    $guid = Get-StableGuid "lokal-powerpoint-addin/$relative"
    $escapedSource = [Security.SecurityElement]::Escape($file.FullName)
    [void]$builder.AppendLine("      <Component Id=`"$componentId`" Guid=`"{$guid}`" Directory=`"$($directoryMap[$parent])`">")
    [void]$builder.AppendLine("        <File Id=`"$fileId`" Source=`"$escapedSource`" KeyPath=`"yes`" />")
    [void]$builder.AppendLine('      </Component>')
}
[void]$builder.AppendLine('    </ComponentGroup>')
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('</Wix>')
[IO.File]::WriteAllText($payloadSource, $builder.ToString(), [Text.UTF8Encoding]::new($false))

Push-Location $repoRoot
try {
    dotnet tool restore | Out-Host
    & dotnet wix extension add WixToolset.Iis.wixext/5.0.2 | Out-Host
    & dotnet wix build `
        (Join-Path $installerRoot 'Package.wxs') `
        $payloadSource `
        -arch x64 `
        -ext WixToolset.Iis.wixext `
        -d "CertificatePath=$certificatePath" `
        -intermediatefolder $objectRoot `
        -pdbtype none `
        -out $msiPath
    if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

$signature = Set-AuthenticodeSignature -FilePath $msiPath -Certificate $certificate -HashAlgorithm SHA256
if (-not $signature.SignerCertificate) {
    throw "The MSI could not be signed: $($signature.StatusMessage)"
}

$hash = Get-FileHash $msiPath -Algorithm SHA256
Write-Host "Built: $msiPath"
Write-Host "SHA256: $($hash.Hash)"
Write-Host "Payload files: $($files.Count)"
Write-Host "Manifest certificate: $($certificate.Thumbprint) ($($certificate.Subject))"
Write-Host "MSI signature: $($signature.Status) (development certificate)"
