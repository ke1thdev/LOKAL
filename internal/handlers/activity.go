package handlers

import (
	"encoding/base64"
	"errors"
	"fmt"
	"math/rand"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"lokal-thesis/internal/auth"
	"lokal-thesis/internal/database"
	"lokal-thesis/internal/middleware"
	"lokal-thesis/internal/models"
)

// AutoStartSession creates a temporary class with a random 5-digit code and starts a session.
// No authentication required — designed for the LOKAL PowerPoint add-in auto-start flow.
func (h *Handler) AutoStartSession(w http.ResponseWriter, r *http.Request) {
	// Generate a unique 5-digit code
	var code string
	for i := 0; i < 100; i++ {
		code = fmt.Sprintf("%05d", rand.Intn(100000))
		_, err := h.DB.GetClassByCode(code)
		if err != nil {
			break // Code is unique
		}
	}

	teacherID := int64(0)
	tokenUsername := ""

	// Prefer the signed-in PowerPoint presenter when the add-in supplies its
	// saved bearer token. This makes the temporary class expose the real
	// presenter's display name to students.
	if tokenValue := bearerToken(r); tokenValue != "" {
		if presenter, lookupErr := h.authenticateTeacherToken(tokenValue); lookupErr == nil {
			teacherID = presenter.ID
			tokenUsername = presenter.Username
		}
	}

	// Preserve the offline/local fallback for an add-in that has not signed in.
	if teacherID == 0 {
		teacherID = 1
		presenter, lookupErr := h.DB.GetTeacherByID(teacherID)
		if lookupErr == nil {
			tokenUsername = presenter.Username
		} else {
			t, createErr := h.DB.CreateTeacher("lokal", "lokal@local", "$2a$10$dummy", "LOKAL Teacher")
			if createErr == nil {
				teacherID = t.ID
				tokenUsername = t.Username
			}
		}
	}
	if tokenUsername == "" {
		tokenUsername = "lokal"
	}

	// Create a class with this code
	class, err := h.DB.CreateClass(teacherID, "LOKAL Session", code, "#0B1F1C")
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to create auto session class")
		return
	}

	// Start a session on this class
	session, err := h.DB.StartSession(class.ID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to start session")
		return
	}

	// Broadcast session start
	h.Hub.BroadcastToRoom("class:"+code, map[string]interface{}{
		"type":    "session_start",
		"payload": session,
	})

	// Issue a JWT so the add-in can call the auth-protected
	// activity/session endpoints during the slideshow
	token, err := auth.GenerateToken(teacherID, tokenUsername)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to generate token")
		return
	}

	joinURL := "http://localhost:8080/student/"
	if h.Server != nil {
		joinURL = h.Server.AdvertisedBaseURL() + "/student/"
	}

	created(w, map[string]interface{}{
		"class_code": code,
		"class_id":   class.ID,
		"session_id": session.ID,
		"token":      token,
		"join_url":   joinURL,
	})
}

// GetActivities returns all activities for the authenticated teacher
func (h *Handler) GetActivities(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)
	activityType := r.URL.Query().Get("type")

	activities, err := h.DB.GetActivitiesByTeacher(teacherID, activityType)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to get activities")
		return
	}
	if activities == nil {
		activities = []models.Activity{}
	}
	success(w, activities)
}

// GetActivity returns a single activity
func (h *Handler) GetActivity(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}
	activity, err := h.DB.GetActivityByID(id)
	if err != nil {
		errResponse(w, http.StatusNotFound, "activity not found")
		return
	}
	success(w, activity)
}

// DeleteActivity deletes an activity and its slide image
func (h *Handler) DeleteActivity(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}

	// Also delete slide image if exists
	imgFile := filepath.Join(h.UploadsDir, "slides", fmt.Sprintf("activity_%d.png", id))
	os.Remove(imgFile)

	err = h.DB.DeleteActivity(id)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to delete activity")
		return
	}

	success(w, map[string]bool{"success": true})
}

// DeleteSessionResponses resets every collected response in a presentation
// session without removing its activity records.
func (h *Handler) DeleteSessionResponses(w http.ResponseWriter, r *http.Request) {
	sessionID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil || sessionID <= 0 {
		errResponse(w, http.StatusBadRequest, "invalid session ID")
		return
	}

	if err := h.DB.DeleteResponsesBySession(sessionID); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to delete session responses")
		return
	}

	success(w, map[string]bool{"success": true})
}

// ToggleActivityFavorite toggles the favorite status of an activity
func (h *Handler) ToggleActivityFavorite(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}

	isFav, err := h.DB.ToggleActivityFavorite(id)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to toggle favorite")
		return
	}

	success(w, map[string]bool{"is_favorite": isFav})
}

// GetResponses returns all responses for an activity
func (h *Handler) GetResponses(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}
	responses, err := h.DB.GetResponsesByActivity(id)
	if err != nil {
		fmt.Printf("GetResponsesByActivity err: %v\n", err)
		errResponse(w, http.StatusInternalServerError, "failed to get responses")
		return
	}
	if responses == nil {
		responses = []models.Response{}
	}
	success(w, responses)
}

// StartSession starts a new live session
func (h *Handler) StartSession(w http.ResponseWriter, r *http.Request) {
	var req struct {
		ClassID int64 `json:"class_id"`
	}
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}
	session, err := h.DB.StartSession(req.ClassID)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to start session")
		return
	}

	// Notify students in the room
	class, _ := h.DB.GetClassByID(req.ClassID)
	if class != nil {
		h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
			"type":    "session_start",
			"payload": session,
		})
	}

	created(w, session)
}

// StopSession stops a live session
func (h *Handler) StopSession(w http.ResponseWriter, r *http.Request) {
	var req struct {
		SessionID int64 `json:"session_id"`
		ClassID   int64 `json:"class_id"`
	}
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}
	if err := h.DB.StopSession(req.SessionID); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to stop session")
		return
	}

	// Close any activities still collecting for this class
	h.DB.CloseOpenActivities(req.ClassID)

	class, _ := h.DB.GetClassByID(req.ClassID)
	if class != nil {
		h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
			"type": "session_stop",
		})
	}

	success(w, map[string]string{"message": "session stopped"})
}

// StartActivity starts a new activity
func (h *Handler) StartActivity(w http.ResponseWriter, r *http.Request) {
	var req models.StartActivityRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	// A class can collect responses for only one activity at a time. This
	// prevents a quick slide change or double-click from leaving stale open
	// activities that would still accept student submissions.
	if err := h.DB.CloseOpenActivities(req.ClassID); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to prepare activity")
		return
	}

	activity, err := h.DB.CreateActivity(req)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to start activity")
		return
	}

	// Broadcast to students
	class, _ := h.DB.GetClassByID(req.ClassID)
	if class != nil {
		h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
			"type":    "activity_start",
			"payload": activity,
		})
	}

	created(w, activity)
}

// CloseActivity closes submissions for an activity
func (h *Handler) CloseActivity(w http.ResponseWriter, r *http.Request) {
	var req struct {
		ActivityID int64 `json:"activity_id"`
		ClassID    int64 `json:"class_id"`
	}
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	if err := h.DB.CloseActivity(req.ActivityID); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to close activity")
		return
	}

	// Get responses for results
	responses, _ := h.DB.GetResponsesByActivity(req.ActivityID)

	class, _ := h.DB.GetClassByID(req.ClassID)
	if class != nil {
		h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
			"type": "activity_close",
			"payload": map[string]interface{}{
				"activity_id": req.ActivityID,
				"responses":   responses,
			},
		})
	}

	success(w, map[string]interface{}{
		"message":   "activity closed",
		"responses": responses,
	})
}

// UploadActivitySlide stores a PNG snapshot of the slide an activity runs on,
// so the student page can show the real slide instead of extracted text.
func (h *Handler) UploadActivitySlide(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}

	var req struct {
		ImageBase64 string `json:"image_base64"`
	}
	if err := decodeJSON(r, &req); err != nil || req.ImageBase64 == "" {
		errResponse(w, http.StatusBadRequest, "image_base64 is required")
		return
	}

	data, err := base64.StdEncoding.DecodeString(req.ImageBase64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid base64 image")
		return
	}

	dir := filepath.Join(h.UploadsDir, "slides")
	if err := os.MkdirAll(dir, 0755); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to store slide")
		return
	}
	path := filepath.Join(dir, fmt.Sprintf("activity_%d.png", id))
	if err := os.WriteFile(path, data, 0644); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to store slide")
		return
	}

	// Tell students the slide image is ready
	url := fmt.Sprintf("/uploads/slides/activity_%d.png", id)
	if activity, aerr := h.DB.GetActivityByID(id); aerr == nil {
		if class, cerr := h.DB.GetClassByID(activity.ClassID); cerr == nil {
			h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
				"type": "slide_ready",
				"payload": map[string]interface{}{
					"activity_id": id,
					"slide_url":   url,
				},
			})
		}
	}

	success(w, map[string]string{"slide_url": url})
}

// AwardStarsToAll awards stars to all participants who responded
func (h *Handler) AwardStarsToAll(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}

	var req struct {
		Stars int `json:"stars"`
	}
	if err := decodeJSON(r, &req); err != nil || req.Stars <= 0 {
		req.Stars = 1
	}

	if err := h.DB.AwardStarsToAll(id, req.Stars); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to award stars")
		return
	}

	activity, _ := h.DB.GetActivityByID(id)
	if activity != nil {
		class, _ := h.DB.GetClassByID(activity.ClassID)
		if class != nil {
			responses, _ := h.DB.GetResponsesByActivity(id)
			participantIDs := make([]int64, 0, len(responses))
			for _, response := range responses {
				participantIDs = append(participantIDs, response.ParticipantID)
			}
			participants := h.refreshAwardedParticipants(middleware.GetTeacherID(r), participantIDs)

			h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
				"type": "stars_awarded",
				"payload": map[string]interface{}{
					"activity_id":  id,
					"stars":        req.Stars,
					"correct_only": false,
					"participants": participants,
				},
			})
		}
	}

	success(w, map[string]string{"message": "stars awarded"})
}

// AwardStarsToCorrect awards stars to participants who responded correctly
func (h *Handler) AwardStarsToCorrect(w http.ResponseWriter, r *http.Request) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid activity ID")
		return
	}

	var req struct {
		Stars int `json:"stars"`
	}
	if err := decodeJSON(r, &req); err != nil || req.Stars <= 0 {
		req.Stars = 1
	}

	if err := h.DB.AwardStarsToCorrect(id, req.Stars); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to award stars")
		return
	}

	activity, _ := h.DB.GetActivityByID(id)
	if activity != nil {
		class, _ := h.DB.GetClassByID(activity.ClassID)
		if class != nil {
			responses, _ := h.DB.GetResponsesByActivity(id)
			var correctIDs []int64
			for _, response := range responses {
				if response.IsCorrect != nil && *response.IsCorrect {
					correctIDs = append(correctIDs, response.ParticipantID)
				}
			}
			participants := h.refreshAwardedParticipants(middleware.GetTeacherID(r), correctIDs)

			h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
				"type": "stars_awarded",
				"payload": map[string]interface{}{
					"activity_id":             id,
					"stars":                   req.Stars,
					"correct_only":            true,
					"correct_participant_ids": correctIDs,
					"participants":            participants,
				},
			})
		}
	}

	success(w, map[string]string{"message": "stars awarded to correct answers"})
}

// refreshAwardedParticipants applies the teacher's configured star thresholds
// after a reward and returns authoritative participant snapshots for live
// student and presenter UIs.
func (h *Handler) refreshAwardedParticipants(teacherID int64, participantIDs []int64) []models.Participant {
	levels, err := h.DB.GetStarLevels(teacherID)
	if err != nil {
		levels = nil
	}

	seen := make(map[int64]bool)
	updated := make([]models.Participant, 0, len(participantIDs))
	for _, participantID := range participantIDs {
		if participantID <= 0 || seen[participantID] {
			continue
		}
		seen[participantID] = true

		participant, getErr := h.DB.GetParticipantByID(participantID)
		if getErr != nil || participant == nil {
			continue
		}

		newLevel := 1
		for _, level := range levels {
			if participant.TotalStars >= level.StarsRequired && level.Level > newLevel {
				newLevel = level.Level
			}
		}
		if participant.Level != newLevel {
			if setErr := h.DB.SetParticipantStarsAndLevel(
				participant.ID, participant.TotalStars, newLevel); setErr == nil {
				participant.Level = newLevel
			}
		}
		updated = append(updated, *participant)
	}
	return updated
}

// StudentJoin handles a student joining a class
func (h *Handler) StudentJoin(w http.ResponseWriter, r *http.Request) {
	var req models.StudentJoinRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	if req.ClassCode == "" || req.Name == "" {
		errResponse(w, http.StatusBadRequest, "class code and name are required")
		return
	}

	class, err := h.DB.GetClassByCode(req.ClassCode)
	if err != nil {
		errResponse(w, http.StatusNotFound, "class not found")
		return
	}

	if class.IsLocked {
		errResponse(w, http.StatusForbidden, "class is locked")
		return
	}

	deviceRegistration := normalizeDeviceRegistration(r, models.DeviceRegistration{
		DeviceUID: req.DeviceID,
		Name:      "Student browser",
		Platform:  "web",
	})
	req.DeviceID = deviceRegistration.DeviceUID

	avatarURL := ""
	if req.Avatar != "" {
		b64data := req.Avatar
		if idx := strings.Index(b64data, ","); idx != -1 {
			b64data = b64data[idx+1:]
		}

		imgData, err := base64.StdEncoding.DecodeString(b64data)
		if err == nil {
			filename := fmt.Sprintf("avatar_%s_%d.jpg", req.DeviceID, time.Now().UnixNano())
			dir := filepath.Join(h.UploadsDir, "avatars")
			os.MkdirAll(dir, 0755)

			filePath := filepath.Join(dir, filename)
			if err := os.WriteFile(filePath, imgData, 0644); err == nil {
				avatarURL = "/uploads/avatars/" + filename
			}
		}
	}

	device, err := h.DB.RegisterDevice(deviceRegistration)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to register device")
		return
	}

	participant, err := h.DB.RegisterJoiningParticipant(class.ID, strings.TrimSpace(req.Name), req.DeviceID, avatarURL)
	if err != nil {
		if errors.Is(err, database.ErrParticipantNameInUse) {
			errResponse(w, http.StatusConflict, "that participant name is already in use")
			return
		}
		errResponse(w, http.StatusInternalServerError, "failed to join class")
		return
	}
	rawToken, tokenHash := auth.GenerateOpaqueToken("lks_")
	expiresAt := time.Now().UTC().Add(auth.StudentSessionTTL)
	if err := h.DB.CreateStudentAuthSession(participant.ID, device.ID, tokenHash, expiresAt); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to create participant session")
		return
	}

	// Get active session + any activity already in progress (late join)
	session, _ := h.DB.GetActiveSession(class.ID)
	activity, _ := h.DB.GetActiveActivity(class.ID)

	// Notify teacher
	h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
		"type": "student_join",
		"payload": map[string]interface{}{
			"participant": participant,
			"class":       class,
		},
	})

	success(w, map[string]interface{}{
		"participant": participant,
		"class":       class,
		"session":     session,
		"activity":    activity,
		"auth_token":  rawToken,
		"expires_at":  expiresAt,
		"device":      device,
	})
}

// GetClassState returns the live session + open activity for a class code.
// Used by the student page to resync after a reload.
func (h *Handler) GetClassState(w http.ResponseWriter, r *http.Request) {
	participant, ok := h.authenticateStudent(w, r)
	if !ok {
		return
	}
	class, err := h.DB.GetClassByCode(r.PathValue("code"))
	if err != nil {
		errResponse(w, http.StatusNotFound, "class not found")
		return
	}

	session, _ := h.DB.GetActiveSession(class.ID)
	activity, _ := h.DB.GetActiveActivity(class.ID)
	if participant.ClassID != class.ID {
		errResponse(w, http.StatusForbidden, "participant session does not belong to this class")
		return
	}

	success(w, map[string]interface{}{
		"class":       class,
		"session":     session,
		"activity":    activity,
		"participant": participant,
	})
}

// StudentSubmit handles a student submitting a response
func (h *Handler) StudentSubmit(w http.ResponseWriter, r *http.Request) {
	participant, ok := h.authenticateStudent(w, r)
	if !ok {
		return
	}
	var req models.StudentSubmitRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	activity, err := h.DB.GetActivityByID(req.ActivityID)
	if err != nil || activity == nil {
		errResponse(w, http.StatusNotFound, "activity not found")
		return
	}
	if activity.ClassID != participant.ClassID {
		errResponse(w, http.StatusForbidden, "activity does not belong to participant class")
		return
	}

	response, err := h.DB.SubmitResponse(req.ActivityID, participant.ID, req.Answer, req.ResponseTimeMs)
	if err != nil {
		errResponse(w, http.StatusConflict, err.Error())
		return
	}

	// Notify teacher about new response
	if activity != nil {
		class, _ := h.DB.GetClassByID(activity.ClassID)
		if class != nil {
			h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
				"type":    "response",
				"payload": response,
			})
		}
	}

	success(w, response)
}

func (h *Handler) authenticateStudent(w http.ResponseWriter, r *http.Request) (*models.Participant, bool) {
	token := bearerToken(r)
	if !strings.HasPrefix(token, "lks_") {
		errResponse(w, http.StatusUnauthorized, "participant authentication required")
		return nil, false
	}
	participant, err := h.DB.AuthenticateStudentSession(auth.HashToken(token))
	if err != nil {
		errResponse(w, http.StatusUnauthorized, "participant session is invalid or expired")
		return nil, false
	}
	return participant, true
}
