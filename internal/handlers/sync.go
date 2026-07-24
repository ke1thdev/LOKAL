package handlers

import (
	"net/http"
	"strings"

	"lokal-thesis/internal/database"
)

func (h *Handler) GetSyncStatus(w http.ResponseWriter, _ *http.Request) {
	if h.Sync == nil {
		errResponse(w, http.StatusServiceUnavailable, "synchronization service is unavailable")
		return
	}
	success(w, h.Sync.Status())
}

func (h *Handler) RunSyncNow(w http.ResponseWriter, _ *http.Request) {
	if h.Sync == nil || !h.Sync.Trigger() {
		errResponse(w, http.StatusConflict, "cloud synchronization is not configured on this local server")
		return
	}
	success(w, map[string]bool{"queued": true})
}

func (h *Handler) ReceiveOutbox(w http.ResponseWriter, r *http.Request) {
	if h.Sync == nil {
		errResponse(w, http.StatusServiceUnavailable, "synchronization service is unavailable")
		return
	}
	var request struct {
		Events []database.OutboxEvent `json:"events"`
	}
	r.Body = http.MaxBytesReader(w, r.Body, 5<<20)
	if err := decodeJSON(r, &request); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid outbox batch")
		return
	}
	secret := strings.TrimSpace(strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer "))
	if err := h.Sync.Receive(secret, request.Events); err != nil {
		status := http.StatusBadRequest
		switch err.Error() {
		case "invalid synchronization credential":
			status = http.StatusUnauthorized
		case "cloud outbox receiver is not configured":
			status = http.StatusServiceUnavailable
		}
		errResponse(w, status, err.Error())
		return
	}
	success(w, map[string]int{"accepted": len(request.Events)})
}
