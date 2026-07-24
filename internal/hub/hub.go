package hub

import (
	"encoding/json"
	"log"
	"strconv"
	"sync"

	"github.com/gorilla/websocket"
)

// Client represents a WebSocket client
type Client struct {
	Hub       *Hub
	Conn      *websocket.Conn
	Send      chan []byte
	Room      string
	IsTeacher bool
	ID        string // participant ID or teacher ID
}

// Message represents a WebSocket message
type Message struct {
	Type    string          `json:"type"`
	Room    string          `json:"room,omitempty"`
	Payload json.RawMessage `json:"payload,omitempty"`
}

// Hub manages WebSocket connections and rooms
type Hub struct {
	clients    map[*Client]bool
	rooms      map[string]map[*Client]bool
	broadcast  chan []byte
	roomCast   chan *RoomMessage
	register   chan *Client
	unregister chan *Client
	mu         sync.RWMutex
	relayMu    sync.RWMutex
	relay      func(room string, message []byte)
}

// RoomMessage is a message sent to a specific room
type RoomMessage struct {
	Room    string
	Message []byte
	Relay   bool
}

// NewHub creates a new Hub
func NewHub() *Hub {
	return &Hub{
		clients:    make(map[*Client]bool),
		rooms:      make(map[string]map[*Client]bool),
		broadcast:  make(chan []byte, 256),
		roomCast:   make(chan *RoomMessage, 256),
		register:   make(chan *Client),
		unregister: make(chan *Client),
	}
}

// Run starts the hub event loop
func (h *Hub) Run() {
	for {
		select {
		case client := <-h.register:
			h.mu.Lock()
			h.clients[client] = true
			if client.Room != "" {
				if h.rooms[client.Room] == nil {
					h.rooms[client.Room] = make(map[*Client]bool)
				}
				h.rooms[client.Room][client] = true
			}
			h.mu.Unlock()
			log.Printf("[WS] Client connected to room: %s", client.Room)

		case client := <-h.unregister:
			h.mu.Lock()
			if _, ok := h.clients[client]; ok {
				delete(h.clients, client)
				if client.Room != "" {
					delete(h.rooms[client.Room], client)
					if len(h.rooms[client.Room]) == 0 {
						delete(h.rooms, client.Room)
					}
				}
				close(client.Send)
			}
			h.mu.Unlock()
			log.Printf("[WS] Client disconnected from room: %s", client.Room)

		case message := <-h.broadcast:
			h.mu.Lock()
			for client := range h.clients {
				select {
				case client.Send <- message:
				default:
					close(client.Send)
					delete(h.clients, client)
					if client.Room != "" {
						delete(h.rooms[client.Room], client)
					}
				}
			}
			h.mu.Unlock()

		case rm := <-h.roomCast:
			h.mu.Lock()
			if clients, ok := h.rooms[rm.Room]; ok {
				for client := range clients {
					select {
					case client.Send <- rm.Message:
					default:
						close(client.Send)
						delete(clients, client)
						delete(h.clients, client)
					}
				}
				if len(clients) == 0 {
					delete(h.rooms, rm.Room)
				}
			}
			h.mu.Unlock()
			if rm.Relay {
				h.relayMu.RLock()
				forward := h.relay
				h.relayMu.RUnlock()
				if forward != nil {
					// The relay owns its own bounded queue. Keeping this call
					// outside the room lock ensures Internet latency can never
					// stall direct LAN classroom delivery.
					forward(rm.Room, rm.Message)
				}
			}
		}
	}
}

// Register adds a client to the hub
func (h *Hub) Register(client *Client) {
	h.register <- client
}

// Unregister removes a client from the hub
func (h *Hub) Unregister(client *Client) {
	h.unregister <- client
}

// BroadcastToRoom sends a message to all clients in a room
func (h *Hub) BroadcastToRoom(room string, msg interface{}) {
	data, err := json.Marshal(msg)
	if err != nil {
		log.Printf("[WS] Error marshaling message: %v", err)
		return
	}
	h.roomCast <- &RoomMessage{Room: room, Message: data, Relay: true}
}

// BroadcastFromRelay injects a cloud-originated frame into the local room
// without sending it back to the relay. It is the loop-prevention boundary for
// hybrid mode and deliberately preserves the existing classroom JSON payload.
func (h *Hub) BroadcastFromRelay(room string, message []byte) {
	if room == "" || len(message) == 0 {
		return
	}
	h.roomCast <- &RoomMessage{Room: room, Message: append([]byte(nil), message...)}
}

// SetRelayForwarder installs the optional outbound bridge. A nil callback
// restores fully local/offline behavior.
func (h *Hub) SetRelayForwarder(forward func(room string, message []byte)) {
	h.relayMu.Lock()
	h.relay = forward
	h.relayMu.Unlock()
}

// GetRoomClientCount returns the number of clients in a room
func (h *Hub) GetRoomClientCount(room string) int {
	h.mu.RLock()
	defer h.mu.RUnlock()
	if clients, ok := h.rooms[room]; ok {
		return len(clients)
	}
	return 0
}

// GetRoomParticipantIDs returns the currently connected student participant IDs.
// Teacher connections are intentionally excluded so presence-driven features such
// as the Name Picker's "online only" filter reflect students only.
func (h *Hub) GetRoomParticipantIDs(room string) []int64 {
	h.mu.RLock()
	defer h.mu.RUnlock()

	ids := make([]int64, 0)
	seen := make(map[int64]bool)
	for client := range h.rooms[room] {
		if client.IsTeacher || client.ID == "" {
			continue
		}
		id, err := strconv.ParseInt(client.ID, 10, 64)
		if err != nil || seen[id] {
			continue
		}
		seen[id] = true
		ids = append(ids, id)
	}
	return ids
}

// ReadPump reads messages from the WebSocket connection
func (c *Client) ReadPump() {
	defer func() {
		c.Hub.Unregister(c)
		c.Conn.Close()
	}()

	for {
		_, message, err := c.Conn.ReadMessage()
		if err != nil {
			break
		}
		// Parse and handle the message
		var msg Message
		if err := json.Unmarshal(message, &msg); err != nil {
			continue
		}
		// Broadcast to the room
		if msg.Room != "" {
			c.Hub.BroadcastToRoom(msg.Room, msg)
		}
	}
}

// WritePump sends messages to the WebSocket connection
func (c *Client) WritePump() {
	defer c.Conn.Close()
	for {
		message, ok := <-c.Send
		if !ok {
			c.Conn.WriteMessage(websocket.CloseMessage, []byte{})
			return
		}
		if err := c.Conn.WriteMessage(websocket.TextMessage, message); err != nil {
			return
		}
	}
}
