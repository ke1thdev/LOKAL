# Authentication and device registration

LOKAL uses database-backed, revocable sessions for browser and PowerPoint
clients. A device identifier represents an application installation; it is a
random value stored by the client and is not a hardware fingerprint.

## Teacher authentication

- `POST /api/v1/auth/register` creates the account and its first device session.
- `POST /api/v1/auth/login` registers or refreshes the supplied device and
  returns an opaque `lkt_` bearer token.
- Only a SHA-256 hash of the token is stored in `teacher_auth_sessions`.
- A new login on the same device revokes its previous active session.
- `POST /api/v1/auth/logout` revokes the current session.
- `GET /api/v1/auth/devices` lists the account's registered devices.
- `DELETE /api/v1/auth/devices/{id}` revokes every active session for that
  device.

Teacher browser device IDs are retained in local storage. The PowerPoint
add-in generates one installation ID and retains it in user-scoped add-in
settings.

## Student identity

Joining a class remains a public operation, but a successful join now returns
an opaque `lks_` token bound to the participant and device. The token is
required for:

- reloading class state;
- submitting an activity response;
- opening the class WebSocket connection.

Participant identity is derived from the bearer token rather than trusted from
a caller-supplied participant ID. A second device cannot silently claim an
existing name. The original device may reconnect and keeps the same
participant record, stars, and level.

## Signing key and compatibility

Presenter JWTs used by existing local PowerPoint flows are signed with HS256
and validated with fixed issuer and audience values. The signing key is loaded
from `LOKAL_JWT_SECRET` when configured, otherwise it is generated once and
stored in the ProgramData runtime configuration directory as `auth.key`.

Opaque database sessions are the preferred authentication mechanism. Signed
presenter JWT support remains for compatibility with already-installed add-ins.

## Storage

The database provider creates these tables on migration:

- `devices`
- `teacher_auth_sessions`
- `student_auth_sessions`

This schema is supported by both SQLite offline mode and PostgreSQL cloud mode.
Expired or revoked sessions are rejected even if a client still retains the
raw token.
