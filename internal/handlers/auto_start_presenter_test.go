package handlers

import (
	"encoding/json"
	"net/http/httptest"
	"path/filepath"
	"testing"
	"time"

	"lokal-thesis/internal/auth"
	"lokal-thesis/internal/database"
	"lokal-thesis/internal/hub"
	"lokal-thesis/internal/models"
)

func TestAutoStartSessionUsesAuthenticatedPresenter(t *testing.T) {
	db, err := database.New(filepath.Join(t.TempDir(), "auto-start-presenter.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	presenter, err := db.CreateTeacher(
		"actual-presenter", "actual-presenter@example.test", "hash", "Actual Presenter")
	if err != nil {
		t.Fatalf("create presenter: %v", err)
	}
	token, err := auth.GenerateToken(presenter.ID, presenter.Username)
	if err != nil {
		t.Fatalf("generate presenter token: %v", err)
	}

	request := httptest.NewRequest("POST", "/api/v1/session/auto-start", nil)
	request.Header.Set("Authorization", "Bearer "+token)
	response := httptest.NewRecorder()

	handler := New(db, hub.NewHub())
	handler.AutoStartSession(response, request)
	if response.Code != 201 {
		t.Fatalf("auto-start status = %d, body = %s", response.Code, response.Body.String())
	}

	var envelope struct {
		Data struct {
			ClassID int64 `json:"class_id"`
		} `json:"data"`
	}
	if err = json.Unmarshal(response.Body.Bytes(), &envelope); err != nil {
		t.Fatalf("decode auto-start response: %v", err)
	}

	class, err := db.GetClassByID(envelope.Data.ClassID)
	if err != nil {
		t.Fatalf("reload auto-start class: %v", err)
	}
	if class.TeacherID != presenter.ID {
		t.Fatalf("class teacher id = %d; want %d", class.TeacherID, presenter.ID)
	}
	if class.TeacherName != presenter.DisplayName {
		t.Fatalf("class teacher name = %q; want %q", class.TeacherName, presenter.DisplayName)
	}
}

func TestAutoStartSessionAcceptsRegisteredDeviceSession(t *testing.T) {
	db, err := database.New(filepath.Join(t.TempDir(), "auto-start-device.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	presenter, err := db.CreateTeacher(
		"device-presenter", "device-presenter@example.test", "hash", "Device Presenter")
	if err != nil {
		t.Fatalf("create presenter: %v", err)
	}
	device, err := db.RegisterDevice(models.DeviceRegistration{
		DeviceUID: "powerpoint-device", Name: "PowerPoint", Platform: "windows-powerpoint",
	})
	if err != nil {
		t.Fatalf("register device: %v", err)
	}
	rawToken, tokenHash := auth.GenerateOpaqueToken("lkt_")
	if err := db.CreateTeacherAuthSession(presenter.ID, device.ID, tokenHash, time.Now().Add(time.Hour)); err != nil {
		t.Fatalf("create teacher session: %v", err)
	}

	request := httptest.NewRequest("POST", "/api/v1/session/auto-start", nil)
	request.Header.Set("Authorization", "Bearer "+rawToken)
	response := httptest.NewRecorder()
	New(db, hub.NewHub()).AutoStartSession(response, request)
	if response.Code != 201 {
		t.Fatalf("auto-start status = %d, body = %s", response.Code, response.Body.String())
	}
}
