package handlers

import (
	"database/sql"
	"net/http"
	"strconv"

	"lokal-thesis/internal/middleware"
	"lokal-thesis/internal/models"
)

// GetClasses returns all classes for the authenticated teacher
func (h *Handler) GetClasses(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)
	classes, err := h.DB.GetClassesByTeacher(teacherID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get classes")
		return
	}
	if classes == nil {
		classes = []models.Class{}
	}
	success(w, classes)
}

// GetClass returns a single class by ID
func (h *Handler) GetClass(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	class, err := h.DB.GetClassByID(id)
	if err != nil {
		errResponse(w, http.StatusNotFound, "class not found")
		return
	}
	success(w, class)
}

// GetOnlineParticipants returns participant IDs with an active student WebSocket
// in the class room. It is used by presentation tools that can filter by presence.
func (h *Handler) GetOnlineParticipants(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	class, err := h.DB.GetClassByID(id)
	if err != nil {
		errResponse(w, http.StatusNotFound, "class not found")
		return
	}
	ids := h.Hub.GetRoomParticipantIDs("class:" + class.Code)
	success(w, ids)
}

// CreateClass creates a new class
func (h *Handler) CreateClass(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)

	var req models.CreateClassRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	if req.Name == "" || req.Code == "" {
		errResponse(w, http.StatusBadRequest, "name and code are required")
		return
	}

	if len(req.Code) < 4 || len(req.Code) > 8 {
		errResponse(w, http.StatusBadRequest, "code must be 4-8 characters")
		return
	}

	if req.AvatarColor == "" {
		req.AvatarColor = "#F97316"
	}

	class, err := h.DB.CreateClass(teacherID, req.Name, req.Code, req.AvatarColor)
	if err != nil {
		errResponse(w, http.StatusConflict, "class code already exists")
		return
	}
	created(w, class)
}

// UpdateClass updates an existing class
func (h *Handler) UpdateClass(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}

	var req models.CreateClassRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	class, err := h.DB.UpdateClass(id, req.Name, req.Code, req.AvatarColor)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to update class")
		return
	}
	success(w, class)
}

// SetClassLock controls whether new students may join the current class.
func (h *Handler) SetClassLock(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	var req struct {
		Locked bool `json:"locked"`
	}
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if err := h.DB.SetClassLocked(id, req.Locked); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to update class lock")
		return
	}
	success(w, map[string]bool{"is_locked": req.Locked})
}

// DeleteClass deletes a class
func (h *Handler) DeleteClass(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	if err := h.DB.DeleteClass(id); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to delete class")
		return
	}
	success(w, map[string]string{"message": "class deleted"})
}

// GetParticipants returns all participants for a class
func (h *Handler) GetParticipants(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	participants, err := h.DB.GetParticipantsByClass(id)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get participants")
		return
	}
	if participants == nil {
		participants = []models.Participant{}
	}
	success(w, participants)
}

// AddParticipant manually adds a participant to a class
func (h *Handler) AddParticipant(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}

	var req struct {
		Name string `json:"name"`
	}
	if err := decodeJSON(r, &req); err != nil || req.Name == "" {
		errResponse(w, http.StatusBadRequest, "name is required")
		return
	}

	// Add participant to database without avatar manually
	participant, err := h.DB.AddParticipant(id, req.Name, "", "")
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to add participant")
		return
	}
	created(w, participant)
}

// EditParticipant updates a participant's name
func (h *Handler) EditParticipant(w http.ResponseWriter, r *http.Request) {
	classID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}

	participantID, err := strconv.ParseInt(r.PathValue("participant_id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid participant ID")
		return
	}

	var req struct {
		Name      string `json:"name"`
		Stars     int    `json:"stars"`
		AvatarUrl string `json:"avatar_url"`
	}
	if err := decodeJSON(r, &req); err != nil || req.Name == "" {
		errResponse(w, http.StatusBadRequest, "name is required")
		return
	}

	// Verify participant belongs to class
	p, err := h.DB.GetParticipantByID(participantID)
	if err != nil || p.ClassID != classID {
		errResponse(w, http.StatusNotFound, "participant not found in this class")
		return
	}

	// Prevent negative stars
	if req.Stars < 0 {
		req.Stars = 0
	}

	teacherID := middleware.GetTeacherID(r)

	// Calculate new level based on total stars
	starLevels, err := h.DB.GetStarLevels(teacherID)
	newLevel := 1
	if err == nil && starLevels != nil {
		for _, sl := range starLevels {
			if req.Stars >= sl.StarsRequired {
				newLevel = sl.Level
			}
		}
	}

	if err := h.DB.UpdateParticipant(participantID, req.Name, req.Stars, newLevel, req.AvatarUrl); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to update participant")
		return
	}
	success(w, map[string]string{"message": "participant updated"})
}

// DeleteParticipant deletes a participant
func (h *Handler) DeleteParticipant(w http.ResponseWriter, r *http.Request) {
	classID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}

	participantID, err := strconv.ParseInt(r.PathValue("participant_id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid participant ID")
		return
	}

	// Verify participant belongs to class
	p, err := h.DB.GetParticipantByID(participantID)
	if err != nil || p.ClassID != classID {
		errResponse(w, http.StatusNotFound, "participant not found in this class")
		return
	}

	if err := h.DB.DeleteParticipant(participantID); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to delete participant")
		return
	}
	success(w, map[string]string{"message": "participant deleted"})
}

// AdjustParticipantStars manually adjusts a participant's stars and dynamically computes level
func (h *Handler) AdjustParticipantStars(w http.ResponseWriter, r *http.Request) {
	classID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}

	participantID, err := strconv.ParseInt(r.PathValue("participant_id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid participant ID")
		return
	}

	var req struct {
		Stars int `json:"stars"`
	}
	if err := decodeJSON(r, &req); err != nil || req.Stars == 0 {
		errResponse(w, http.StatusBadRequest, "stars amount (+1 or -1) is required")
		return
	}

	teacherID := middleware.GetTeacherID(r)

	// Fetch participant
	p, err := h.DB.GetParticipantByID(participantID)
	if err != nil {
		errResponse(w, http.StatusNotFound, "participant not found")
		return
	}
	if p.ClassID != classID {
		errResponse(w, http.StatusBadRequest, "participant does not belong to this class")
		return
	}

	// Calculate new stars
	oldStars := p.TotalStars
	newStars := p.TotalStars + req.Stars
	if newStars < 0 {
		newStars = 0 // prevent negative stars
	}

	// Calculate new level based on star levels
	starLevels, err := h.DB.GetStarLevels(teacherID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to fetch star levels")
		return
	}

	newLevel := 1
	for _, sl := range starLevels {
		if newStars >= sl.StarsRequired {
			newLevel = sl.Level
		}
	}

	// Update DB
	err = h.DB.SetParticipantStarsAndLevel(participantID, newStars, newLevel)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to update participant")
		return
	}

	p.TotalStars = newStars
	p.Level = newLevel

	if class, classErr := h.DB.GetClassByID(classID); classErr == nil && class != nil {
		h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
			"type": "participant_updated",
			"payload": map[string]interface{}{
				"participant": p,
				"star_delta":  newStars - oldStars,
			},
		})
	}

	success(w, p)
}

// ResetStars resets all stars for a class
func (h *Handler) ResetStars(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	if err := h.DB.ResetStars(id); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to reset stars")
		return
	}
	success(w, map[string]string{"message": "stars reset"})
}

// GetLeaderboard returns the leaderboard for a class
func (h *Handler) GetLeaderboard(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	var sessionID int64
	if raw := r.URL.Query().Get("session_id"); raw != "" {
		sessionID, err = strconv.ParseInt(raw, 10, 64)
		if err != nil || sessionID < 0 {
			errResponse(w, http.StatusBadRequest, "invalid session ID")
			return
		}
	}
	participants, err := h.DB.GetLeaderboard(id, sessionID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get leaderboard")
		return
	}
	if participants == nil {
		participants = []models.Participant{}
	}
	success(w, participants)
}

// GetClassReports returns reports for a specific class
func (h *Handler) GetClassReports(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}
	reports, err := h.DB.GetReportsByClass(id)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get reports")
		return
	}
	if reports == nil {
		reports = []models.ReportSummary{}
	}
	success(w, reports)
}

// GetClassByCode returns class info by code (for students)
func (h *Handler) GetClassByCode(w http.ResponseWriter, r *http.Request) {
	code := r.PathValue("code")
	class, err := h.DB.GetClassByCode(code)
	if err == sql.ErrNoRows {
		errResponse(w, http.StatusNotFound, "class not found")
		return
	}
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get class")
		return
	}
	success(w, class)
}
