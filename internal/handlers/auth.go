package handlers

import (
	"net/http"
	"runtime"
	"strconv"
	"strings"
	"time"

	"lokal-thesis/internal/auth"
	"lokal-thesis/internal/middleware"
	"lokal-thesis/internal/models"
)

// Register creates a new teacher account
func (h *Handler) Register(w http.ResponseWriter, r *http.Request) {
	var req models.RegisterRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	req.Username = strings.TrimSpace(req.Username)
	req.Email = strings.TrimSpace(req.Email)
	req.DisplayName = strings.TrimSpace(req.DisplayName)
	if validationError := validateRegistrationCredentials(req.Username, req.Password); validationError != "" {
		errResponse(w, http.StatusBadRequest, validationError)
		return
	}
	if len(req.Email) > 254 || len(req.DisplayName) > 120 {
		errResponse(w, http.StatusBadRequest, "email or display name is too long")
		return
	}

	if req.DisplayName == "" {
		req.DisplayName = req.Username
	}

	hash, err := auth.HashPassword(req.Password)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to hash password")
		return
	}

	teacher, err := h.DB.CreateTeacher(req.Username, req.Email, hash, req.DisplayName)
	if err != nil {
		errResponse(w, http.StatusConflict, "username or email already exists")
		return
	}

	response, err := h.issueTeacherSession(r, teacher, req.Device)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to create authenticated session")
		return
	}

	created(w, response)
}

// UpdateProfile updates the editable teacher profile used by both the web
// dashboard and the presenter identity shown to students.
func (h *Handler) UpdateProfile(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)
	var req struct {
		DisplayName  string `json:"display_name"`
		Email        string `json:"email"`
		AvatarURL    string `json:"avatar_url"`
		Organization string `json:"organization"`
		Profession   string `json:"profession"`
	}
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}
	req.DisplayName = strings.TrimSpace(req.DisplayName)
	if req.DisplayName == "" {
		errResponse(w, http.StatusBadRequest, "display name is required")
		return
	}
	teacher, err := h.DB.UpdateTeacherProfile(
		teacherID,
		req.DisplayName,
		strings.TrimSpace(req.Email),
		strings.TrimSpace(req.AvatarURL),
		strings.TrimSpace(req.Organization),
		strings.TrimSpace(req.Profession),
	)
	if err != nil {
		errResponse(w, http.StatusConflict, "email address is already in use")
		return
	}
	success(w, teacher)
}

// Login authenticates a teacher
func (h *Handler) Login(w http.ResponseWriter, r *http.Request) {
	var req models.LoginRequest
	if err := decodeJSON(r, &req); err != nil {
		errResponse(w, http.StatusBadRequest, "invalid request body")
		return
	}

	req.Username = strings.TrimSpace(req.Username)
	if req.Username == "" || req.Password == "" || len(req.Username) > 64 || len(req.Password) > 128 {
		errResponse(w, http.StatusBadRequest, "invalid username or password")
		return
	}

	teacher, err := h.DB.GetTeacherByUsername(req.Username)
	if err != nil {
		errResponse(w, http.StatusUnauthorized, "invalid username or password")
		return
	}

	if !auth.CheckPassword(req.Password, teacher.PasswordHash) {
		errResponse(w, http.StatusUnauthorized, "invalid username or password")
		return
	}

	response, err := h.issueTeacherSession(r, teacher, req.Device)
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to create authenticated session")
		return
	}

	success(w, response)
}

func validateRegistrationCredentials(username, password string) string {
	if username == "" || password == "" {
		return "username and password are required"
	}
	if len(username) < 3 || len(username) > 64 {
		return "username must be between 3 and 64 characters"
	}
	if len(password) < 8 || len(password) > 128 {
		return "password must be between 8 and 128 characters"
	}
	return ""
}

func (h *Handler) issueTeacherSession(r *http.Request, teacher *models.Teacher, registration models.DeviceRegistration) (models.AuthResponse, error) {
	registration = normalizeDeviceRegistration(r, registration)
	device, err := h.DB.RegisterDevice(registration)
	if err != nil {
		return models.AuthResponse{}, err
	}
	raw, hash := auth.GenerateOpaqueToken("lkt_")
	expiresAt := time.Now().UTC().Add(auth.TeacherSessionTTL)
	if err := h.DB.CreateTeacherAuthSession(teacher.ID, device.ID, hash, expiresAt); err != nil {
		return models.AuthResponse{}, err
	}
	return models.AuthResponse{Token: raw, Teacher: teacher, Device: device, ExpiresAt: expiresAt}, nil
}

func normalizeDeviceRegistration(r *http.Request, registration models.DeviceRegistration) models.DeviceRegistration {
	registration.DeviceUID = strings.TrimSpace(registration.DeviceUID)
	if len(registration.DeviceUID) > 160 {
		registration.DeviceUID = registration.DeviceUID[:160]
	}
	if registration.DeviceUID == "" {
		raw, _ := auth.GenerateOpaqueToken("dev_")
		registration.DeviceUID = raw
	}
	registration.Name = strings.TrimSpace(registration.Name)
	if registration.Name == "" {
		registration.Name = "LOKAL " + runtime.GOOS
	}
	if len(registration.Name) > 120 {
		registration.Name = registration.Name[:120]
	}
	registration.Platform = strings.TrimSpace(registration.Platform)
	if registration.Platform == "" {
		registration.Platform = runtime.GOOS
	}
	registration.UserAgent = strings.TrimSpace(registration.UserAgent)
	if registration.UserAgent == "" {
		registration.UserAgent = r.UserAgent()
	}
	if len(registration.UserAgent) > 512 {
		registration.UserAgent = registration.UserAgent[:512]
	}
	return registration
}

func bearerToken(r *http.Request) string {
	header := r.Header.Get("Authorization")
	if !strings.HasPrefix(header, "Bearer ") {
		return ""
	}
	return strings.TrimSpace(strings.TrimPrefix(header, "Bearer "))
}

func (h *Handler) authenticateTeacherToken(rawToken string) (*models.Teacher, error) {
	if strings.HasPrefix(rawToken, "lkt_") {
		return h.DB.AuthenticateTeacherSession(auth.HashToken(rawToken))
	}
	claims, err := auth.ValidateToken(rawToken)
	if err != nil {
		return nil, err
	}
	return h.DB.GetTeacherByID(claims.TeacherID)
}

func (h *Handler) Logout(w http.ResponseWriter, r *http.Request) {
	token := bearerToken(r)
	if strings.HasPrefix(token, "lkt_") {
		if err := h.DB.RevokeTeacherSession(auth.HashToken(token)); err != nil {
			errResponse(w, http.StatusInternalServerError, "failed to end session")
			return
		}
	}
	success(w, map[string]bool{"logged_out": true})
}

func (h *Handler) GetDevices(w http.ResponseWriter, r *http.Request) {
	devices, err := h.DB.GetTeacherDevices(middleware.GetTeacherID(r))
	if err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to load registered devices")
		return
	}
	success(w, devices)
}

func (h *Handler) RevokeDevice(w http.ResponseWriter, r *http.Request) {
	deviceID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil || deviceID <= 0 {
		errResponse(w, http.StatusBadRequest, "invalid device id")
		return
	}
	if err := h.DB.RevokeTeacherDevice(middleware.GetTeacherID(r), deviceID); err != nil {
		errResponse(w, http.StatusNotFound, "registered device not found")
		return
	}
	success(w, map[string]bool{"revoked": true})
}

// GetProfile returns the authenticated teacher's profile
func (h *Handler) GetProfile(w http.ResponseWriter, r *http.Request) {
	teacherID := middleware.GetTeacherID(r)
	teacher, err := h.DB.GetTeacherByID(teacherID)
	if err != nil {
		errResponse(w, http.StatusNotFound, "teacher not found")
		return
	}
	success(w, teacher)
}
