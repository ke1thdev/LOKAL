# Database provider abstraction

LOKAL's repository layer no longer opens SQLite or performs SQLite migrations
directly. `internal/database.Provider` now owns the SQL driver name, connection
string, placeholder binding, migrations, generated-ID behavior, and string
aggregation syntax.

## Shipped providers

The installed/offline mode ships with the `sqlite` provider. It stores data at
the runtime path documented in `runtime-storage.md`, enables foreign keys and
WAL mode, and performs additive schema upgrades without swallowing migration
errors.

The online/server mode ships with the `postgres` provider (the `postgresql`
alias is also accepted). It uses pgx through Go's `database/sql`, PostgreSQL
`JSONB`, native booleans and timestamps, `INSERT ... RETURNING`, transaction-
scoped migration locking, and cloud-oriented connection pooling. Startup
creates or upgrades the schema before accepting requests.

`database.New(path)` remains available and is intentionally equivalent to:

```go
database.Open(database.Config{Provider: "sqlite", Path: path})
```

## Runtime configuration

- `LOKAL_DB_PROVIDER` selects a registered provider and defaults to `sqlite`.
- `LOKAL_DB_DSN` supplies a provider connection string. Treat it as a secret;
  LOKAL never writes it to the application log.
- `LOKAL_DB_PATH` remains the SQLite file override.
- `LOKAL_DB_CONNECT_TIMEOUT` bounds the initial connection (default `10s` in
  PostgreSQL mode).
- `LOKAL_DB_MAX_OPEN_CONNS` and `LOKAL_DB_MAX_IDLE_CONNS` configure the pool
  (PostgreSQL defaults: `20` and `5`).
- `LOKAL_DB_CONN_MAX_LIFETIME` rotates pooled cloud connections (PostgreSQL
  default: `30m`).

When a non-SQLite provider is selected, startup still migrates shared uploads
and diagnostics from a legacy installation but skips copying the old SQLite
database and its WAL sidecars.

## Enabling cloud PostgreSQL

1. Create an empty PostgreSQL database and a dedicated application role with
   permission to create and alter objects in the target schema.
2. Copy `config/postgres.env.example` into the service environment and replace
   the sample host, database, username, and password. Require TLS for a remote
   database; use `sslmode=verify-full` when the provider supplies a trusted CA.
3. Start LOKAL. A successful startup log contains
   `[DB] postgres database ready`. Credentials are never included in that log.

Browsers, PowerPoint add-ins, and student devices still connect to the LOKAL
application server. They must never receive PostgreSQL credentials or connect
to PostgreSQL directly. The application server remains responsible for
authentication, HTTP/WebSocket traffic, and database access.

For a disposable integration database, run:

```powershell
$env:LOKAL_TEST_POSTGRES_DSN='postgres://.../lokal_test?sslmode=require'
go test ./internal/database -run TestPostgresIntegration -v
```

The integration test creates the schema and temporary records, so do not point
it at production. Normal `go test ./...` runs provider/rebinding tests and skips
only this live-database check when the environment variable is absent.

## Adding another provider

Implement `database.Provider`, import its `database/sql` driver, and register it
during initialization with `database.RegisterProvider`. Hosted engines must
provide their own versioned schema migration and generated-ID implementation
(for example, `INSERT ... RETURNING id`). Repository methods continue using
provider-neutral `?` placeholders; the provider rebinder translates them.

Switching `LOKAL_DB_PROVIDER` selects one authoritative database for that
application-server process. An SQLite installation can optionally synchronize
its durable local outbox to a hosted PostgreSQL LOKAL server. See
[local-outbox-synchronization.md](local-outbox-synchronization.md) for setup,
security boundaries, retries, idempotency, and the current single-writer
conflict behavior.
