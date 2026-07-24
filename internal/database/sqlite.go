package database

import (
	"database/sql"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	_ "modernc.org/sqlite"
)

type sqliteProvider struct{}

func (sqliteProvider) Name() string               { return "sqlite" }
func (sqliteProvider) DriverName() string         { return "sqlite" }
func (sqliteProvider) Rebind(query string) string { return query }

func (sqliteProvider) DataSourceName(config Config) (string, error) {
	if config.DSN != "" {
		return config.DSN, nil
	}
	if strings.TrimSpace(config.Path) == "" {
		return "", fmt.Errorf("sqlite database path is required")
	}
	if err := os.MkdirAll(filepath.Dir(config.Path), 0755); err != nil {
		return "", fmt.Errorf("create database directory: %w", err)
	}
	separator := "?"
	if strings.Contains(config.Path, "?") {
		separator = "&"
	}
	return config.Path + separator + "_pragma=journal_mode(WAL)&_pragma=foreign_keys(1)", nil
}

func (sqliteProvider) InsertID(db *sql.DB, query, _ string, args ...any) (int64, error) {
	result, err := db.Exec(query, args...)
	if err != nil {
		return 0, err
	}
	id, err := result.LastInsertId()
	if err != nil {
		return 0, fmt.Errorf("read inserted row id: %w", err)
	}
	return id, nil
}

func (sqliteProvider) StringAggregate(expression, separator string) string {
	return fmt.Sprintf("GROUP_CONCAT(%s, '%s')", expression, strings.ReplaceAll(separator, "'", "''"))
}

func (sqliteProvider) Migrate(db *sql.DB) error {
	schema := `
	CREATE TABLE IF NOT EXISTS teachers (
		id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE NOT NULL,
		email TEXT UNIQUE, password_hash TEXT NOT NULL, display_name TEXT,
		avatar_url TEXT, created_at DATETIME DEFAULT CURRENT_TIMESTAMP
	);
	CREATE TABLE IF NOT EXISTS devices (
		id INTEGER PRIMARY KEY AUTOINCREMENT, device_uid TEXT UNIQUE NOT NULL,
		name TEXT NOT NULL, platform TEXT NOT NULL, user_agent TEXT,
		created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		last_seen_at DATETIME DEFAULT CURRENT_TIMESTAMP, revoked_at DATETIME
	);
	CREATE TABLE IF NOT EXISTS teacher_auth_sessions (
		id INTEGER PRIMARY KEY AUTOINCREMENT, teacher_id INTEGER NOT NULL,
		device_id INTEGER NOT NULL, token_hash TEXT UNIQUE NOT NULL,
		created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		last_seen_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		expires_at DATETIME NOT NULL, revoked_at DATETIME,
		FOREIGN KEY (teacher_id) REFERENCES teachers(id) ON DELETE CASCADE,
		FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS classes (
		id INTEGER PRIMARY KEY AUTOINCREMENT, teacher_id INTEGER NOT NULL,
		name TEXT NOT NULL, code TEXT UNIQUE NOT NULL, avatar_color TEXT DEFAULT '#F97316',
		is_locked BOOLEAN DEFAULT 0, max_participants INTEGER DEFAULT 200,
		created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		FOREIGN KEY (teacher_id) REFERENCES teachers(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS participants (
		id INTEGER PRIMARY KEY AUTOINCREMENT, class_id INTEGER NOT NULL, name TEXT NOT NULL,
		device_id TEXT, avatar_url TEXT, total_stars INTEGER DEFAULT 0, level INTEGER DEFAULT 1,
		joined_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		FOREIGN KEY (class_id) REFERENCES classes(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS groups (
		id INTEGER PRIMARY KEY AUTOINCREMENT, class_id INTEGER NOT NULL, name TEXT NOT NULL,
		color TEXT DEFAULT '#0B1F1C', created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		FOREIGN KEY (class_id) REFERENCES classes(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS group_members (
		id INTEGER PRIMARY KEY AUTOINCREMENT, group_id INTEGER NOT NULL, participant_id INTEGER NOT NULL,
		FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE,
		FOREIGN KEY (participant_id) REFERENCES participants(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS sessions (
		id INTEGER PRIMARY KEY AUTOINCREMENT, class_id INTEGER NOT NULL,
		started_at DATETIME DEFAULT CURRENT_TIMESTAMP, ended_at DATETIME,
		is_active BOOLEAN DEFAULT 1, is_favorite BOOLEAN DEFAULT 0,
		FOREIGN KEY (class_id) REFERENCES classes(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS activities (
		id INTEGER PRIMARY KEY AUTOINCREMENT, session_id INTEGER NOT NULL, class_id INTEGER NOT NULL,
		type TEXT NOT NULL, question_text TEXT, config JSON, is_quiz_mode BOOLEAN DEFAULT 0,
		auto_close_seconds INTEGER DEFAULT 0, started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		closed_at DATETIME, is_favorite BOOLEAN DEFAULT 0,
		FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS responses (
		id INTEGER PRIMARY KEY AUTOINCREMENT, activity_id INTEGER NOT NULL,
		participant_id INTEGER NOT NULL, answer JSON, is_correct BOOLEAN,
		stars_earned INTEGER DEFAULT 0, response_time_ms INTEGER,
		submitted_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		FOREIGN KEY (activity_id) REFERENCES activities(id) ON DELETE CASCADE,
		FOREIGN KEY (participant_id) REFERENCES participants(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS star_levels (
		id INTEGER PRIMARY KEY AUTOINCREMENT, teacher_id INTEGER NOT NULL, level INTEGER NOT NULL,
		stars_required INTEGER NOT NULL, badge_name TEXT,
		FOREIGN KEY (teacher_id) REFERENCES teachers(id) ON DELETE CASCADE
	);
	CREATE TABLE IF NOT EXISTS student_auth_sessions (
		id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id INTEGER NOT NULL,
		device_id INTEGER NOT NULL, token_hash TEXT UNIQUE NOT NULL,
		created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		last_seen_at DATETIME DEFAULT CURRENT_TIMESTAMP,
		expires_at DATETIME NOT NULL, revoked_at DATETIME,
		FOREIGN KEY (participant_id) REFERENCES participants(id) ON DELETE CASCADE,
		FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
	);`
	if _, err := db.Exec(schema); err != nil {
		return err
	}
	for _, column := range []struct{ table, name, definition string }{
		{"sessions", "is_favorite", "BOOLEAN DEFAULT 0"},
		{"activities", "is_favorite", "BOOLEAN DEFAULT 0"},
		{"teachers", "organization", "TEXT DEFAULT ''"},
		{"teachers", "profession", "TEXT DEFAULT ''"},
	} {
		if err := sqliteAddColumnIfMissing(db, column.table, column.name, column.definition); err != nil {
			return err
		}
	}
	if _, err := db.Exec("CREATE UNIQUE INDEX IF NOT EXISTS idx_group_members_participant ON group_members(participant_id)"); err != nil {
		return err
	}
	for _, statement := range []string{
		"CREATE INDEX IF NOT EXISTS idx_teacher_auth_teacher_device ON teacher_auth_sessions(teacher_id, device_id)",
		"CREATE INDEX IF NOT EXISTS idx_student_auth_participant_device ON student_auth_sessions(participant_id, device_id)",
	} {
		if _, err := db.Exec(statement); err != nil {
			return err
		}
	}
	_, err := db.Exec(`UPDATE groups SET color = '#0B1F1C' WHERE UPPER(color) IN ('#062620','#0B302B','#0D4039','#12544B','#16655A','#1B7A6C','#0F766E')`)
	if err != nil {
		return err
	}
	return installSQLiteOutbox(db)
}

func sqliteAddColumnIfMissing(db *sql.DB, table, column, definition string) error {
	rows, err := db.Query("PRAGMA table_info(" + table + ")")
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var cid int
		var name, dataType string
		var notNull int
		var defaultValue any
		var primaryKey int
		if err := rows.Scan(&cid, &name, &dataType, &notNull, &defaultValue, &primaryKey); err != nil {
			return err
		}
		if strings.EqualFold(name, column) {
			return nil
		}
	}
	if err := rows.Err(); err != nil {
		return err
	}
	_, err = db.Exec("ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition)
	return err
}

func init() { RegisterProvider(sqliteProvider{}) }
