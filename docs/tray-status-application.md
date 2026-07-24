# LOKAL tray and server-status application

`LOKAL.Tray.exe` is the lightweight desktop companion for the native **LOKAL
Server** Windows service. It is intentionally separate from PowerPoint: closing
PowerPoint does not stop the classroom server, and exiting the tray application
does not stop the service.

## User experience

- Left-click or double-click the LOKAL tray icon to open the status window.
- Right-click it for quick access to the teacher dashboard, student join page,
  service controls, logs, and startup preference.
- The status window shows the real Windows-service state, active operating mode,
  listener address, and whether a saved configuration needs a restart.
- **Start**, **Restart**, and **Stop** request Windows administrator approval and
  delegate to `lokal.exe service ...`; the tray never implements a second server.
- **Exit Status App** removes only the tray icon. It does not end a live class.
- Starting a second copy activates the existing status window instead of creating
  duplicate tray icons.

The tray reads `%ProgramData%\LOKAL\config\server.json`, checks
`/api/v1/server/status`, and opens `%ProgramData%\LOKAL\logs\lokal.log`. This keeps
it aligned with the offline, local-network, and online operating modes.

## Build and validation

Run from an ordinary PowerShell prompt:

```powershell
.\scripts\build-tray-app.ps1
.\scripts\test-tray-app.ps1 -SkipBuild
.\scripts\build-tray-msi.ps1 -SkipTrayBuild
.\scripts\test-tray-msi.ps1 -SkipBuild
```

The release artifacts are `artifacts\LOKAL.Tray.exe` and
`artifacts\LOKAL.Server.Status-x64.msi`. The validation commands check
the branded version metadata, development signature, diagnostic mode, service
discovery, and ProgramData paths without leaving a tray process running.

## Installer integration

The branded bootstrapper chains the dedicated tray MSI after the PowerPoint MSI.
It installs the tray under `Program Files\LOKAL`, creates a Start Menu shortcut,
and registers it to start at Windows sign-in. The future local-server MSI owns
`lokal.exe`, web/assets, and the Windows service; the tray discovers that service
through the Service Control Manager even before both executables share a folder.
