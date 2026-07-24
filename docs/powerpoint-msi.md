# LOKAL PowerPoint MSI

This package installs the signed LOKAL VSTO add-in for all users of a 64-bit Windows computer. It copies the complete VSTO deployment payload to `Program Files\LOKAL\PowerPoint Add-in` and registers the add-in in both 64-bit and 32-bit Office registry views.

## Build and validate

Run these commands from the repository root in a normal PowerShell window:

```powershell
.\scripts\build-powerpoint-msi.ps1
.\scripts\test-powerpoint-msi.ps1
```

The build produces `artifacts\LOKAL.PowerPoint.AddIn-x64.msi`. The validation checks the MSI File, Component, Registry, and LaunchCondition tables, verifies its embedded Authenticode signature, and performs a non-destructive administrative extraction.

## Install and test in PowerPoint

Close PowerPoint, then run:

```powershell
.\scripts\install-powerpoint-msi.ps1
```

Accept the Windows administrator prompt. The helper backs up and removes the current-user Visual Studio development registration when it points into `bin\Debug` or `bin\Release`; otherwise that HKCU entry would override the per-machine MSI registration.

After installation:

1. Start PowerPoint.
2. Confirm the **LOKAL** ribbon is visible.
3. Open **File > Options > Add-ins** and confirm LOKAL is listed under active application add-ins.
4. Exercise one offline class session and one PowerPoint restart.
5. Close PowerPoint and run `.\scripts\uninstall-powerpoint-msi.ps1` to test clean removal.

## Signing and production release

The current VSTO manifests use the self-signed `LOKAL Development` certificate. The MSI embeds only its public certificate and adds it to the machine Root and Trusted Publishers stores so the signed VSTO deployment can load. The private signing key is never packaged.

Before public distribution, replace the development certificate with an organization/code-signing certificate, re-sign the VSTO manifests and MSI, and remove the development-certificate trust component from `Package.wxs`.
