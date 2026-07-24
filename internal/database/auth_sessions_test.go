package database

import (
	"errors"
	"path/filepath"
	"testing"
	"time"

	"lokal-thesis/internal/auth"
	"lokal-thesis/internal/models"
)

func TestTeacherSessionFollowsDeviceRevocation(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "teacher-auth.db"))
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("teacher-one", "teacher@example.test", "hash", "Teacher One")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	device, err := db.RegisterDevice(models.DeviceRegistration{
		DeviceUID: "device-teacher-1", Name: "Teacher laptop", Platform: "windows",
	})
	if err != nil {
		t.Fatalf("register device: %v", err)
	}
	token, tokenHash := auth.GenerateOpaqueToken("lkt_")
	if err := db.CreateTeacherAuthSession(teacher.ID, device.ID, tokenHash, time.Now().Add(time.Hour)); err != nil {
		t.Fatalf("create session: %v", err)
	}

	authenticated, err := db.AuthenticateTeacherSession(auth.HashToken(token))
	if err != nil || authenticated.ID != teacher.ID {
		t.Fatalf("authenticate session = %#v, %v", authenticated, err)
	}
	devices, err := db.GetTeacherDevices(teacher.ID)
	if err != nil || len(devices) != 1 || !devices[0].Active {
		t.Fatalf("active devices = %#v, %v", devices, err)
	}
	if err := db.RevokeTeacherDevice(teacher.ID, device.ID); err != nil {
		t.Fatalf("revoke device: %v", err)
	}
	if _, err := db.AuthenticateTeacherSession(auth.HashToken(token)); err == nil {
		t.Fatal("revoked device session still authenticated")
	}
}

func TestStudentIdentityCannotBeClaimedByAnotherDevice(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "student-auth.db"))
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("teacher-two", "teacher2@example.test", "hash", "Teacher Two")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	class, err := db.CreateClass(teacher.ID, "Class A", "A1234", "#0B1F1C")
	if err != nil {
		t.Fatalf("create class: %v", err)
	}
	firstDevice, err := db.RegisterDevice(models.DeviceRegistration{
		DeviceUID: "student-device-1", Name: "Student browser", Platform: "web",
	})
	if err != nil {
		t.Fatalf("register first device: %v", err)
	}
	participant, err := db.RegisterJoiningParticipant(class.ID, "Learner", firstDevice.DeviceUID, "")
	if err != nil {
		t.Fatalf("join participant: %v", err)
	}
	token, tokenHash := auth.GenerateOpaqueToken("lks_")
	if err := db.CreateStudentAuthSession(participant.ID, firstDevice.ID, tokenHash, time.Now().Add(time.Hour)); err != nil {
		t.Fatalf("create student session: %v", err)
	}
	authenticated, err := db.AuthenticateStudentSession(auth.HashToken(token))
	if err != nil || authenticated.ID != participant.ID {
		t.Fatalf("authenticate student = %#v, %v", authenticated, err)
	}

	if _, err := db.RegisterJoiningParticipant(class.ID, "Learner", "student-device-2", ""); !errors.Is(err, ErrParticipantNameInUse) {
		t.Fatalf("second device claim error = %v; want %v", err, ErrParticipantNameInUse)
	}
	reconnected, err := db.RegisterJoiningParticipant(class.ID, "Learner Renamed", firstDevice.DeviceUID, "")
	if err != nil || reconnected.ID != participant.ID {
		t.Fatalf("same-device reconnect = %#v, %v", reconnected, err)
	}
}
