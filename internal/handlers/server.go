package handlers

import (
	"net/http"

	"lokal-thesis/internal/serverconfig"
)

func (h *Handler) GetServerStatus(w http.ResponseWriter, _ *http.Request) {
	if h.Server == nil {
		fallback := serverconfig.NewManager("", serverconfig.Default())
		success(w, fallback.Status())
		return
	}
	success(w, h.Server.Status())
}

func (h *Handler) GetServerConfig(w http.ResponseWriter, _ *http.Request) {
	if h.Server == nil {
		errResponse(w, http.StatusServiceUnavailable, "server configuration is unavailable")
		return
	}
	success(w, map[string]interface{}{
		"config": h.Server.Saved(),
		"status": h.Server.Status(),
	})
}

func (h *Handler) UpdateServerConfig(w http.ResponseWriter, r *http.Request) {
	if h.Server == nil {
		errResponse(w, http.StatusServiceUnavailable, "server configuration is unavailable")
		return
	}
	var request serverconfig.Config
	if err := decodeJSON(r, &request); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid server configuration")
		return
	}
	config, restartRequired, err := h.Server.Update(request)
	if err != nil {
		errResponse(w, http.StatusBadRequest, err.Error())
		return
	}
	success(w, map[string]interface{}{
		"config":           config,
		"status":           h.Server.Status(),
		"restart_required": restartRequired,
	})
}
