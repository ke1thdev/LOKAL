package wsrelay

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"
	"lokal-thesis/internal/hub"
)

func TestHostedBrokerRoutesInBothDirectionsWithoutEchoLoop(t *testing.T) {
	websocketHub := hub.NewHub()
	go websocketHub.Run()

	manager := New(nil, websocketHub, Config{
		Secret:      "relay-test-secret",
		NodeID:      "host-node",
		HostEnabled: true,
		QueueSize:   16,
	})
	websocketHub.SetRelayForwarder(manager.Forward)
	server := httptest.NewServer(http.HandlerFunc(manager.ServeEdge))
	defer server.Close()

	headers := http.Header{}
	headers.Set("Authorization", "Bearer relay-test-secret")
	headers.Set("X-LOKAL-Node-ID", "edge-one")
	endpoint := "ws" + strings.TrimPrefix(server.URL, "http")
	edge, _, err := websocket.DefaultDialer.Dial(endpoint, headers)
	if err != nil {
		t.Fatal(err)
	}
	defer edge.Close()

	room := "class:TEST42"
	if err := edge.WriteJSON(Frame{
		Type:    "register",
		Version: protocolVersion,
		NodeID:  "edge-one",
		Rooms:   []string{room},
	}); err != nil {
		t.Fatal(err)
	}

	local := &hub.Client{Hub: websocketHub, Send: make(chan []byte, 8), Room: room, ID: "teacher", IsTeacher: true}
	websocketHub.Register(local)
	defer websocketHub.Unregister(local)
	waitFor(t, time.Second, func() bool {
		status := manager.Status()
		return status.ConnectedEdges == 1 && status.HostedRooms == 1
	})

	inboundPayload := json.RawMessage(`{"type":"participant_joined","room":"class:TEST42","payload":{"id":7}}`)
	if err := edge.WriteJSON(Frame{
		Type:    "event",
		Version: protocolVersion,
		EventID: "edge-event-1",
		NodeID:  "edge-one",
		Room:    room,
		Payload: inboundPayload,
	}); err != nil {
		t.Fatal(err)
	}
	select {
	case got := <-local.Send:
		if string(got) != string(inboundPayload) {
			t.Fatalf("local payload = %s, want %s", got, inboundPayload)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("edge event was not delivered to the hosted room")
	}

	outboundPayload := map[string]any{
		"type":    "stars_updated",
		"room":    room,
		"payload": map[string]any{"participant_id": 7, "stars": 3},
	}
	websocketHub.BroadcastToRoom(room, outboundPayload)
	_ = edge.SetReadDeadline(time.Now().Add(2 * time.Second))
	var frame Frame
	if err := edge.ReadJSON(&frame); err != nil {
		t.Fatalf("host event was not relayed to edge: %v", err)
	}
	if frame.Type != "event" || frame.Room != room || frame.EventID == "" {
		t.Fatalf("unexpected relayed frame: %+v", frame)
	}
	var decoded map[string]any
	if err := json.Unmarshal(frame.Payload, &decoded); err != nil {
		t.Fatal(err)
	}
	if decoded["type"] != "stars_updated" {
		t.Fatalf("relayed event type = %v", decoded["type"])
	}

	// A hosted injection uses BroadcastFromRelay, so it must not immediately
	// appear back on the same edge connection.
	_ = edge.SetReadDeadline(time.Now().Add(150 * time.Millisecond))
	if err := edge.ReadJSON(&frame); err == nil {
		t.Fatal("edge-originated event was echoed back through the relay")
	}
}

func TestHostedBrokerRejectsInvalidSecret(t *testing.T) {
	manager := New(nil, hub.NewHub(), Config{Secret: "expected", HostEnabled: true})
	server := httptest.NewServer(http.HandlerFunc(manager.ServeEdge))
	defer server.Close()

	headers := http.Header{}
	headers.Set("Authorization", "Bearer wrong")
	headers.Set("X-LOKAL-Node-ID", "edge-one")
	endpoint := "ws" + strings.TrimPrefix(server.URL, "http")
	_, response, err := websocket.DefaultDialer.Dial(endpoint, headers)
	if err == nil {
		t.Fatal("relay accepted an invalid secret")
	}
	if response == nil || response.StatusCode != http.StatusUnauthorized {
		t.Fatalf("status = %v, want 401", response)
	}
}

func waitFor(t *testing.T, timeout time.Duration, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if condition() {
			return
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatal("condition was not met before timeout")
}
