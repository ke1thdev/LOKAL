# LOKAL

LOKAL is an offline-first, hybrid classroom response and engagement system for
Microsoft PowerPoint. It combines a Windows local server, a PowerPoint add-in,
teacher and student web applications, classroom activities, live responses,
leaderboards, name picking, star levels, device registration, synchronization,
and a hybrid WebSocket relay.

This academic thesis project was developed by:

- Keith Renz D. Romblon
- Camille R. Ramilo

## Main components

- Go local server and API
- SQLite offline storage and PostgreSQL cloud storage
- Local outbox synchronization and hybrid WebSocket relay
- Teacher and student web applications
- Microsoft PowerPoint VSTO add-in
- Windows tray/status application
- WiX MSI and branded bootstrapper sources

## Repository safety

This repository intentionally excludes:

- Runtime databases and classroom data
- Uploaded slides, avatars, and other user content
- Environment files and credentials
- Signing certificates and private keys
- Compiled executables, MSIs, installer bundles, logs, and build output

Use the example files in [`config`](config) as configuration templates. Never
commit production credentials or copy a real environment file into Git.

## Development

The Go server can be verified with:

```powershell
go test ./...
go build .
```

PowerPoint, tray application, MSI, and bootstrapper build scripts are under
[`scripts`](scripts). Windows installers are generated locally under
`artifacts/` and should be published as versioned GitHub Release assets rather
than committed to the source history.

## License

Installation and distribution terms are provided in the branded installer EULA
under [`installer/bootstrapper`](installer/bootstrapper). This repository is an
academic thesis project; its presence on GitHub does not grant rights beyond
the terms stated by the authors.

