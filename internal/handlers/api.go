package handlers

import (
	"encoding/json"
	"net/http"
	"strconv"
	"strings"

	"github.com/gorilla/websocket"
	"github.com/skip2/go-qrcode"
	"lokal-thesis/internal/auth"
	"lokal-thesis/internal/database"
	"lokal-thesis/internal/hub"
	"lokal-thesis/internal/middleware"
	"lokal-thesis/internal/serverconfig"
	"lokal-thesis/internal/syncoutbox"
	"lokal-thesis/internal/wsrelay"
)

// Handler holds shared dependencies
type Handler struct {
	DB         *database.DB
	Hub        *hub.Hub
	UploadsDir string
	Server     *serverconfig.Manager
	Sync       *syncoutbox.Manager
	Relay      *wsrelay.Manager
}

// SetServerConfig attaches the runtime server configuration manager.
func (h *Handler) SetServerConfig(manager *serverconfig.Manager) {
	h.Server = manager
}

func (h *Handler) SetSync(manager *syncoutbox.Manager) {
	h.Sync = manager
}

func (h *Handler) SetRelay(manager *wsrelay.Manager) {
	h.Relay = manager
}

// New creates a new Handler
func New(db *database.DB, h *hub.Hub, uploadsDir ...string) *Handler {
	path := "uploads"
	if len(uploadsDir) > 0 && uploadsDir[0] != "" {
		path = uploadsDir[0]
	}
	handler := &Handler{DB: db, Hub: h, UploadsDir: path}
	middleware.SetSessionValidator(func(rawToken string) (*middleware.SessionIdentity, error) {
		teacher, err := db.AuthenticateTeacherSession(auth.HashToken(rawToken))
		if err != nil {
			return nil, err
		}
		return &middleware.SessionIdentity{TeacherID: teacher.ID, Username: teacher.Username}, nil
	})
	return handler
}

// RegisterRoutes sets up all API routes
func (h *Handler) RegisterRoutes(mux *http.ServeMux) {
	// Auth (no middleware)
	mux.HandleFunc("POST /api/v1/auth/register", h.Register)
	mux.HandleFunc("POST /api/v1/auth/login", h.Login)
	mux.HandleFunc("POST /api/v1/auth/logout", middleware.Auth(h.Logout))
	mux.HandleFunc("GET /api/v1/auth/devices", middleware.Auth(h.GetDevices))
	mux.HandleFunc("DELETE /api/v1/auth/devices/{id}", middleware.Auth(h.RevokeDevice))

	// Classes (auth required)
	mux.HandleFunc("GET /api/v1/classes", middleware.Auth(h.GetClasses))
	mux.HandleFunc("POST /api/v1/classes", middleware.Auth(h.CreateClass))
	mux.HandleFunc("GET /api/v1/classes/{id}", middleware.Auth(h.GetClass))
	mux.HandleFunc("PUT /api/v1/classes/{id}", middleware.Auth(h.UpdateClass))
	mux.HandleFunc("POST /api/v1/classes/{id}/lock", middleware.Auth(h.SetClassLock))
	mux.HandleFunc("DELETE /api/v1/classes/{id}", middleware.Auth(h.DeleteClass))
	mux.HandleFunc("GET /api/v1/classes/{id}/participants", middleware.Auth(h.GetParticipants))
	mux.HandleFunc("GET /api/v1/classes/{id}/online-participants", middleware.Auth(h.GetOnlineParticipants))
	mux.HandleFunc("POST /api/v1/classes/{id}/participants", middleware.Auth(h.AddParticipant))
	mux.HandleFunc("PUT /api/v1/classes/{id}/participants/{participant_id}", middleware.Auth(h.EditParticipant))
	mux.HandleFunc("DELETE /api/v1/classes/{id}/participants/{participant_id}", middleware.Auth(h.DeleteParticipant))
	mux.HandleFunc("POST /api/v1/classes/{id}/participants/{participant_id}/stars", middleware.Auth(h.AdjustParticipantStars))
	mux.HandleFunc("PUT /api/v1/classes/{id}/participants/{participant_id}/group", middleware.Auth(h.SetParticipantGroup))
	mux.HandleFunc("GET /api/v1/classes/{id}/groups", middleware.Auth(h.GetGroups))
	mux.HandleFunc("POST /api/v1/classes/{id}/groups", middleware.Auth(h.CreateGroup))
	mux.HandleFunc("PUT /api/v1/classes/{id}/groups/{group_id}", middleware.Auth(h.UpdateGroup))
	mux.HandleFunc("DELETE /api/v1/classes/{id}/groups/{group_id}", middleware.Auth(h.DeleteGroup))
	mux.HandleFunc("POST /api/v1/classes/{id}/reset-stars", middleware.Auth(h.ResetStars))
	mux.HandleFunc("POST /api/v1/classes/{id}/slide", middleware.Auth(h.UploadClassSlide))
	mux.HandleFunc("GET /api/v1/classes/{id}/leaderboard", middleware.Auth(h.GetLeaderboard))
	mux.HandleFunc("GET /api/v1/classes/{id}/reports", middleware.Auth(h.GetClassReports))

	// Reports (auth required)
	mux.HandleFunc("GET /api/v1/reports", middleware.Auth(h.GetReports))
	mux.HandleFunc("GET /api/v1/reports/{id}", middleware.Auth(h.GetReportDetails))
	mux.HandleFunc("GET /api/v1/sessions/{id}/quiz-summary", middleware.Auth(h.GetQuizSummary))
	mux.HandleFunc("POST /api/v1/reports/{id}/favorite", middleware.Auth(h.ToggleFavoriteReport))
	mux.HandleFunc("DELETE /api/v1/reports/{id}", middleware.Auth(h.DeleteReport))

	// Activities (auth required)
	mux.HandleFunc("GET /api/v1/activities", middleware.Auth(h.GetActivities))
	mux.HandleFunc("GET /api/v1/activities/{id}", middleware.Auth(h.GetActivity))
	mux.HandleFunc("DELETE /api/v1/activities/{id}", middleware.Auth(h.DeleteActivity))
	mux.HandleFunc("DELETE /api/v1/sessions/{id}/responses", middleware.Auth(h.DeleteSessionResponses))
	mux.HandleFunc("POST /api/v1/activities/{id}/favorite", middleware.Auth(h.ToggleActivityFavorite))
	mux.HandleFunc("GET /api/v1/activities/{id}/responses", middleware.Auth(h.GetResponses))
	mux.HandleFunc("POST /api/v1/activities/{id}/award-stars", middleware.Auth(h.AwardStarsToAll))
	mux.HandleFunc("POST /api/v1/activities/{id}/award-stars-correct", middleware.Auth(h.AwardStarsToCorrect))
	mux.HandleFunc("POST /api/v1/activities/{id}/slide", h.UploadActivitySlide)

	// Settings (auth required)
	mux.HandleFunc("GET /api/v1/settings/star-levels", middleware.Auth(h.GetStarLevels))
	mux.HandleFunc("PUT /api/v1/settings/star-levels", middleware.Auth(h.UpdateStarLevels))

	// Server operating mode. Status is public so local clients and the
	// PowerPoint add-in can discover the active endpoint before signing in.
	mux.HandleFunc("GET /api/v1/server/status", h.GetServerStatus)
	mux.HandleFunc("GET /api/v1/server/config", middleware.Auth(h.GetServerConfig))
	mux.HandleFunc("PUT /api/v1/server/config", middleware.Auth(h.UpdateServerConfig))
	mux.HandleFunc("GET /api/v1/sync/status", middleware.Auth(h.GetSyncStatus))
	mux.HandleFunc("POST /api/v1/sync/run", middleware.Auth(h.RunSyncNow))
	mux.HandleFunc("POST /api/v1/sync/outbox", h.ReceiveOutbox)
	mux.HandleFunc("GET /api/v1/relay/status", middleware.Auth(h.GetRelayStatus))
	mux.HandleFunc("GET /api/v1/relay/edge", h.HandleRelayEdge)

	// Session management (auth required)
	mux.HandleFunc("POST /api/v1/session/start", middleware.Auth(h.StartSession))
	mux.HandleFunc("POST /api/v1/session/stop", middleware.Auth(h.StopSession))

	// Utilities
	mux.HandleFunc("GET /api/v1/qrcode", h.GenerateQRCode)

	mux.HandleFunc("POST /api/v1/activity/start", middleware.Auth(h.StartActivity))
	mux.HandleFunc("POST /api/v1/activity/close", middleware.Auth(h.CloseActivity))

	// Student join/discovery are public. State, submission, and WebSocket
	// identity are protected by the opaque token returned from join.
	mux.HandleFunc("POST /api/v1/student/join", h.StudentJoin)
	mux.HandleFunc("POST /api/v1/student/submit", h.StudentSubmit)
	mux.HandleFunc("GET /api/v1/student/class/{code}", h.GetClassByCode)
	mux.HandleFunc("GET /api/v1/student/class/{code}/state", h.GetClassState)

	// Teacher profile
	mux.HandleFunc("GET /api/v1/profile", middleware.Auth(h.GetProfile))
	mux.HandleFunc("PUT /api/v1/profile", middleware.Auth(h.UpdateProfile))

	// WebSocket
	mux.HandleFunc("GET /ws", h.HandleWebSocket)

	// Auto-session (no auth — used by LOKAL PowerPoint add-in for auto-start)
	mux.HandleFunc("POST /api/v1/session/auto-start", h.AutoStartSession)
}

// JSON helpers

func writeJSON(w http.ResponseWriter, status int, data interface{}) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(data)
}

func success(w http.ResponseWriter, data interface{}) {
	writeJSON(w, http.StatusOK, map[string]interface{}{
		"success": true,
		"data":    data,
	})
}

func created(w http.ResponseWriter, data interface{}) {
	writeJSON(w, http.StatusCreated, map[string]interface{}{
		"success": true,
		"data":    data,
	})
}

func errResponse(w http.ResponseWriter, status int, msg string) {
	writeJSON(w, status, map[string]interface{}{
		"success": false,
		"error":   msg,
	})
}

func decodeJSON(r *http.Request, v interface{}) error {
	return json.NewDecoder(r.Body).Decode(v)
}

// WebSocket upgrader
var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true // Allow all origins for local/hybrid mode
	},
}

// HandleWebSocket upgrades HTTP to WebSocket
func (h *Handler) HandleWebSocket(w http.ResponseWriter, r *http.Request) {
	room := r.URL.Query().Get("room")
	isTeacher := r.URL.Query().Get("role") == "teacher"
	clientID := r.URL.Query().Get("id")

	if room == "" || clientID == "" {
		errResponse(w, http.StatusBadRequest, "room and client id are required")
		return
	}
	rawToken := strings.TrimSpace(r.URL.Query().Get("token"))
	if isTeacher {
		teacher, authErr := h.authenticateTeacherToken(rawToken)
		classCode := strings.TrimPrefix(room, "class:")
		class, classErr := h.DB.GetClassByCode(classCode)
		if authErr != nil || classErr != nil || class.TeacherID != teacher.ID || room != "class:"+class.Code {
			errResponse(w, http.StatusUnauthorized, "invalid presenter WebSocket session")
			return
		}
	} else {
		participant, authErr := h.DB.AuthenticateStudentSession(auth.HashToken(rawToken))
		if authErr != nil || strconv.FormatInt(participant.ID, 10) != clientID {
			errResponse(w, http.StatusUnauthorized, "invalid participant WebSocket session")
			return
		}
		class, classErr := h.DB.GetClassByID(participant.ClassID)
		if classErr != nil || room != "class:"+class.Code {
			errResponse(w, http.StatusForbidden, "participant cannot join this room")
			return
		}
	}

	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		return
	}

	client := &hub.Client{
		Hub:       h.Hub,
		Conn:      conn,
		Send:      make(chan []byte, 256),
		Room:      room,
		IsTeacher: isTeacher,
		ID:        clientID,
	}

	h.Hub.Register(client)
	go client.WritePump()
	go client.ReadPump()
}

// GenerateQRCode generates a QR code image from the data query param
func (h *Handler) GenerateQRCode(w http.ResponseWriter, r *http.Request) {
	data := r.URL.Query().Get("data")
	if data == "" {
		http.Error(w, "missing data parameter", http.StatusBadRequest)
		return
	}

	png, err := qrcode.Encode(data, qrcode.Medium, 256)
	if err != nil {
		http.Error(w, "failed to generate QR code", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "image/png")
	w.Write(png)
}
