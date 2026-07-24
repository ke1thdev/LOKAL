package handlers

import (
	"net/http"
	"strconv"
	"strings"

	"lokal-thesis/internal/middleware"
)

func (h *Handler) ownedClassID(w http.ResponseWriter, r *http.Request) (int64, bool) {
	classID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return 0, false
	}
	class, err := h.DB.GetClassByID(classID)
	if err != nil || class.TeacherID != middleware.GetTeacherID(r) {
		errResponse(w, http.StatusNotFound, "class not found")
		return 0, false
	}
	return classID, true
}

func (h *Handler) GetGroups(w http.ResponseWriter, r *http.Request) {
	classID, ok := h.ownedClassID(w, r)
	if !ok {
		return
	}
	groups, err := h.DB.GetGroupsByClass(classID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get groups")
		return
	}
	success(w, groups)
}

func (h *Handler) CreateGroup(w http.ResponseWriter, r *http.Request) {
	classID, ok := h.ownedClassID(w, r)
	if !ok {
		return
	}
	var req struct {
		Name  string `json:"name"`
		Color string `json:"color"`
	}
	if decodeJSON(r, &req) != nil || strings.TrimSpace(req.Name) == "" {
		errResponse(w, http.StatusBadRequest, "group name is required")
		return
	}
	if req.Color == "" {
		req.Color = "#0B1F1C"
	}
	group, err := h.DB.CreateGroup(classID, strings.TrimSpace(req.Name), req.Color)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to create group")
		return
	}
	created(w, group)
}

func (h *Handler) UpdateGroup(w http.ResponseWriter, r *http.Request) {
	classID, ok := h.ownedClassID(w, r)
	if !ok {
		return
	}
	groupID, err := strconv.ParseInt(r.PathValue("group_id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid group ID")
		return
	}
	var req struct {
		Name  string `json:"name"`
		Color string `json:"color"`
	}
	if decodeJSON(r, &req) != nil || strings.TrimSpace(req.Name) == "" {
		errResponse(w, http.StatusBadRequest, "group name is required")
		return
	}
	if err := h.DB.UpdateGroup(groupID, classID, strings.TrimSpace(req.Name), req.Color); err != nil {
		errResponse(w, http.StatusNotFound, "group not found")
		return
	}
	success(w, map[string]string{"message": "group updated"})
}

func (h *Handler) DeleteGroup(w http.ResponseWriter, r *http.Request) {
	classID, ok := h.ownedClassID(w, r)
	if !ok {
		return
	}
	groupID, err := strconv.ParseInt(r.PathValue("group_id"), 10, 64)
	if err != nil || h.DB.DeleteGroup(groupID, classID) != nil {
		errResponse(w, http.StatusNotFound, "group not found")
		return
	}
	success(w, map[string]string{"message": "group deleted"})
}

func (h *Handler) SetParticipantGroup(w http.ResponseWriter, r *http.Request) {
	classID, ok := h.ownedClassID(w, r)
	if !ok {
		return
	}
	participantID, err := strconv.ParseInt(r.PathValue("participant_id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid participant ID")
		return
	}
	var req struct {
		GroupID int64 `json:"group_id"`
	}
	if decodeJSON(r, &req) != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if err := h.DB.SetParticipantGroup(classID, participantID, req.GroupID); err != nil {
		errResponse(w, http.StatusNotFound, "participant or group not found")
		return
	}
	success(w, map[string]string{"message": "participant group updated"})
}
