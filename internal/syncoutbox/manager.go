package syncoutbox

import (
	"bytes"
	"context"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"lokal-thesis/internal/database"
)

const endpointPath = "/api/v1/sync/outbox"

type Config struct {
	CloudURL string
	Secret   string
	Interval time.Duration
	BatchSize int
}

func ConfigFromEnvironment() Config {
	interval := 5 * time.Second
	if value := strings.TrimSpace(os.Getenv("LOKAL_SYNC_INTERVAL")); value != "" {
		if parsed, err := time.ParseDuration(value); err == nil && parsed >= time.Second {
			interval = parsed
		}
	}
	return Config{
		CloudURL: strings.TrimSpace(os.Getenv("LOKAL_CLOUD_SYNC_URL")),
		Secret: strings.TrimSpace(os.Getenv("LOKAL_SYNC_SECRET")),
		Interval: interval,
		BatchSize: 100,
	}
}

type Status struct {
	Enabled       bool                  `json:"enabled"`
	Provider      string                `json:"provider"`
	CloudURL      string                `json:"cloud_url,omitempty"`
	State         string                `json:"state"`
	LastAttemptAt *time.Time            `json:"last_attempt_at,omitempty"`
	LastSuccessAt *time.Time            `json:"last_success_at,omitempty"`
	LastError     string                `json:"last_error,omitempty"`
	Outbox        database.OutboxStats  `json:"outbox"`
}

type Manager struct {
	db       *database.DB
	config   Config
	client   *http.Client
	trigger  chan struct{}
	running  atomic.Bool
	mu       sync.RWMutex
	lastAttempt *time.Time
	lastSuccess *time.Time
	lastError string
}

func New(db *database.DB, config Config) *Manager {
	if config.BatchSize <= 0 || config.BatchSize > 500 {
		config.BatchSize = 100
	}
	if config.Interval < time.Second {
		config.Interval = 5 * time.Second
	}
	return &Manager{
		db: db, config: config,
		client: &http.Client{Timeout: 20 * time.Second},
		trigger: make(chan struct{}, 1),
	}
}

func (m *Manager) Enabled() bool {
	return m != nil && m.db.ProviderName() == database.DefaultProvider &&
		m.config.CloudURL != "" && m.config.Secret != ""
}

func (m *Manager) ReceiverEnabled() bool {
	return m != nil && m.db.ProviderName() == "postgres" && m.config.Secret != ""
}

func (m *Manager) Receive(secret string, events []database.OutboxEvent) error {
	if !m.ReceiverEnabled() {
		return errors.New("cloud outbox receiver is not configured")
	}
	if subtle.ConstantTimeCompare([]byte(secret), []byte(m.config.Secret)) != 1 {
		return errors.New("invalid synchronization credential")
	}
	if len(events) == 0 {
		return errors.New("events are required")
	}
	if len(events) > 500 {
		return errors.New("batch exceeds 500 events")
	}
	return m.db.ApplyOutboxEvents(events)
}

func (m *Manager) Start(ctx context.Context) {
	if !m.Enabled() {
		return
	}
	log.Printf("[Sync] Local outbox enabled; cloud endpoint %s", sanitizeURL(m.config.CloudURL))
	go m.loop(ctx)
}

func (m *Manager) Trigger() bool {
	if !m.Enabled() {
		return false
	}
	select {
	case m.trigger <- struct{}{}:
	default:
	}
	return true
}

func (m *Manager) Status() Status {
	status := Status{
		Enabled: m.Enabled(),
		Provider: m.db.ProviderName(),
		CloudURL: sanitizeURL(m.config.CloudURL),
		State: "disabled",
	}
	stats, err := m.db.OutboxStats()
	if err == nil {
		status.Outbox = stats
	}
	m.mu.RLock()
	status.LastAttemptAt, status.LastSuccessAt, status.LastError =
		m.lastAttempt, m.lastSuccess, m.lastError
	m.mu.RUnlock()
	switch {
	case m.running.Load():
		status.State = "syncing"
	case status.Enabled && status.LastError != "":
		status.State = "attention"
	case status.Enabled && stats.Pending > 0:
		status.State = "pending"
	case status.Enabled:
		status.State = "up_to_date"
	case m.db.ProviderName() == "postgres":
		if m.ReceiverEnabled() {
			status.State = "cloud_receiver"
			status.Enabled = true
		}
	}
	return status
}

func (m *Manager) loop(ctx context.Context) {
	ticker := time.NewTicker(m.config.Interval)
	defer ticker.Stop()
	m.sync(ctx)
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			m.sync(ctx)
		case <-m.trigger:
			m.sync(ctx)
		}
	}
}

func (m *Manager) sync(ctx context.Context) {
	if !m.running.CompareAndSwap(false, true) {
		return
	}
	defer m.running.Store(false)
	for {
		events, err := m.db.PendingOutbox(m.config.BatchSize)
		if err != nil {
			m.recordFailure(err)
			return
		}
		if len(events) == 0 {
			return
		}
		now := time.Now().UTC()
		m.mu.Lock()
		m.lastAttempt = &now
		m.mu.Unlock()
		if err := m.deliver(ctx, events); err != nil {
			ids := eventIDs(events)
			if markErr := m.db.MarkOutboxFailed(ids, err.Error()); markErr != nil {
				log.Printf("[Sync] Could not mark failed batch: %v", markErr)
			}
			m.recordFailure(err)
			return
		}
		if err := m.db.MarkOutboxSynced(eventIDs(events)); err != nil {
			m.recordFailure(err)
			return
		}
		m.mu.Lock()
		m.lastSuccess = &now
		m.lastError = ""
		m.mu.Unlock()
		log.Printf("[Sync] Delivered %d local change(s)", len(events))
		if len(events) < m.config.BatchSize {
			return
		}
	}
}

func (m *Manager) deliver(ctx context.Context, events []database.OutboxEvent) error {
	body, err := json.Marshal(map[string]any{"events": events})
	if err != nil {
		return err
	}
	target, err := syncEndpoint(m.config.CloudURL)
	if err != nil {
		return err
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, target, bytes.NewReader(body))
	if err != nil {
		return err
	}
	request.Header.Set("Content-Type", "application/json")
	request.Header.Set("Authorization", "Bearer "+m.config.Secret)
	response, err := m.client.Do(request)
	if err != nil {
		return fmt.Errorf("reach cloud sync endpoint: %w", err)
	}
	defer response.Body.Close()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		message, _ := io.ReadAll(io.LimitReader(response.Body, 2048))
		return fmt.Errorf("cloud sync returned %s: %s", response.Status, strings.TrimSpace(string(message)))
	}
	return nil
}

func (m *Manager) recordFailure(err error) {
	if err == nil {
		return
	}
	m.mu.Lock()
	m.lastError = err.Error()
	m.mu.Unlock()
	log.Printf("[Sync] %v", err)
}

func eventIDs(events []database.OutboxEvent) []int64 {
	ids := make([]int64, len(events))
	for index := range events {
		ids[index] = events[index].ID
	}
	return ids
}

func syncEndpoint(raw string) (string, error) {
	parsed, err := url.Parse(strings.TrimSpace(raw))
	if err != nil || parsed.Scheme == "" || parsed.Host == "" {
		return "", errors.New("LOKAL_CLOUD_SYNC_URL must be an absolute HTTP(S) URL")
	}
	if strings.HasSuffix(parsed.Path, endpointPath) {
		return parsed.String(), nil
	}
	parsed.Path = strings.TrimRight(parsed.Path, "/") + endpointPath
	return parsed.String(), nil
}

func sanitizeURL(raw string) string {
	parsed, err := url.Parse(strings.TrimSpace(raw))
	if err != nil || parsed.Host == "" {
		return ""
	}
	parsed.User = nil
	return parsed.String()
}
