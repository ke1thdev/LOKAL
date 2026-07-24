package handlers

import (
	"path/filepath"
	"testing"

	"lokal-thesis/internal/database"
	"lokal-thesis/internal/models"
)

func TestRefreshAwardedParticipantsUsesTeacherThresholds(t *testing.T) {
	db, err := database.New(filepath.Join(t.TempDir(), "award-level-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher(
		"award-teacher", "award-teacher@example.test", "hash", "Teacher")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	class, err := db.CreateClass(teacher.ID, "Award Class", "AWARD", "#0f766e")
	if err != nil {
		t.Fatalf("create class: %v", err)
	}
	participant, err := db.AddParticipant(class.ID, "Learner", "award-device", "")
	if err != nil {
		t.Fatalf("create participant: %v", err)
	}

	levels := []models.StarLevel{
		{Level: 1, StarsRequired: 0, BadgeName: "One"},
		{Level: 2, StarsRequired: 2, BadgeName: "Two"},
		{Level: 3, StarsRequired: 4, BadgeName: "Three"},
	}
	if err = db.UpdateStarLevels(teacher.ID, levels); err != nil {
		t.Fatalf("set star thresholds: %v", err)
	}
	if err = db.AwardStars(participant.ID, 4); err != nil {
		t.Fatalf("award stars: %v", err)
	}

	handler := &Handler{DB: db}
	updated := handler.refreshAwardedParticipants(
		teacher.ID, []int64{participant.ID, participant.ID})
	if len(updated) != 1 {
		t.Fatalf("updated participants = %d; want one deduplicated snapshot", len(updated))
	}
	if updated[0].TotalStars != 4 || updated[0].Level != 3 {
		t.Fatalf("updated participant = stars %d, level %d; want stars 4, level 3",
			updated[0].TotalStars, updated[0].Level)
	}

	persisted, err := db.GetParticipantByID(participant.ID)
	if err != nil {
		t.Fatalf("reload participant: %v", err)
	}
	if persisted.Level != 3 {
		t.Fatalf("persisted level = %d; want 3", persisted.Level)
	}
}
