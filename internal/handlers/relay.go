package handlers

import (
	"net/http"
)

func (h *Handler) GetRelayStatus(w http.ResponseWriter, _ *http.Request) {
	if h.Relay == nil {
		success(w, map[string]interface{}{
			"enabled": false,
			"state":   "disabled",
		})
		return
	}
	success(w, h.Relay.Status())
}

func (h *Handler) HandleRelayEdge(w http.ResponseWriter, r *http.Request) {
	if h.Relay == nil {
		http.Error(w, "WebSocket relay is unavailable", http.StatusServiceUnavailable)
		return
	}
	h.Relay.ServeEdge(w, r)
}
