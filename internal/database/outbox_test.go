package database

import (
	"path/filepath"
	"testing"
)

func TestSQLiteOutboxCapturesDomainMutations(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "outbox.db"))
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("sync-teacher", "sync@example.test", "hash", "Sync Teacher")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	class, err := db.CreateClass(teacher.ID, "Sync Class", "SYNC1", "#0B1F1C")
	if err != nil {
		t.Fatalf("create class: %v", err)
	}
	participant, err := db.AddParticipant(class.ID, "Learner", "device-local", "")
	if err != nil {
		t.Fatalf("add participant: %v", err)
	}
	if err := db.AwardStars(participant.ID, 3); err != nil {
		t.Fatalf("award stars: %v", err)
	}

	events, err := db.PendingOutbox(100)
	if err != nil {
		t.Fatalf("read outbox: %v", err)
	}
	if len(events) < 4 {
		t.Fatalf("captured %d events; want teacher, class, participant insert and participant update", len(events))
	}
	var foundParticipantUpdate bool
	for _, event := range events {
		if event.EventID == "" || event.SourceNode == "" {
			t.Fatalf("event lacks durable identity: %#v", event)
		}
		if event.TableName == "participants" && event.Action == "upsert" &&
			string(event.Payload) != "" {
			values, decodeErr := decodeOutboxPayload(event.Payload, replicatedTables["participants"])
			if decodeErr != nil {
				t.Fatalf("decode participant payload: %v", decodeErr)
			}
			if values["total_stars"] == int64(3) {
				foundParticipantUpdate = true
			}
		}
	}
	if !foundParticipantUpdate {
		t.Fatal("updated participant star snapshot was not captured")
	}

	stats, err := db.OutboxStats()
	if err != nil {
		t.Fatalf("outbox stats: %v", err)
	}
	if stats.Pending != len(events) || stats.OldestPending == nil {
		t.Fatalf("stats = %#v; want %d pending with oldest timestamp", stats, len(events))
	}

	ids := make([]int64, len(events))
	for index := range events {
		ids[index] = events[index].ID
	}
	if err := db.MarkOutboxFailed(ids[:1], "temporary cloud outage"); err != nil {
		t.Fatalf("mark failed: %v", err)
	}
	stats, err = db.OutboxStats()
	if err != nil || stats.Failed != 1 {
		t.Fatalf("failed stats = %#v, %v", stats, err)
	}
	if err := db.MarkOutboxSynced(ids); err != nil {
		t.Fatalf("mark synced: %v", err)
	}
	stats, err = db.OutboxStats()
	if err != nil || stats.Pending != 0 || stats.Synced != len(events) {
		t.Fatalf("synced stats = %#v, %v", stats, err)
	}
}

func TestSQLiteOutboxExcludesAuthenticationState(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "outbox-auth.db"))
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("auth-teacher", "auth@example.test", "hash", "Auth Teacher")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	result, err := db.Exec(`INSERT INTO devices
		(device_uid, name, platform) VALUES (?, ?, ?)`,
		"excluded-device", "Excluded browser", "test")
	if err != nil {
		t.Fatalf("insert device: %v", err)
	}
	deviceID, err := result.LastInsertId()
	if err != nil {
		t.Fatalf("device id: %v", err)
	}
	initial, err := db.PendingOutbox(100)
	if err != nil {
		t.Fatalf("read initial outbox: %v", err)
	}
	if _, err := db.Exec(`INSERT INTO teacher_auth_sessions
		(teacher_id, device_id, token_hash, expires_at)
		VALUES (?, ?, ?, datetime('now', '+1 day'))`,
		teacher.ID, deviceID, "opaque-token-hash"); err != nil {
		t.Fatalf("insert auth session: %v", err)
	}
	after, err := db.PendingOutbox(100)
	if err != nil {
		t.Fatalf("read final outbox: %v", err)
	}
	if len(after) != len(initial) {
		t.Fatalf("authentication mutation created an outbox event: before=%d after=%d", len(initial), len(after))
	}
}
