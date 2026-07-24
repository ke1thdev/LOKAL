# Local outbox synchronization

LOKAL can keep accepting classroom changes in SQLite while Internet access is
unavailable, then deliver those changes to a hosted LOKAL server backed by
PostgreSQL. The feature is opt-in and independent from the Offline, Local
Network, and Online reachability modes.

## Data flow

1. SQLite triggers append a complete row snapshot to `sync_outbox` in the same
   database transaction as each classroom mutation.
2. The background worker reads a bounded batch in creation order.
3. It sends the batch over HTTPS to `/api/v1/sync/outbox` using the configured
   bearer synchronization secret.
4. The hosted PostgreSQL server applies each allowlisted row and records its
   unique event ID in `sync_inbox`.
5. Only after a successful response does the local server mark the events
   delivered. Failed batches remain durable and retry with bounded backoff.

Event IDs make repeated delivery idempotent. Explicit primary keys preserve
relationships between classes, participants, sessions, activities, and
responses.

## Local server configuration

Set these values in the Windows service environment and restart **LOKAL
Server**:

```text
LOKAL_DB_PROVIDER=sqlite
LOKAL_CLOUD_SYNC_URL=https://class.example.edu
LOKAL_SYNC_SECRET=replace-with-a-long-random-shared-secret
LOKAL_SYNC_INTERVAL=5s
```

`LOKAL_CLOUD_SYNC_URL` may be the hosted LOKAL base URL or the complete
`/api/v1/sync/outbox` URL. The teacher dashboard Server page shows queued,
retrying, and delivered event counts and provides a **Sync now** action.

## Hosted server configuration

Run the hosted application with PostgreSQL and the same secret:

```text
LOKAL_DB_PROVIDER=postgres
LOKAL_DB_DSN=postgres://lokal:password@db.example.edu/lokal?sslmode=verify-full
LOKAL_SYNC_SECRET=replace-with-a-long-random-shared-secret
LOKAL_SERVER_MODE=online
LOKAL_PUBLIC_URL=https://class.example.edu
```

The receiver is disabled unless the active provider is PostgreSQL and
`LOKAL_SYNC_SECRET` is present.

## Replicated and excluded records

The outbox covers teachers, classes, participants, groups, group membership,
sessions, activities, responses, and star-level settings. Teacher password
hashes are included so the hosted copy can authenticate the same teacher.
Therefore HTTPS, a strong secret, restricted database access, and encrypted
backups are required.

Device registration records and opaque authentication sessions are
intentionally excluded. A stolen or replayed local browser token is never
promoted into the hosted system.

## Conflict behavior

The current implementation is a local-to-cloud, ordered synchronization path.
The latest delivered local row snapshot replaces the same hosted row. It is
appropriate when one registered LOKAL installation is the writer for a given
class. Bidirectional cloud-to-local merge, concurrent multi-device editing, and
automatic conflict resolution are separate future capabilities.

