param(
    [string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'lokal.exe')
)

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window (Run as administrator).'
}

$Executable = [System.IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "LOKAL executable not found: $Executable"
}

& $Executable service uninstall
if ($LASTEXITCODE -ne 0) {
    throw "LOKAL service removal failed with exit code $LASTEXITCODE."
}

