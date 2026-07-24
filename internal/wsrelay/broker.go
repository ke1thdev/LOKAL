package wsrelay

import (
	"crypto/subtle"
	"log"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

type edgePeer struct {
	broker *broker
	conn   *websocket.Conn
	send   chan Frame
	done   chan struct{}
	nodeID string

	mu    sync.RWMutex
	rooms map[string]struct{}
}

type broker struct {
	manager *Manager

	mu    sync.RWMutex
	peers map[*edgePeer]struct{}
	rooms map[string]map[*edgePeer]struct{}
}

func newBroker(manager *Manager) *broker {
	return &broker{
		manager: manager,
		peers:   make(map[*edgePeer]struct{}),
		rooms:   make(map[string]map[*edgePeer]struct{}),
	}
}

var relayUpgrader = websocket.Upgrader{
	ReadBufferSize:  4096,
	WriteBufferSize: 4096,
	CheckOrigin: func(*http.Request) bool {
		return true
	},
}

func (b *broker) serve(w http.ResponseWriter, r *http.Request) {
	if !validRelaySecret(r, b.manager.cfg.Secret) {
		http.Error(w, "invalid relay credentials", http.StatusUnauthorized)
		return
	}
	nodeID := strings.TrimSpace(r.Header.Get("X-LOKAL-Node-ID"))
	if nodeID == "" {
		http.Error(w, "relay node id is required", http.StatusBadRequest)
		return
	}
	conn, err := relayUpgrader.Upgrade(w, r, nil)
	if err != nil {
		return
	}
	peer := &edgePeer{
		broker: b,
		conn:   conn,
		send:   make(chan Frame, 256),
		done:   make(chan struct{}),
		nodeID: nodeID,
		rooms:  make(map[string]struct{}),
	}
	b.add(peer)
	log.Printf("[Relay] Edge %s connected", nodeID)
	go peer.writePump()
	peer.readPump()
}

func (p *edgePeer) readPump() {
	defer func() {
		p.broker.remove(p)
		_ = p.conn.Close()
		log.Printf("[Relay] Edge %s disconnected", p.nodeID)
	}()
	p.conn.SetReadLimit(2 << 20)
	_ = p.conn.SetReadDeadline(time.Now().Add(75 * time.Second))
	p.conn.SetPongHandler(func(string) error {
		return p.conn.SetReadDeadline(time.Now().Add(75 * time.Second))
	})
	for {
		var frame Frame
		if err := p.conn.ReadJSON(&frame); err != nil {
			return
		}
		switch frame.Type {
		case "register":
			if frame.Version != 0 && frame.Version != protocolVersion {
				continue
			}
			p.broker.registerRooms(p, frame.Rooms)
		case "event":
			if !p.hasRoom(frame.Room) {
				continue
			}
			p.broker.manager.acceptFromEdge(frame, p)
		case "ping":
			p.enqueue(Frame{Type: "pong", Version: protocolVersion})
		}
	}
}

func (p *edgePeer) writePump() {
	ping := time.NewTicker(25 * time.Second)
	defer ping.Stop()
	for {
		select {
		case <-p.done:
			return
		case frame, ok := <-p.send:
			if !ok {
				_ = p.conn.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(websocket.CloseNormalClosure, "relay closed"), time.Now().Add(time.Second))
				return
			}
			if err := writeFrame(p.conn, frame); err != nil {
				_ = p.conn.Close()
				return
			}
		case <-ping.C:
			if err := p.conn.WriteControl(websocket.PingMessage, nil, time.Now().Add(5*time.Second)); err != nil {
				_ = p.conn.Close()
				return
			}
		}
	}
}

func (p *edgePeer) enqueue(frame Frame) {
	select {
	case <-p.done:
		return
	case p.send <- frame:
	default:
		p.broker.manager.dropped.Add(1)
		_ = p.conn.Close()
	}
}

func (p *edgePeer) hasRoom(room string) bool {
	p.mu.RLock()
	_, ok := p.rooms[room]
	p.mu.RUnlock()
	return ok
}

func (b *broker) add(peer *edgePeer) {
	b.mu.Lock()
	b.peers[peer] = struct{}{}
	b.mu.Unlock()
}

func (b *broker) remove(peer *edgePeer) {
	b.mu.Lock()
	if _, exists := b.peers[peer]; !exists {
		b.mu.Unlock()
		return
	}
	delete(b.peers, peer)
	for room, peers := range b.rooms {
		delete(peers, peer)
		if len(peers) == 0 {
			delete(b.rooms, room)
		}
	}
	b.mu.Unlock()
	close(peer.done)
}

func (b *broker) registerRooms(peer *edgePeer, rooms []string) {
	clean := make(map[string]struct{})
	for _, room := range rooms {
		room = strings.TrimSpace(room)
		if strings.HasPrefix(room, "class:") && len(room) > len("class:") {
			clean[room] = struct{}{}
		}
	}
	b.mu.Lock()
	for room, peers := range b.rooms {
		delete(peers, peer)
		if len(peers) == 0 {
			delete(b.rooms, room)
		}
	}
	for room := range clean {
		if b.rooms[room] == nil {
			b.rooms[room] = make(map[*edgePeer]struct{})
		}
		b.rooms[room][peer] = struct{}{}
	}
	b.mu.Unlock()
	peer.mu.Lock()
	peer.rooms = clean
	peer.mu.Unlock()
}

func (b *broker) publish(frame Frame, source *edgePeer) {
	b.mu.RLock()
	registered := b.rooms[frame.Room]
	peers := make([]*edgePeer, 0, len(registered))
	for peer := range registered {
		if peer != source {
			peers = append(peers, peer)
		}
	}
	b.mu.RUnlock()
	for _, peer := range peers {
		peer.enqueue(frame)
		b.manager.outboundCount.Add(1)
	}
}

func (b *broker) counts() (int, int) {
	b.mu.RLock()
	defer b.mu.RUnlock()
	return len(b.peers), len(b.rooms)
}

func validRelaySecret(r *http.Request, expected string) bool {
	provided := strings.TrimSpace(strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer "))
	if provided == "" {
		provided = strings.TrimSpace(r.Header.Get("X-LOKAL-Relay-Secret"))
	}
	if provided == "" || expected == "" || len(provided) != len(expected) {
		return false
	}
	return subtle.ConstantTimeCompare([]byte(provided), []byte(expected)) == 1
}
