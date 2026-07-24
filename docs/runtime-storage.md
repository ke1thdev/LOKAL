# LOKAL runtime storage

LOKAL keeps writable runtime data outside the installation and source folders.
On Windows, the default layout is:

```text
C:\ProgramData\LOKAL\
  data\lokal.db
  uploads\slides\
  uploads\avatars\
  logs\lokal.log
  logs\slide-error.log
```

The server creates the directory tree at startup. On the first run of the
installed layout it also performs a non-destructive, idempotent import from the
legacy executable and working directories. Existing ProgramData files always
win. The old `lokal.db`, `uploads`, and `slide_error.txt` files are left in place
as a rollback copy.

The installer should stop any running legacy LOKAL server before the first
launch and grant the account that runs LOKAL Modify permission on
`C:\ProgramData\LOKAL`.

## Environment overrides

- `LOKAL_DATA_DIR` changes the root data directory.
- `LOKAL_DB_PATH` changes only the SQLite database path.
- `LOKAL_DB_PROVIDER` selects a registered SQL provider (default: `sqlite`).
- `LOKAL_DB_DSN` supplies that provider's connection string and should be
  configured as a secret for hosted deployments.
- PostgreSQL cloud mode and its connection-pool environment variables are
  documented in `database-providers.md`.
- `LOKAL_UPLOADS_PATH` changes only the uploads directory.
- `LOKAL_LOG_PATH` changes the server log file.
- `LOKAL_MIGRATE_LEGACY=1` explicitly enables legacy migration when path
  overrides are in use.

Explicit path overrides are isolated by default and do not import legacy data.
The `db_check` diagnostic command opens the resolved ProgramData database; an
explicit database filename may be supplied as its first command-line argument.
See `database-providers.md` for the provider extension contract.
