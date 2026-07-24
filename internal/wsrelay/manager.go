package wsrelay

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"os"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/gorilla/websocket"
	"lokal-thesis/internal/database"
	"lokal-thesis/internal/hub"
)

const protocolVersion = 1

type Frame struct {
	Type      string          `json:"type"`
	Version   int             `json:"version,omitempty"`
	EventID   string          `json:"event_id,omitempty"`
	NodeID    string          `json:"node_id,omitempty"`
	Rooms     []string        `json:"rooms,omitempty"`
	Room      string          `json:"room,omitempty"`
	Payload   json.RawMessage `json:"payload,omitempty"`
	CreatedAt time.Time       `json:"created_at,omitempty"`
}

type Status struct {
	Enabled         bool       `json:"enabled"`
	EdgeEnabled     bool       `json:"edge_enabled"`
	HostEnabled     bool       `json:"host_enabled"`
	State           string     `json:"state"`
	NodeID          string     `json:"node_id,omitempty"`
	RelayURL        string     `json:"relay_url,omitempty"`
	Connected       bool       `json:"connected"`
	RegisteredRooms int        `json:"registered_rooms"`
	ConnectedEdges  int        `json:"connected_edges"`
	HostedRooms     int        `json:"hosted_rooms"`
	Queued          int        `json:"queued"`
	Dropped         uint64     `json:"dropped"`
	RelayedOutbound uint64     `json:"relayed_outbound"`
	RelayedInbound  uint64     `json:"relayed_inbound"`
	LastConnectedAt *time.Time `json:"last_connected_at,omitempty"`
	LastEventAt     *time.Time `json:"last_event_at,omitempty"`
	LastError       string     `json:"last_error,omitempty"`
}

type Manager struct {
	cfg      Config
	db       *database.DB
	hub      *hub.Hub
	broker   *broker
	outbound chan Frame

	ctx    context.Context
	cancel context.CancelFunc

	statusMu sync.RWMutex
	status   Status

	recentMu sync.Mutex
	recent   map[string]time.Time

	dropped       atomic.Uint64
	outboundCount atomic.Uint64
	inboundCount  atomic.Uint64
}

func New(db *database.DB, websocketHub *hub.Hub, cfg Config) *Manager {
	if cfg.QueueSize <= 0 {
		cfg.QueueSize = 1024
	}
	if cfg.ConnectTimeout <= 0 {
		cfg.ConnectTimeout = 10 * time.Second
	}
	if cfg.RefreshInterval <= 0 {
		cfg.RefreshInterval = 30 * time.Second
	}
	if cfg.MinBackoff <= 0 {
		cfg.MinBackoff = time.Second
	}
	if cfg.MaxBackoff < cfg.MinBackoff {
		cfg.MaxBackoff = 30 * time.Second
	}
	m := &Manager{
		cfg:      cfg,
		db:       db,
		hub:      websocketHub,
		outbound: make(chan Frame, cfg.QueueSize),
		recent:   make(map[string]time.Time),
	}
	m.status = Status{
		Enabled:     cfg.Active(),
		EdgeEnabled: cfg.EdgeEnabled(),
		HostEnabled: cfg.HostEnabled && strings.TrimSpace(cfg.Secret) != "",
		State:       "disabled",
		RelayURL:    sanitizeRelayURL(cfg.URL),
	}
	if m.status.HostEnabled {
		m.broker = newBroker(m)
		m.status.State = "hosting"
	}
	return m
}

func (m *Manager) Active() bool { return m.cfg.Active() }

func (m *Manager) Start(parent context.Context) {
	if !m.Active() {
		return
	}
	m.ctx, m.cancel = context.WithCancel(parent)
	if m.cfg.EdgeEnabled() {
		go m.runEdge()
	}
}

func (m *Manager) Stop() {
	if m.cancel != nil {
		m.cancel()
	}
}

// Forward is installed on hub.Hub. It never blocks the classroom delivery
// loop: Internet outages only increase the dropped counter once the bounded
// relay queue is full.
func (m *Manager) Forward(room string, message []byte) {
	if room == "" || len(message) == 0 || !json.Valid(message) {
		return
	}
	frame := Frame{
		Type:      "event",
		Version:   protocolVersion,
		EventID:   randomID(),
		NodeID:    m.nodeID(),
		Room:      room,
		Payload:   append(json.RawMessage(nil), message...),
		CreatedAt: time.Now().UTC(),
	}
	if m.broker != nil {
		m.broker.publish(frame, nil)
	}
	if m.cfg.EdgeEnabled() {
		select {
		case m.outbound <- frame:
		default:
			m.dropped.Add(1)
			m.setError("relay queue is full; newest real-time event was dropped")
		}
	}
}

func (m *Manager) Status() Status {
	m.statusMu.RLock()
	status := m.status
	m.statusMu.RUnlock()
	status.Queued = len(m.outbound)
	status.Dropped = m.dropped.Load()
	status.RelayedOutbound = m.outboundCount.Load()
	status.RelayedInbound = m.inboundCount.Load()
	if m.broker != nil {
		status.ConnectedEdges, status.HostedRooms = m.broker.counts()
	}
	return status
}

func (m *Manager) ServeEdge(w http.ResponseWriter, r *http.Request) {
	if m.broker == nil {
		http.Error(w, "hosted WebSocket relay is disabled", http.StatusServiceUnavailable)
		return
	}
	m.broker.serve(w, r)
}

func (m *Manager) runEdge() {
	endpoint, err := m.cfg.relayEndpoint()
	if err != nil {
		m.setError("invalid relay URL: " + err.Error())
		return
	}
	nodeID := m.nodeID()
	if nodeID == "" {
		m.setError("relay node identity is unavailable; set LOKAL_RELAY_NODE_ID")
		return
	}
	m.statusMu.Lock()
	m.status.NodeID = nodeID
	m.status.State = "connecting"
	m.statusMu.Unlock()

	backoff := m.cfg.MinBackoff
	for {
		if m.ctx.Err() != nil {
			m.setDisconnected("stopped")
			return
		}
		err = m.connect(endpoint, nodeID)
		if m.ctx.Err() != nil {
			m.setDisconnected("stopped")
			return
		}
		if err != nil {
			m.setError(err.Error())
			log.Printf("[Relay] Edge connection unavailable: %v; retrying in %s", err, backoff)
		}
		timer := time.NewTimer(backoff)
		select {
		case <-m.ctx.Done():
			timer.Stop()
			m.setDisconnected("stopped")
			return
		case <-timer.C:
		}
		backoff *= 2
		if backoff > m.cfg.MaxBackoff {
			backoff = m.cfg.MaxBackoff
		}
	}
}

func (m *Manager) connect(endpoint, nodeID string) error {
	dialer := websocket.Dialer{
		HandshakeTimeout: m.cfg.ConnectTimeout,
		Proxy:            http.ProxyFromEnvironment,
	}
	headers := http.Header{}
	headers.Set("Authorization", "Bearer "+m.cfg.Secret)
	headers.Set("X-LOKAL-Node-ID", nodeID)
	conn, response, err := dialer.DialContext(m.ctx, endpoint, headers)
	if err != nil {
		if response != nil {
			return fmt.Errorf("relay handshake failed: %s", response.Status)
		}
		return fmt.Errorf("connect relay: %w", err)
	}
	defer conn.Close()
	conn.SetReadLimit(2 << 20)
	_ = conn.SetReadDeadline(time.Now().Add(75 * time.Second))
	conn.SetPongHandler(func(string) error {
		return conn.SetReadDeadline(time.Now().Add(75 * time.Second))
	})

	rooms, err := m.db.RelayRooms()
	if err != nil {
		return err
	}
	register := Frame{Type: "register", Version: protocolVersion, NodeID: nodeID, Rooms: rooms}
	if err := writeFrame(conn, register); err != nil {
		return err
	}
	now := time.Now().UTC()
	m.statusMu.Lock()
	m.status.State = "connected"
	m.status.Connected = true
	m.status.RegisteredRooms = len(rooms)
	m.status.LastConnectedAt = &now
	m.status.LastError = ""
	m.statusMu.Unlock()
	log.Printf("[Relay] Connected to %s with %d classroom room(s)", sanitizeRelayURL(endpoint), len(rooms))

	incoming := make(chan Frame, 32)
	readErr := make(chan error, 1)
	go func() {
		for {
			var frame Frame
			if err := conn.ReadJSON(&frame); err != nil {
				readErr <- err
				return
			}
			select {
			case incoming <- frame:
			case <-m.ctx.Done():
				return
			}
		}
	}()

	refresh := time.NewTicker(m.cfg.RefreshInterval)
	ping := time.NewTicker(25 * time.Second)
	defer refresh.Stop()
	defer ping.Stop()
	for {
		select {
		case <-m.ctx.Done():
			_ = conn.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(websocket.CloseNormalClosure, "server stopping"), time.Now().Add(time.Second))
			return m.ctx.Err()
		case err := <-readErr:
			m.setDisconnected("connecting")
			return fmt.Errorf("relay connection closed: %w", err)
		case frame := <-incoming:
			m.receive(frame)
		case frame := <-m.outbound:
			frame.NodeID = nodeID
			if err := writeFrame(conn, frame); err != nil {
				m.requeue(frame)
				m.setDisconnected("connecting")
				return fmt.Errorf("send relay event: %w", err)
			}
			m.outboundCount.Add(1)
			m.touchEvent()
		case <-refresh.C:
			rooms, err = m.db.RelayRooms()
			if err != nil {
				m.setError(err.Error())
				continue
			}
			if err := writeFrame(conn, Frame{Type: "register", Version: protocolVersion, NodeID: nodeID, Rooms: rooms}); err != nil {
				m.setDisconnected("connecting")
				return err
			}
			m.statusMu.Lock()
			m.status.RegisteredRooms = len(rooms)
			m.statusMu.Unlock()
		case <-ping.C:
			if err := conn.WriteControl(websocket.PingMessage, nil, time.Now().Add(5*time.Second)); err != nil {
				m.setDisconnected("connecting")
				return err
			}
		}
	}
}

func (m *Manager) receive(frame Frame) {
	if frame.Type != "event" || frame.Room == "" || len(frame.Payload) == 0 || !json.Valid(frame.Payload) {
		return
	}
	if frame.EventID != "" && m.seen(frame.EventID) {
		return
	}
	m.inboundCount.Add(1)
	m.touchEvent()
	m.hub.BroadcastFromRelay(frame.Room, frame.Payload)
}

func (m *Manager) acceptFromEdge(frame Frame, source *edgePeer) {
	if frame.Type != "event" || frame.Room == "" || len(frame.Payload) == 0 || !json.Valid(frame.Payload) {
		return
	}
	if frame.EventID == "" {
		frame.EventID = randomID()
	}
	if m.seen(frame.EventID) {
		return
	}
	frame.Version = protocolVersion
	frame.NodeID = source.nodeID
	if frame.CreatedAt.IsZero() {
		frame.CreatedAt = time.Now().UTC()
	}
	m.inboundCount.Add(1)
	m.touchEvent()
	m.broker.publish(frame, source)
	m.hub.BroadcastFromRelay(frame.Room, frame.Payload)
}

func (m *Manager) nodeID() string {
	if id := strings.TrimSpace(m.cfg.NodeID); id != "" {
		return id
	}
	if m.db != nil {
		id, err := m.db.RelayNodeID()
		if err == nil && id != "" {
			return id
		}
	}
	if m.cfg.HostEnabled {
		hostname, hostErr := os.Hostname()
		if hostErr == nil && strings.TrimSpace(hostname) != "" {
			return "hosted-" + strings.ToLower(strings.TrimSpace(hostname))
		}
		return "hosted-lokal"
	}
	return ""
}

func (m *Manager) seen(id string) bool {
	now := time.Now()
	m.recentMu.Lock()
	defer m.recentMu.Unlock()
	if previous, exists := m.recent[id]; exists && now.Sub(previous) < 10*time.Minute {
		return true
	}
	m.recent[id] = now
	if len(m.recent) > 4096 {
		cutoff := now.Add(-10 * time.Minute)
		for key, at := range m.recent {
			if at.Before(cutoff) {
				delete(m.recent, key)
			}
		}
	}
	return false
}

func (m *Manager) requeue(frame Frame) {
	select {
	case m.outbound <- frame:
	default:
		m.dropped.Add(1)
	}
}

func (m *Manager) setError(message string) {
	m.statusMu.Lock()
	m.status.Connected = false
	if m.status.HostEnabled && !m.status.EdgeEnabled {
		m.status.State = "hosting"
	} else {
		m.status.State = "attention"
	}
	m.status.LastError = message
	m.statusMu.Unlock()
}

func (m *Manager) setDisconnected(state string) {
	m.statusMu.Lock()
	m.status.Connected = false
	m.status.State = state
	m.statusMu.Unlock()
}

func (m *Manager) touchEvent() {
	now := time.Now().UTC()
	m.statusMu.Lock()
	m.status.LastEventAt = &now
	m.statusMu.Unlock()
}

func writeFrame(conn *websocket.Conn, frame Frame) error {
	_ = conn.SetWriteDeadline(time.Now().Add(10 * time.Second))
	return conn.WriteJSON(frame)
}

func randomID() string {
	var value [16]byte
	if _, err := rand.Read(value[:]); err != nil {
		return fmt.Sprintf("%d", time.Now().UnixNano())
	}
	return hex.EncodeToString(value[:])
}

func sanitizeRelayURL(raw string) string {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return ""
	}
	if at := strings.Index(raw, "@"); at >= 0 {
		if scheme := strings.Index(raw, "://"); scheme >= 0 {
			raw = raw[:scheme+3] + raw[at+1:]
		}
	}
	return raw
}
