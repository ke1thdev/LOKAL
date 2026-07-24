package syncoutbox

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"lokal-thesis/internal/database"
)

func TestManagerDeliversAndAcknowledgesSQLiteOutbox(t *testing.T) {
	db, err := database.New(filepath.Join(t.TempDir(), "sync.db"))
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	defer db.Close()
	if _, err := db.CreateTeacher("delivery", "delivery@example.test", "hash", "Delivery"); err != nil {
		t.Fatalf("create teacher: %v", err)
	}

	var received []database.OutboxEvent
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != endpointPath {
			t.Errorf("path = %q; want %q", r.URL.Path, endpointPath)
		}
		if r.Header.Get("Authorization") != "Bearer shared-secret" {
			t.Errorf("authorization = %q", r.Header.Get("Authorization"))
		}
		var body struct {
			Events []database.OutboxEvent `json:"events"`
		}
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Errorf("decode request: %v", err)
		}
		received = append(received, body.Events...)
		w.Header().Set("Content-Type", "application/json")
		w.Write([]byte(`{"success":true}`))
	}))
	defer server.Close()

	manager := New(db, Config{
		CloudURL: server.URL,
		Secret: "shared-secret",
		Interval: time.Hour,
		BatchSize: 50,
	})
	manager.sync(context.Background())

	if len(received) == 0 {
		t.Fatal("cloud endpoint received no events")
	}
	status := manager.Status()
	if status.Outbox.Pending != 0 || status.Outbox.Synced != len(received) {
		t.Fatalf("status = %#v; received %d", status, len(received))
	}
	if status.LastSuccessAt == nil || status.LastError != "" {
		t.Fatalf("unexpected delivery state: %#v", status)
	}
}

func TestManagerRetainsFailedBatchForRetry(t *testing.T) {
	db, err := database.New(filepath.Join(t.TempDir(), "retry.db"))
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	defer db.Close()
	if _, err := db.CreateTeacher("retry", "retry@example.test", "hash", "Retry"); err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		http.Error(w, "temporarily unavailable", http.StatusServiceUnavailable)
	}))
	defer server.Close()

	manager := New(db, Config{CloudURL: server.URL, Secret: "secret", Interval: time.Hour})
	manager.sync(context.Background())
	status := manager.Status()
	if status.Outbox.Pending == 0 || status.Outbox.Failed == 0 {
		t.Fatalf("failed batch was not retained: %#v", status)
	}
	if !strings.Contains(status.LastError, "503") {
		t.Fatalf("last error = %q; want HTTP status", status.LastError)
	}
}

func TestSyncEndpointAcceptsBaseOrCompleteURL(t *testing.T) {
	base, err := syncEndpoint("https://cloud.example/lokal")
	if err != nil || base != "https://cloud.example/lokal"+endpointPath {
		t.Fatalf("base endpoint = %q, %v", base, err)
	}
	complete, err := syncEndpoint("https://cloud.example" + endpointPath)
	if err != nil || complete != "https://cloud.example"+endpointPath {
		t.Fatalf("complete endpoint = %q, %v", complete, err)
	}
}
