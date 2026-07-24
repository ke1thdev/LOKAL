package database

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"sort"
	"strconv"
	"strings"
	"time"
)

// OutboxEvent is a durable, state-based mutation captured by the local SQLite
// database. EventID makes cloud delivery idempotent; Payload is a complete row
// snapshot for insert/update events and is empty for deletes.
type OutboxEvent struct {
	ID           int64           `json:"-"`
	EventID      string          `json:"event_id"`
	SourceNode   string          `json:"source_node"`
	TableName    string          `json:"table_name"`
	Action       string          `json:"action"`
	RecordID     string          `json:"record_id"`
	Payload      json.RawMessage `json:"payload,omitempty"`
	CreatedAt    time.Time       `json:"created_at"`
	AttemptCount int             `json:"attempt_count,omitempty"`
}

type OutboxStats struct {
	Pending       int        `json:"pending"`
	Failed        int        `json:"failed"`
	Synced        int        `json:"synced"`
	OldestPending *time.Time `json:"oldest_pending,omitempty"`
}

// Only application data is replicated. Device registrations and opaque login
// sessions deliberately stay on the database where they were issued.
var replicatedTables = map[string]struct {
	columns []string
	bools   map[string]bool
}{
	"teachers": {
		columns: []string{"id", "username", "email", "password_hash", "display_name", "avatar_url", "organization", "profession", "created_at"},
	},
	"classes": {
		columns: []string{"id", "teacher_id", "name", "code", "avatar_color", "is_locked", "max_participants", "created_at"},
		bools:   map[string]bool{"is_locked": true},
	},
	"participants": {
		columns: []string{"id", "class_id", "name", "device_id", "avatar_url", "total_stars", "level", "joined_at"},
	},
	"groups": {
		columns: []string{"id", "class_id", "name", "color", "created_at"},
	},
	"group_members": {
		columns: []string{"id", "group_id", "participant_id"},
	},
	"sessions": {
		columns: []string{"id", "class_id", "started_at", "ended_at", "is_active", "is_favorite"},
		bools:   map[string]bool{"is_active": true, "is_favorite": true},
	},
	"activities": {
		columns: []string{"id", "session_id", "class_id", "type", "question_text", "config", "is_quiz_mode", "auto_close_seconds", "started_at", "closed_at", "is_favorite"},
		bools:   map[string]bool{"is_quiz_mode": true, "is_favorite": true},
	},
	"responses": {
		columns: []string{"id", "activity_id", "participant_id", "answer", "is_correct", "stars_earned", "response_time_ms", "submitted_at"},
		bools:   map[string]bool{"is_correct": true},
	},
	"star_levels": {
		columns: []string{"id", "teacher_id", "level", "stars_required", "badge_name"},
	},
}

func installSQLiteOutbox(db *sql.DB) error {
	if _, err := db.Exec(`
		CREATE TABLE IF NOT EXISTS sync_node (
			id INTEGER PRIMARY KEY CHECK (id = 1),
			node_uid TEXT UNIQUE NOT NULL,
			created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
		);
		INSERT OR IGNORE INTO sync_node(id, node_uid)
		VALUES (1, lower(hex(randomblob(16))));
		CREATE TABLE IF NOT EXISTS sync_outbox (
			id INTEGER PRIMARY KEY AUTOINCREMENT,
			event_id TEXT UNIQUE NOT NULL,
			source_node TEXT NOT NULL,
			table_name TEXT NOT NULL,
			action TEXT NOT NULL CHECK (action IN ('upsert','delete')),
			record_id TEXT NOT NULL,
			payload TEXT,
			created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
			attempt_count INTEGER NOT NULL DEFAULT 0,
			next_attempt_at DATETIME,
			last_error TEXT,
			synced_at DATETIME
		);
		CREATE INDEX IF NOT EXISTS idx_sync_outbox_pending
			ON sync_outbox(synced_at, next_attempt_at, id);
	`); err != nil {
		return fmt.Errorf("create local sync outbox: %w", err)
	}

	tableNames := make([]string, 0, len(replicatedTables))
	for table := range replicatedTables {
		tableNames = append(tableNames, table)
	}
	sort.Strings(tableNames)
	for _, table := range tableNames {
		spec := replicatedTables[table]
		pairs := make([]string, 0, len(spec.columns)*2)
		for _, column := range spec.columns {
			pairs = append(pairs, "'"+column+"'", "NEW."+column)
		}
		payload := "json_object(" + strings.Join(pairs, ",") + ")"
		insertTrigger := fmt.Sprintf(`
			CREATE TRIGGER IF NOT EXISTS lokal_sync_%[1]s_insert
			AFTER INSERT ON %[1]s
			BEGIN
				INSERT INTO sync_outbox(event_id, source_node, table_name, action, record_id, payload)
				VALUES (lower(hex(randomblob(16))), (SELECT node_uid FROM sync_node WHERE id=1),
					'%[1]s', 'upsert', CAST(NEW.id AS TEXT), %[2]s);
			END`, table, payload)
		updateTrigger := fmt.Sprintf(`
			CREATE TRIGGER IF NOT EXISTS lokal_sync_%[1]s_update
			AFTER UPDATE ON %[1]s
			BEGIN
				INSERT INTO sync_outbox(event_id, source_node, table_name, action, record_id, payload)
				VALUES (lower(hex(randomblob(16))), (SELECT node_uid FROM sync_node WHERE id=1),
					'%[1]s', 'upsert', CAST(NEW.id AS TEXT), %[2]s);
			END`, table, payload)
		deleteTrigger := fmt.Sprintf(`
			CREATE TRIGGER IF NOT EXISTS lokal_sync_%[1]s_delete
			AFTER DELETE ON %[1]s
			BEGIN
				INSERT INTO sync_outbox(event_id, source_node, table_name, action, record_id, payload)
				VALUES (lower(hex(randomblob(16))), (SELECT node_uid FROM sync_node WHERE id=1),
					'%[1]s', 'delete', CAST(OLD.id AS TEXT), NULL);
			END`, table)
		for _, statement := range []string{insertTrigger, updateTrigger, deleteTrigger} {
			if _, err := db.Exec(statement); err != nil {
				return fmt.Errorf("install %s outbox trigger: %w", table, err)
			}
		}
	}
	return nil
}

func (d *DB) PendingOutbox(limit int) ([]OutboxEvent, error) {
	if d.ProviderName() != DefaultProvider {
		return nil, nil
	}
	if limit <= 0 || limit > 500 {
		limit = 100
	}
	rows, err := d.Query(`
		SELECT id, event_id, source_node, table_name, action, record_id,
		       COALESCE(payload,''), created_at, attempt_count
		FROM sync_outbox
		WHERE synced_at IS NULL
		  AND (next_attempt_at IS NULL OR next_attempt_at <= CURRENT_TIMESTAMP)
		ORDER BY id
		LIMIT ?`, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	events := make([]OutboxEvent, 0, limit)
	for rows.Next() {
		var event OutboxEvent
		var payload string
		if err := rows.Scan(&event.ID, &event.EventID, &event.SourceNode, &event.TableName,
			&event.Action, &event.RecordID, &payload, &event.CreatedAt, &event.AttemptCount); err != nil {
			return nil, err
		}
		if payload != "" {
			event.Payload = json.RawMessage(payload)
		}
		events = append(events, event)
	}
	return events, rows.Err()
}

func (d *DB) MarkOutboxSynced(ids []int64) error {
	if len(ids) == 0 {
		return nil
	}
	placeholders := make([]string, len(ids))
	args := make([]any, len(ids))
	for index, id := range ids {
		placeholders[index], args[index] = "?", id
	}
	_, err := d.Exec(`UPDATE sync_outbox
		SET synced_at=CURRENT_TIMESTAMP, last_error=NULL, next_attempt_at=NULL
		WHERE id IN (`+strings.Join(placeholders, ",")+`)`, args...)
	return err
}

func (d *DB) MarkOutboxFailed(ids []int64, message string) error {
	if len(ids) == 0 {
		return nil
	}
	if len(message) > 1000 {
		message = message[:1000]
	}
	placeholders := make([]string, len(ids))
	args := []any{message}
	for index, id := range ids {
		placeholders[index] = "?"
		args = append(args, id)
	}
	_, err := d.Exec(`UPDATE sync_outbox
		SET attempt_count=attempt_count+1,
		    last_error=?,
		    next_attempt_at=datetime('now', '+' || MIN(300, (attempt_count + 1) * (attempt_count + 1) * 5) || ' seconds')
		WHERE id IN (`+strings.Join(placeholders, ",")+`)`, args...)
	return err
}

func (d *DB) OutboxStats() (OutboxStats, error) {
	if d.ProviderName() != DefaultProvider {
		return OutboxStats{}, nil
	}
	var stats OutboxStats
	var oldest sql.NullString
	err := d.QueryRow(`
		SELECT
			COALESCE(SUM(CASE WHEN synced_at IS NULL THEN 1 ELSE 0 END), 0),
			COALESCE(SUM(CASE WHEN synced_at IS NULL AND attempt_count > 0 THEN 1 ELSE 0 END), 0),
			COALESCE(SUM(CASE WHEN synced_at IS NOT NULL THEN 1 ELSE 0 END), 0),
			MIN(CASE WHEN synced_at IS NULL THEN created_at END)
		FROM sync_outbox`).Scan(&stats.Pending, &stats.Failed, &stats.Synced, &oldest)
	if err != nil {
		return stats, err
	}
	if oldest.Valid {
		for _, layout := range []string{
			time.RFC3339Nano,
			"2006-01-02 15:04:05.999999999-07:00",
			"2006-01-02 15:04:05",
		} {
			parsed, parseErr := time.Parse(layout, oldest.String)
			if parseErr == nil {
				stats.OldestPending = &parsed
				break
			}
		}
	}
	return stats, nil
}

// ApplyOutboxEvents replays a batch into the hosted PostgreSQL database and
// records each event in sync_inbox in the same transaction.
func (d *DB) ApplyOutboxEvents(events []OutboxEvent) error {
	if d.ProviderName() != "postgres" {
		return errors.New("outbox receiver requires the PostgreSQL provider")
	}
	for _, event := range events {
		if err := d.applyOutboxEvent(event); err != nil {
			return fmt.Errorf("apply event %s: %w", event.EventID, err)
		}
	}
	return nil
}

func (d *DB) applyOutboxEvent(event OutboxEvent) error {
	spec, ok := replicatedTables[event.TableName]
	if !ok {
		return fmt.Errorf("table %q is not sync-enabled", event.TableName)
	}
	if event.EventID == "" || event.SourceNode == "" {
		return errors.New("event_id and source_node are required")
	}
	tx, err := d.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	var exists int
	err = tx.QueryRow(`SELECT 1 FROM sync_inbox WHERE event_id = ?`, event.EventID).Scan(&exists)
	if err == nil {
		return tx.Commit()
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return err
	}

	switch event.Action {
	case "upsert":
		values, err := decodeOutboxPayload(event.Payload, spec)
		if err != nil {
			return err
		}
		columns := make([]string, 0, len(values))
		for _, column := range spec.columns {
			if _, exists := values[column]; exists {
				columns = append(columns, column)
			}
		}
		if len(columns) == 0 {
			return errors.New("empty row payload")
		}
		quoted := make([]string, len(columns))
		placeholders := make([]string, len(columns))
		updates := make([]string, 0, len(columns)-1)
		args := make([]any, len(columns))
		for index, column := range columns {
			quoted[index] = `"` + column + `"`
			placeholders[index] = "?"
			args[index] = values[column]
			if column != "id" {
				updates = append(updates, `"`+column+`"=EXCLUDED."`+column+`"`)
			}
		}
		query := `INSERT INTO "` + event.TableName + `" (` + strings.Join(quoted, ",") +
			`) VALUES (` + strings.Join(placeholders, ",") + `) ON CONFLICT ("id") DO UPDATE SET ` +
			strings.Join(updates, ",")
		if _, err := tx.Exec(query, args...); err != nil {
			return err
		}
		// Explicit IDs keep relationships stable during replay. Advance the
		// hosted sequence so later cloud-created rows cannot collide.
		if _, err := tx.Exec(`SELECT setval(
			pg_get_serial_sequence('` + event.TableName + `', 'id'),
			GREATEST((SELECT COALESCE(MAX(id), 1) FROM "` + event.TableName + `"), 1),
			TRUE)`); err != nil {
			return err
		}
	case "delete":
		id, err := strconv.ParseInt(event.RecordID, 10, 64)
		if err != nil {
			return fmt.Errorf("invalid record id: %w", err)
		}
		if _, err := tx.Exec(`DELETE FROM "`+event.TableName+`" WHERE id = ?`, id); err != nil {
			return err
		}
	default:
		return fmt.Errorf("unsupported action %q", event.Action)
	}
	if _, err := tx.Exec(`INSERT INTO sync_inbox
		(event_id, source_node, table_name, action, record_id)
		VALUES (?, ?, ?, ?, ?)`,
		event.EventID, event.SourceNode, event.TableName, event.Action, event.RecordID); err != nil {
		return err
	}
	return tx.Commit()
}

func decodeOutboxPayload(payload json.RawMessage, spec struct {
	columns []string
	bools   map[string]bool
}) (map[string]any, error) {
	decoder := json.NewDecoder(strings.NewReader(string(payload)))
	decoder.UseNumber()
	var raw map[string]any
	if err := decoder.Decode(&raw); err != nil {
		return nil, fmt.Errorf("decode payload: %w", err)
	}
	allowed := make(map[string]bool, len(spec.columns))
	for _, column := range spec.columns {
		allowed[column] = true
	}
	values := make(map[string]any, len(raw))
	for column, value := range raw {
		if !allowed[column] {
			return nil, fmt.Errorf("column %q is not sync-enabled", column)
		}
		if spec.bools[column] && value != nil {
			switch typed := value.(type) {
			case bool:
				value = typed
			case json.Number:
				value = typed.String() != "0"
			}
		} else if number, ok := value.(json.Number); ok {
			if integer, err := number.Int64(); err == nil {
				value = integer
			} else if decimal, err := number.Float64(); err == nil {
				value = decimal
			}
		}
		values[column] = value
	}
	return values, nil
}
