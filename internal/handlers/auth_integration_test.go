package handlers

import (
	"bytes"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"

	"lokal-thesis/internal/database"
	"lokal-thesis/internal/hub"
	"lokal-thesis/internal/models"
)

func TestTeacherDeviceRegistrationAndRevocationAPI(t *testing.T) {
	db, err := database.New(filepath.Join(t.TempDir(), "auth-handler-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	handler := New(db, hub.NewHub())
	mux := http.NewServeMux()
	handler.RegisterRoutes(mux)

	registerBody := []byte(`{
		"username":"device-teacher",
		"email":"device-teacher@example.test",
		"password":"correct-horse-battery-staple",
		"display_name":"Device Teacher",
		"device":{
			"id":"web-test-device",
			"name":"Test browser",
			"platform":"Windows",
			"user_agent":"LOKAL integration test"
		}
	}`)
	registerRequest := httptest.NewRequest(http.MethodPost, "/api/v1/auth/register", bytes.NewReader(registerBody))
	registerRequest.Header.Set("Content-Type", "application/json")
	registerResponse := httptest.NewRecorder()
	mux.ServeHTTP(registerResponse, registerRequest)
	if registerResponse.Code != http.StatusCreated {
		t.Fatalf("register status = %d, body = %s", registerResponse.Code, registerResponse.Body.String())
	}

	var registration struct {
		Data models.AuthResponse `json:"data"`
	}
	if err := json.Unmarshal(registerResponse.Body.Bytes(), &registration); err != nil {
		t.Fatalf("decode registration: %v", err)
	}
	if registration.Data.Token == "" || registration.Data.Device == nil {
		t.Fatalf("registration did not return token and device: %+v", registration.Data)
	}

	devicesRequest := authenticatedRequest(http.MethodGet, "/api/v1/auth/devices", registration.Data.Token)
	devicesResponse := httptest.NewRecorder()
	mux.ServeHTTP(devicesResponse, devicesRequest)
	if devicesResponse.Code != http.StatusOK {
		t.Fatalf("list devices status = %d, body = %s", devicesResponse.Code, devicesResponse.Body.String())
	}
	var devices struct {
		Data []models.Device `json:"data"`
	}
	if err := json.Unmarshal(devicesResponse.Body.Bytes(), &devices); err != nil {
		t.Fatalf("decode devices: %v", err)
	}
	if len(devices.Data) != 1 || devices.Data[0].DeviceUID != "web-test-device" || !devices.Data[0].Active {
		t.Fatalf("registered devices = %+v", devices.Data)
	}

	revokePath := fmt.Sprintf("/api/v1/auth/devices/%d", registration.Data.Device.ID)
	revokeRequest := authenticatedRequest(http.MethodDelete, revokePath, registration.Data.Token)
	revokeResponse := httptest.NewRecorder()
	mux.ServeHTTP(revokeResponse, revokeRequest)
	if revokeResponse.Code != http.StatusOK {
		t.Fatalf("revoke status = %d, body = %s", revokeResponse.Code, revokeResponse.Body.String())
	}

	profileRequest := authenticatedRequest(http.MethodGet, "/api/v1/profile", registration.Data.Token)
	profileResponse := httptest.NewRecorder()
	mux.ServeHTTP(profileResponse, profileRequest)
	if profileResponse.Code != http.StatusUnauthorized {
		t.Fatalf("revoked session profile status = %d; want %d", profileResponse.Code, http.StatusUnauthorized)
	}
}

func authenticatedRequest(method, path, token string) *http.Request {
	request := httptest.NewRequest(method, path, nil)
	request.Header.Set("Authorization", "Bearer "+token)
	return request
}
