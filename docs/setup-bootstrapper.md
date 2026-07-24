# LOKAL branded setup bootstrapper

`LOKAL-Setup-x64.exe` is the public-facing installer shell for LOKAL. It uses
the WiX Burn engine and WiX Standard Bootstrapper Application so the install,
upgrade, repair, and uninstall flow is consistent and rollback-aware.

## Branding and license

- Product name: **LOKAL Setup**
- Authors/developers: **Keith Renz D. Romblon** and **Camille R. Ramilo**
- Branding: the LOKAL icon and 512-pixel logo from `assets`
- License: a mandatory RTF license page shown before installation
- License source: `installer/bootstrapper/EULA.txt`
- Installer license: `installer/bootstrapper/EULA.rtf`

The agreement identifies LOKAL as an independent academic thesis project and
covers classroom data, local/hybrid networking, third-party dependencies,
academic-development status, warranty, and liability. The text should receive
the thesis adviser or institution's legal/ethics review before public release.

## Build

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-setup-bootstrapper.ps1
```

The build rebuilds the signed PowerPoint MSI and the signed LOKAL Server Status
tray MSI, embeds both into the Burn bundle, signs the detached Burn engine,
reattaches it, and signs the complete bundle. To reuse an already validated
PowerPoint MSI:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-setup-bootstrapper.ps1 `
  -SkipPowerPointMsiBuild
```

Output: `artifacts\LOKAL-Setup-x64.exe`

## Validate without installing

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-setup-bootstrapper.ps1
```

The test extracts the Burn containers, verifies that both embedded MSI packages
are byte-for-byte identical to their validated source packages, checks bundle metadata,
checks both developer names in the EULA, confirms the logo and RTF license are
embedded, and checks the Authenticode signer.

## Install and uninstall

Close PowerPoint, right-click `LOKAL-Setup-x64.exe`, and choose **Run as
administrator**. Accept the EULA and select **Install**. The same setup
executable supports repair and uninstall after installation. Windows Apps &
features also lists the chained LOKAL PowerPoint Add-in and LOKAL Server Status
MSI packages. The tray launches automatically at the next Windows sign-in and
is also available from **Start > LOKAL > LOKAL Server Status**.

## Current scope and next packaging step

This phase embeds the tested PowerPoint and tray/status MSI packages. The native LOKAL server is already
capable of running as `LOKALServer`, but it does not yet have a Program Files
MSI that safely copies `lokal.exe`, `web`, and `assets`, installs the service,
configures the private-network firewall rule, and rolls those changes back.
`installer/bootstrapper/Bundle.wxs` contains the intentional chain point for
that package. Do not chain the repository `lokal.exe` directly.

## Release signing

The current artifact is signed with the self-signed `LOKAL Development`
certificate. Its signature can report `UnknownError` on machines that do not
trust that certificate. Before distribution, replace both VSTO/MSI and bundle
signing with a trusted organization code-signing certificate and timestamp the
signatures. Never include a private signing key in the installer or repository.
