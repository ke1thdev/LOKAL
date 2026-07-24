# LOKAL Windows service

LOKAL can run as a native Windows service named `LOKALServer` (display name
**LOKAL Server**). This keeps the classroom server available without requiring
the teacher to leave a terminal window open.

## Installed behavior

- Starts automatically after Windows boots, using delayed automatic start.
- Runs under the Windows Local System account by default.
- Restarts after 5 seconds, 15 seconds, and 60 seconds on repeated failures.
- Handles Windows Stop and Shutdown requests with a graceful HTTP shutdown.
- Reads configuration from `%ProgramData%\LOKAL\config\server.json`.
- Writes logs to `%ProgramData%\LOKAL\logs\lokal.log`.
- Preserves the database, uploads, configuration, and logs when uninstalled.
- Resolves `web` and `assets` beside the installed `lokal.exe`; it does not rely
  on the service's working directory.

## Commands

Run these commands from an elevated terminal:

```powershell
lokal.exe service install
lokal.exe service status
lokal.exe service stop
lokal.exe service start
lokal.exe service restart
lokal.exe service uninstall
```

`service install` is upgrade-safe: if the service already exists, its binary
path and settings are updated, then the service is restarted with the new
binary. It also starts a newly installed service immediately.

Convenience scripts are available in `scripts/install-windows-service.ps1` and
`scripts/uninstall-windows-service.ps1`.

## Installer integration

The future elevated installer should:

1. Copy `lokal.exe`, `web`, and `assets` into the same Program Files directory.
2. Grant any installer-required access to `%ProgramData%\LOKAL`.
3. Run `lokal.exe service install` after files have been copied.
4. Add a Windows Firewall private-network allow rule for the installed
   `lokal.exe`, so Local Network mode is reachable by students.
5. On uninstall, run `lokal.exe service uninstall` before deleting program
   files. Do not delete `%ProgramData%\LOKAL` unless the user explicitly chooses
   to remove classroom data.

Changing the operating mode, bind address, or port in the teacher Server page
requires `lokal.exe service restart` (or a restart from Windows Services).

