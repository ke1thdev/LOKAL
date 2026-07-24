# LOKAL hybrid WebSocket relay

LOKAL keeps direct offline and LAN classroom traffic local. The optional relay
adds a persistent outbound connection from a classroom PC to a hosted LOKAL
server, allowing remote browsers to use the same classroom WebSocket protocol
without opening an inbound port on the classroom network.

## Deployment roles

### Hosted LOKAL server

Run the PostgreSQL-backed hosted application with:

```text
LOKAL_RELAY_HOST_ENABLED=true
LOKAL_RELAY_SECRET=<long-random-relay-secret>
```

The hosted broker listens at `/api/v1/relay/edge`. The endpoint requires the
relay bearer secret and a stable `X-LOKAL-Node-ID`; it is not a public learner
WebSocket endpoint. Learners continue to connect to the hosted `/ws` endpoint
using their normal opaque student session token.

### Local classroom server

Run the SQLite-backed classroom installation with:

```text
LOKAL_RELAY_URL=https://class.example.edu
LOKAL_RELAY_SECRET=<same-long-random-relay-secret>
```

`LOKAL_RELAY_URL` accepts an HTTP(S) hosted base URL or the complete WS(S)
endpoint. HTTPS is converted to WSS automatically. The local node identity is
stored durably in SQLite's `sync_node` table; `LOKAL_RELAY_NODE_ID` may override
it for managed deployments.

## Reliability and safety

- LAN delivery happens before relay forwarding and never waits for the
  Internet.
- The edge connector reconnects with exponential backoff and refreshes its
  registered classroom list every 30 seconds.
- A bounded queue prevents an outage from exhausting local memory.
- Every event has an ID and a ten-minute deduplication window.
- Relay-originated messages are injected through a non-forwarding hub path, so
  they cannot bounce indefinitely between local and hosted servers.
- The relay secret is compared in constant time and is never returned by the
  status API.

The relay is the real-time transport layer. Durable SQLite-to-PostgreSQL
replication remains handled independently by the local outbox synchronizer.
Both should be configured for a complete online classroom deployment.
