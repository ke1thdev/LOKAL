package database

import (
	"database/sql"
	"fmt"
	"regexp"
	"strings"
	"unicode"

	_ "github.com/jackc/pgx/v5/stdlib"
)

// postgresProvider is the cloud database implementation. It deliberately uses
// pgx through database/sql so the repository and transaction APIs remain the
// same in offline (SQLite) and online (PostgreSQL) operating modes.
type postgresProvider struct{}

func (postgresProvider) Name() string       { return "postgres" }
func (postgresProvider) DriverName() string { return "pgx" }

func (postgresProvider) DataSourceName(config Config) (string, error) {
	dsn := strings.TrimSpace(config.DSN)
	if dsn == "" {
		return "", fmt.Errorf("LOKAL_DB_DSN is required when LOKAL_DB_PROVIDER=postgres")
	}
	return dsn, nil
}

// Rebind converts repository-neutral question-mark placeholders into pgx's
// positional placeholders. Question marks inside strings, quoted identifiers,
// dollar-quoted bodies, and SQL comments are intentionally preserved.
func (postgresProvider) Rebind(query string) string {
	var output strings.Builder
	output.Grow(len(query) + 16)
	argument := 1
	for i := 0; i < len(query); {
		switch query[i] {
		case '\'':
			start := i
			i = consumeQuoted(query, i, '\'')
			output.WriteString(query[start:i])
		case '"':
			start := i
			i = consumeQuoted(query, i, '"')
			output.WriteString(query[start:i])
		case '-':
			if i+1 < len(query) && query[i+1] == '-' {
				start := i
				i += 2
				for i < len(query) && query[i] != '\n' {
					i++
				}
				output.WriteString(query[start:i])
				continue
			}
			output.WriteByte(query[i])
			i++
		case '/':
			if i+1 < len(query) && query[i+1] == '*' {
				start := i
				i += 2
				for i+1 < len(query) && !(query[i] == '*' && query[i+1] == '/') {
					i++
				}
				if i+1 < len(query) {
					i += 2
				}
				output.WriteString(query[start:i])
				continue
			}
			output.WriteByte(query[i])
			i++
		case '$':
			if delimiter, ok := dollarQuoteDelimiter(query[i:]); ok {
				start := i
				i += len(delimiter)
				if end := strings.Index(query[i:], delimiter); end >= 0 {
					i += end + len(delimiter)
				} else {
					i = len(query)
				}
				output.WriteString(query[start:i])
				continue
			}
			output.WriteByte(query[i])
			i++
		case '?':
			fmt.Fprintf(&output, "$%d", argument)
			argument++
			i++
		default:
			output.WriteByte(query[i])
			i++
		}
	}
	return output.String()
}

func consumeQuoted(query string, start int, quote byte) int {
	for i := start + 1; i < len(query); i++ {
		if query[i] != quote {
			continue
		}
		if i+1 < len(query) && query[i+1] == quote {
			i++
			continue
		}
		return i + 1
	}
	return len(query)
}

func dollarQuoteDelimiter(value string) (string, bool) {
	if value == "" || value[0] != '$' {
		return "", false
	}
	for i := 1; i < len(value); i++ {
		if value[i] == '$' {
			return value[:i+1], true
		}
		if !(value[i] == '_' || unicode.IsLetter(rune(value[i])) || (i > 1 && unicode.IsDigit(rune(value[i])))) {
			return "", false
		}
	}
	return "", false
}

var postgresIdentifier = regexp.MustCompile(`^[A-Za-z_][A-Za-z0-9_]*$`)

func (postgresProvider) InsertID(db *sql.DB, query, idColumn string, args ...any) (int64, error) {
	if !postgresIdentifier.MatchString(idColumn) {
		return 0, fmt.Errorf("invalid PostgreSQL RETURNING column %q", idColumn)
	}
	query = strings.TrimSpace(query)
	query = strings.TrimSuffix(query, ";") + ` RETURNING "` + idColumn + `"`
	var id int64
	if err := db.QueryRow(query, args...).Scan(&id); err != nil {
		return 0, err
	}
	return id, nil
}

func (postgresProvider) StringAggregate(expression, separator string) string {
	return fmt.Sprintf("STRING_AGG(%s, '%s')", expression, strings.ReplaceAll(separator, "'", "''"))
}

func (postgresProvider) Migrate(db *sql.DB) error {
	tx, err := db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	// A transaction-scoped advisory lock prevents two LOKAL instances from
	// racing the schema migration during a cloud deployment or rolling update.
	if _, err := tx.Exec(`SELECT pg_advisory_xact_lock(7646520250722)`); err != nil {
		return fmt.Errorf("acquire migration lock: %w", err)
	}

	statements := []string{
		`CREATE TABLE IF NOT EXISTS schema_migrations (
			version BIGINT PRIMARY KEY,
			applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE TABLE IF NOT EXISTS teachers (
			id BIGSERIAL PRIMARY KEY,
			username TEXT UNIQUE NOT NULL,
			email TEXT UNIQUE,
			password_hash TEXT NOT NULL,
			display_name TEXT,
			avatar_url TEXT,
			organization TEXT NOT NULL DEFAULT '',
			profession TEXT NOT NULL DEFAULT '',
			created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE TABLE IF NOT EXISTS devices (
			id BIGSERIAL PRIMARY KEY,
			device_uid TEXT UNIQUE NOT NULL,
			name TEXT NOT NULL,
			platform TEXT NOT NULL,
			user_agent TEXT,
			created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			last_seen_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			revoked_at TIMESTAMPTZ
		)`,
		`CREATE TABLE IF NOT EXISTS teacher_auth_sessions (
			id BIGSERIAL PRIMARY KEY,
			teacher_id BIGINT NOT NULL REFERENCES teachers(id) ON DELETE CASCADE,
			device_id BIGINT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
			token_hash TEXT UNIQUE NOT NULL,
			created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			last_seen_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			expires_at TIMESTAMPTZ NOT NULL,
			revoked_at TIMESTAMPTZ
		)`,
		`CREATE TABLE IF NOT EXISTS classes (
			id BIGSERIAL PRIMARY KEY,
			teacher_id BIGINT NOT NULL REFERENCES teachers(id) ON DELETE CASCADE,
			name TEXT NOT NULL,
			code TEXT UNIQUE NOT NULL,
			avatar_color TEXT NOT NULL DEFAULT '#F97316',
			is_locked BOOLEAN NOT NULL DEFAULT FALSE,
			max_participants INTEGER NOT NULL DEFAULT 200,
			created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE TABLE IF NOT EXISTS participants (
			id BIGSERIAL PRIMARY KEY,
			class_id BIGINT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
			name TEXT NOT NULL,
			device_id TEXT,
			avatar_url TEXT,
			total_stars INTEGER NOT NULL DEFAULT 0,
			level INTEGER NOT NULL DEFAULT 1,
			joined_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE TABLE IF NOT EXISTS groups (
			id BIGSERIAL PRIMARY KEY,
			class_id BIGINT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
			name TEXT NOT NULL,
			color TEXT NOT NULL DEFAULT '#0B1F1C',
			created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE TABLE IF NOT EXISTS group_members (
			id BIGSERIAL PRIMARY KEY,
			group_id BIGINT NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
			participant_id BIGINT NOT NULL REFERENCES participants(id) ON DELETE CASCADE
		)`,
		`CREATE TABLE IF NOT EXISTS sessions (
			id BIGSERIAL PRIMARY KEY,
			class_id BIGINT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
			started_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			ended_at TIMESTAMPTZ,
			is_active BOOLEAN NOT NULL DEFAULT TRUE,
			is_favorite BOOLEAN NOT NULL DEFAULT FALSE
		)`,
		`CREATE TABLE IF NOT EXISTS activities (
			id BIGSERIAL PRIMARY KEY,
			session_id BIGINT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
			class_id BIGINT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
			type TEXT NOT NULL,
			question_text TEXT,
			config JSONB,
			is_quiz_mode BOOLEAN NOT NULL DEFAULT FALSE,
			auto_close_seconds INTEGER NOT NULL DEFAULT 0,
			started_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			closed_at TIMESTAMPTZ,
			is_favorite BOOLEAN NOT NULL DEFAULT FALSE
		)`,
		`CREATE TABLE IF NOT EXISTS responses (
			id BIGSERIAL PRIMARY KEY,
			activity_id BIGINT NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
			participant_id BIGINT NOT NULL REFERENCES participants(id) ON DELETE CASCADE,
			answer JSONB,
			is_correct BOOLEAN,
			stars_earned INTEGER NOT NULL DEFAULT 0,
			response_time_ms INTEGER,
			submitted_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE TABLE IF NOT EXISTS star_levels (
			id BIGSERIAL PRIMARY KEY,
			teacher_id BIGINT NOT NULL REFERENCES teachers(id) ON DELETE CASCADE,
			level INTEGER NOT NULL,
			stars_required INTEGER NOT NULL,
			badge_name TEXT
		)`,
		`CREATE TABLE IF NOT EXISTS student_auth_sessions (
			id BIGSERIAL PRIMARY KEY,
			participant_id BIGINT NOT NULL REFERENCES participants(id) ON DELETE CASCADE,
			device_id BIGINT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
			token_hash TEXT UNIQUE NOT NULL,
			created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			last_seen_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
			expires_at TIMESTAMPTZ NOT NULL,
			revoked_at TIMESTAMPTZ
		)`,
		`CREATE TABLE IF NOT EXISTS sync_inbox (
			event_id TEXT PRIMARY KEY,
			source_node TEXT NOT NULL,
			table_name TEXT NOT NULL,
			action TEXT NOT NULL,
			record_id TEXT NOT NULL,
			received_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
		)`,
		`CREATE INDEX IF NOT EXISTS idx_sync_inbox_received_at ON sync_inbox(received_at)`,
		`ALTER TABLE teachers ADD COLUMN IF NOT EXISTS organization TEXT NOT NULL DEFAULT ''`,
		`ALTER TABLE teachers ADD COLUMN IF NOT EXISTS profession TEXT NOT NULL DEFAULT ''`,
		`ALTER TABLE sessions ADD COLUMN IF NOT EXISTS is_favorite BOOLEAN NOT NULL DEFAULT FALSE`,
		`ALTER TABLE activities ADD COLUMN IF NOT EXISTS is_favorite BOOLEAN NOT NULL DEFAULT FALSE`,
		`CREATE INDEX IF NOT EXISTS idx_classes_teacher ON classes(teacher_id)`,
		`CREATE INDEX IF NOT EXISTS idx_participants_class ON participants(class_id)`,
		`CREATE INDEX IF NOT EXISTS idx_groups_class ON groups(class_id)`,
		`CREATE UNIQUE INDEX IF NOT EXISTS idx_group_members_participant ON group_members(participant_id)`,
		`CREATE INDEX IF NOT EXISTS idx_sessions_class_active ON sessions(class_id, is_active)`,
		`CREATE INDEX IF NOT EXISTS idx_activities_class_session ON activities(class_id, session_id)`,
		`CREATE INDEX IF NOT EXISTS idx_responses_activity_participant ON responses(activity_id, participant_id)`,
		`CREATE UNIQUE INDEX IF NOT EXISTS idx_star_levels_teacher_level ON star_levels(teacher_id, level)`,
		`CREATE INDEX IF NOT EXISTS idx_teacher_auth_teacher_device ON teacher_auth_sessions(teacher_id, device_id)`,
		`CREATE INDEX IF NOT EXISTS idx_student_auth_participant_device ON student_auth_sessions(participant_id, device_id)`,
		`UPDATE groups SET color = '#0B1F1C' WHERE UPPER(color) IN ('#062620','#0B302B','#0D4039','#12544B','#16655A','#1B7A6C','#0F766E')`,
		`INSERT INTO schema_migrations(version) VALUES (1) ON CONFLICT (version) DO NOTHING`,
	}
	for index, statement := range statements {
		if _, err := tx.Exec(statement); err != nil {
			return fmt.Errorf("PostgreSQL migration statement %d: %w", index+1, err)
		}
	}
	return tx.Commit()
}

func init() { RegisterProvider(postgresProvider{}) }
