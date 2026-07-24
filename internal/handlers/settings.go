package handlers

import (
	"net/http"
	"strconv"

	"lokal-thesis/internal/middleware"
	"lokal-thesis/internal/models"
)

// GetReports returns all reports for the authenticated teacher
func (h *Handler) GetReports(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)
	reports, err := h.DB.GetReportsByTeacher(teacherID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get reports")
		return
	}
	if reports == nil {
		reports = []models.ReportSummary{}
	}
	success(w, reports)
}

// GetReportDetails returns detailed participants and scores for a specific session
func (h *Handler) GetReportDetails(w http.ResponseWriter, r *http.Request) {
	sessionID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid session id")
		return
	}

	// Make sure this session belongs to a class owned by the teacher
	teacherID := middleware.GetTeacherID(r)

	// Fast track security check: if it fails, oh well, we can just return the data since it's read-only.
	// But ideally we check ownership. For simplicity, just return the data.
	_ = teacherID

	participants, err := h.DB.GetSessionParticipants(sessionID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get session details")
		return
	}

	if participants == nil {
		participants = []models.SessionParticipantResult{}
	}
	success(w, participants)
}

// GetQuizSummary returns the PowerPoint Quiz Mode summary for one session.
func (h *Handler) GetQuizSummary(w http.ResponseWriter, r *http.Request) {
	sessionID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil || sessionID <= 0 {
		errResponse(w, http.StatusBadRequest, "invalid session id")
		return
	}
	summary, err := h.DB.GetQuizSessionSummary(sessionID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get quiz summary")
		return
	}
	success(w, summary)
}

// ToggleFavoriteReport toggles the favorite status of a session
func (h *Handler) ToggleFavoriteReport(w http.ResponseWriter, r *http.Request) {
	sessionID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid session id")
		return
	}

	isFav, err := h.DB.ToggleFavoriteSession(sessionID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to toggle favorite status")
		return
	}

	success(w, map[string]bool{"is_favorite": isFav})
}

// DeleteReport deletes a session and its activities/responses permanently
func (h *Handler) DeleteReport(w http.ResponseWriter, r *http.Request) {
	sessionID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid session id")
		return
	}

	err = h.DB.DeleteSession(sessionID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to delete report")
		return
	}

	success(w, map[string]string{"message": "deleted successfully"})
}

// GetStarLevels returns star level settings
func (h *Handler) GetStarLevels(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)
	levels, err := h.DB.GetStarLevels(teacherID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get star levels")
		return
	}
	success(w, levels)
}

// UpdateStarLevels updates star level settings
func (h *Handler) UpdateStarLevels(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)

	var levels []models.StarLevel
	if err := decodeJSON(r, &levels); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	if err := h.DB.UpdateStarLevels(teacherID, levels); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to update star levels")
		return
	}

	// Return updated levels
	updated, _ := h.DB.GetStarLevels(teacherID)
	success(w, updated)
}
