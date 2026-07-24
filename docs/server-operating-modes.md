# LOKAL server configuration and operating modes

LOKAL stores its server configuration at `%ProgramData%\LOKAL\config\server.json` on Windows. The teacher dashboard opens on the **Server** page, where the presenter can inspect the active URLs and save a different operating mode.

For installed deployments, the local server runs through the native **LOKAL
Server** Windows service. See [windows-service.md](windows-service.md) for its
lifecycle and installer commands.

## Modes

### Offline

- Listens only on `127.0.0.1`.
- Only the same computer can open the teacher or student pages.
- Does not expose the class to the local network or Internet.

### Local Network

- Listens on `0.0.0.0` and advertises the preferred active LAN address.
- Learners on the same Wi-Fi or wired network can join without Internet access.
- This is the default and preserves LOKAL's existing classroom behavior.

### Online

- Listens on `0.0.0.0` and advertises the configured public URL.
- The public URL must route to this LOKAL server through a reverse proxy, secure tunnel, or hosted deployment.
- HTTPS is recommended because teacher authentication and learner traffic cross the Internet.
- This mode changes reachability only. Optional local-to-cloud database
  synchronization is configured separately with `LOKAL_CLOUD_SYNC_URL` and
  `LOKAL_SYNC_SECRET`; see
  [local-outbox-synchronization.md](local-outbox-synchronization.md).
- Classroom PCs that cannot accept inbound Internet traffic can use the
  authenticated outbound [hybrid WebSocket relay](hybrid-websocket-relay.md)
  instead of exposing their local listener.

## Applying changes

The running listener cannot safely change its bind address or port in place. Saving a different mode, address, port, or public URL marks the configuration as **Restart required**. Close and reopen LOKAL to activate it.

## Environment overrides

Managed and development installations can override the JSON configuration with:

- `LOKAL_SERVER_MODE` (`offline`, `lan`, or `online`)
- `LOKAL_BIND_ADDRESS`
- `PORT`
- `LOKAL_PUBLIC_URL`
- `LOKAL_SERVER_CONFIG` (alternate configuration file path)

The default listener is `0.0.0.0:8080` in Local Network mode.
