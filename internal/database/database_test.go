package database

import (
	"encoding/json"
	"path/filepath"
	"strings"
	"testing"

	"lokal-thesis/internal/models"
)

func TestSubmitResponseMultipleChoiceScoringAndClosedState(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "lokal-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("teacher", "teacher@example.test", "hash", "Teacher")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	class, err := db.CreateClass(teacher.ID, "Test Class", "TEST1", "#0f766e")
	if err != nil {
		t.Fatalf("create class: %v", err)
	}
	participant, err := db.AddParticipant(class.ID, "Learner", "device-1", "")
	if err != nil {
		t.Fatalf("create participant: %v", err)
	}
	session, err := db.StartSession(class.ID)
	if err != nil {
		t.Fatalf("start session: %v", err)
	}

	config := json.RawMessage(`{"num_choices":4,"correct_answer":[1],"allow_multiple":false,"difficulty":2}`)
	answer := json.RawMessage(`[1]`)

	t.Run("correct non-quiz response earns no automatic stars", func(t *testing.T) {
		activity, createErr := db.CreateActivity(models.StartActivityRequest{
			SessionID: session.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
			QuestionText: "Question", Config: config, IsQuizMode: false,
		})
		if createErr != nil {
			t.Fatalf("create activity: %v", createErr)
		}

		response, submitErr := db.SubmitResponse(activity.ID, participant.ID, answer, 1200)
		if submitErr != nil {
			t.Fatalf("submit response: %v", submitErr)
		}
		if response.IsCorrect == nil || !*response.IsCorrect {
			t.Fatalf("expected response to be correct, got %#v", response.IsCorrect)
		}
		if response.StarsEarned != 0 {
			t.Fatalf("non-quiz response earned %d stars; want 0", response.StarsEarned)
		}
	})

	t.Run("quiz response uses configured difficulty", func(t *testing.T) {
		activity, createErr := db.CreateActivity(models.StartActivityRequest{
			SessionID: session.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
			QuestionText: "Question", Config: config, IsQuizMode: true,
		})
		if createErr != nil {
			t.Fatalf("create activity: %v", createErr)
		}

		response, submitErr := db.SubmitResponse(activity.ID, participant.ID, answer, 900)
		if submitErr != nil {
			t.Fatalf("submit response: %v", submitErr)
		}
		if response.StarsEarned != 2 {
			t.Fatalf("quiz response earned %d stars; want 2", response.StarsEarned)
		}
	})

	t.Run("closed activity rejects late responses", func(t *testing.T) {
		activity, createErr := db.CreateActivity(models.StartActivityRequest{
			SessionID: session.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
			QuestionText: "Question", Config: config, IsQuizMode: true,
		})
		if createErr != nil {
			t.Fatalf("create activity: %v", createErr)
		}
		if closeErr := db.CloseActivity(activity.ID); closeErr != nil {
			t.Fatalf("close activity: %v", closeErr)
		}

		_, submitErr := db.SubmitResponse(activity.ID, participant.ID, answer, 1000)
		if submitErr == nil || !strings.Contains(submitErr.Error(), "closed") {
			t.Fatalf("late response error = %v; want submissions closed", submitErr)
		}
	})
}

func TestLeaderboardSeparatesSessionAndLifetimeStars(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "lokal-leaderboard-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("rank-teacher", "rank@example.test", "hash", "Teacher")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	class, err := db.CreateClass(teacher.ID, "Rank Class", "RANK1", "#0f766e")
	if err != nil {
		t.Fatalf("create class: %v", err)
	}
	participant, err := db.AddParticipant(class.ID, "Learner", "rank-device", "")
	if err != nil {
		t.Fatalf("create participant: %v", err)
	}
	if err = db.AwardStars(participant.ID, 9); err != nil {
		t.Fatalf("seed lifetime stars: %v", err)
	}

	session, err := db.StartSession(class.ID)
	if err != nil {
		t.Fatalf("start session: %v", err)
	}
	config := json.RawMessage(`{"correct_answer":[0],"difficulty":2}`)
	activity, err := db.CreateActivity(models.StartActivityRequest{
		SessionID: session.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
		Config: config, IsQuizMode: true,
	})
	if err != nil {
		t.Fatalf("create activity: %v", err)
	}
	if _, err = db.SubmitResponse(activity.ID, participant.ID, json.RawMessage(`[0]`), 500); err != nil {
		t.Fatalf("submit response: %v", err)
	}

	beforeClose, err := db.GetLeaderboard(class.ID, session.ID)
	if err != nil || len(beforeClose) != 1 {
		t.Fatalf("leaderboard before close: %#v, %v", beforeClose, err)
	}
	if beforeClose[0].SessionStars != 0 {
		t.Fatalf("open activity contributed %d session stars; want 0", beforeClose[0].SessionStars)
	}

	if err = db.CloseActivity(activity.ID); err != nil {
		t.Fatalf("close activity: %v", err)
	}
	afterClose, err := db.GetLeaderboard(class.ID, session.ID)
	if err != nil || len(afterClose) != 1 {
		t.Fatalf("leaderboard after close: %#v, %v", afterClose, err)
	}
	if afterClose[0].SessionStars != 2 {
		t.Fatalf("session stars = %d; want 2", afterClose[0].SessionStars)
	}
	if afterClose[0].TotalStars != 9 {
		t.Fatalf("lifetime stars = %d; want independently stored 9", afterClose[0].TotalStars)
	}
}

func TestTeacherProfileAndClassGroupsPersist(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "lokal-web-parity-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, err := db.CreateTeacher("web-teacher", "before@example.test", "hash", "Before")
	if err != nil {
		t.Fatalf("create teacher: %v", err)
	}
	updated, err := db.UpdateTeacherProfile(
		teacher.ID, "Randy Bello", "randy@example.test", "/assets/avatar.png",
		"DEBESMSCAT", "Instructor",
	)
	if err != nil {
		t.Fatalf("update profile: %v", err)
	}
	if updated.DisplayName != "Randy Bello" || updated.Organization != "DEBESMSCAT" ||
		updated.Profession != "Instructor" {
		t.Fatalf("updated profile = %#v", updated)
	}

	class, err := db.CreateClass(teacher.ID, "Web Class", "WEB01", "#0f766e")
	if err != nil {
		t.Fatalf("create class: %v", err)
	}
	participant, err := db.AddParticipant(class.ID, "Learner", "web-device", "")
	if err != nil {
		t.Fatalf("create participant: %v", err)
	}
	group, err := db.CreateGroup(class.ID, "Green Team", "#0B1F1C")
	if err != nil {
		t.Fatalf("create group: %v", err)
	}
	if err = db.SetParticipantGroup(class.ID, participant.ID, group.ID); err != nil {
		t.Fatalf("assign participant group: %v", err)
	}

	participants, err := db.GetParticipantsByClass(class.ID)
	if err != nil || len(participants) != 1 {
		t.Fatalf("participants = %#v, %v", participants, err)
	}
	if participants[0].GroupID != group.ID || participants[0].GroupName != "Green Team" ||
		participants[0].GroupColor != "#0B1F1C" {
		t.Fatalf("participant group = %#v", participants[0])
	}

	groups, err := db.GetGroupsByClass(class.ID)
	if err != nil || len(groups) != 1 || groups[0].MemberCount != 1 {
		t.Fatalf("groups = %#v, %v", groups, err)
	}

	if err = db.DeleteGroup(group.ID, class.ID); err != nil {
		t.Fatalf("delete group: %v", err)
	}
	participants, err = db.GetParticipantsByClass(class.ID)
	if err != nil || participants[0].GroupID != 0 {
		t.Fatalf("participant remained grouped after delete: %#v, %v", participants, err)
	}
}

func TestLeaderboardIncludesQuizSpeedTiebreaker(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "lokal-speed-rank-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, _ := db.CreateTeacher("speed-teacher", "speed@example.test", "hash", "Teacher")
	class, _ := db.CreateClass(teacher.ID, "Speed Rank", "SPEED1", "#0f766e")
	fast, _ := db.AddParticipant(class.ID, "Fast", "fast-device", "")
	slow, _ := db.AddParticipant(class.ID, "Slow", "slow-device", "")
	session, _ := db.StartSession(class.ID)
	config := json.RawMessage(`{"correct_answer":[0],"difficulty":2}`)
	activity, _ := db.CreateActivity(models.StartActivityRequest{
		SessionID: session.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
		Config: config, IsQuizMode: true,
	})
	if _, err = db.SubmitResponse(activity.ID, fast.ID, json.RawMessage(`[0]`), 450); err != nil {
		t.Fatalf("submit fast response: %v", err)
	}
	if _, err = db.SubmitResponse(activity.ID, slow.ID, json.RawMessage(`[0]`), 2100); err != nil {
		t.Fatalf("submit slow response: %v", err)
	}
	if err = db.CloseActivity(activity.ID); err != nil {
		t.Fatalf("close activity: %v", err)
	}

	ranked, err := db.GetLeaderboard(class.ID, session.ID)
	if err != nil || len(ranked) != 2 {
		t.Fatalf("leaderboard: %#v, %v", ranked, err)
	}
	byName := map[string]models.Participant{}
	for _, participant := range ranked {
		byName[participant.Name] = participant
	}
	if byName["Fast"].SessionStars != byName["Slow"].SessionStars {
		t.Fatalf("expected equal stars, got fast=%d slow=%d",
			byName["Fast"].SessionStars, byName["Slow"].SessionStars)
	}
	if byName["Fast"].SessionResponseTimeMs >= byName["Slow"].SessionResponseTimeMs {
		t.Fatalf("speed tiebreaker fast=%d slow=%d",
			byName["Fast"].SessionResponseTimeMs, byName["Slow"].SessionResponseTimeMs)
	}
}

func TestDeleteResponsesBySessionPreservesActivitiesAndOtherSessions(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "lokal-reset-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, _ := db.CreateTeacher("reset-teacher", "reset@example.test", "hash", "Teacher")
	class, _ := db.CreateClass(teacher.ID, "Reset Class", "RESET1", "#0f766e")
	participant, _ := db.AddParticipant(class.ID, "Learner", "reset-device", "")
	sessionOne, _ := db.StartSession(class.ID)
	sessionTwo, _ := db.StartSession(class.ID)
	config := json.RawMessage(`{"correct_answer":[0]}`)

	activityOne, _ := db.CreateActivity(models.StartActivityRequest{
		SessionID: sessionOne.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
		Config: config,
	})
	activityTwo, _ := db.CreateActivity(models.StartActivityRequest{
		SessionID: sessionTwo.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
		Config: config,
	})
	if _, err = db.SubmitResponse(activityOne.ID, participant.ID, json.RawMessage(`[0]`), 500); err != nil {
		t.Fatalf("submit session one response: %v", err)
	}
	if _, err = db.SubmitResponse(activityTwo.ID, participant.ID, json.RawMessage(`[0]`), 600); err != nil {
		t.Fatalf("submit session two response: %v", err)
	}

	if err = db.DeleteResponsesBySession(sessionOne.ID); err != nil {
		t.Fatalf("delete session responses: %v", err)
	}
	responsesOne, _ := db.GetResponsesByActivity(activityOne.ID)
	responsesTwo, _ := db.GetResponsesByActivity(activityTwo.ID)
	if len(responsesOne) != 0 {
		t.Fatalf("session one still has %d responses; want 0", len(responsesOne))
	}
	if len(responsesTwo) != 1 {
		t.Fatalf("session two has %d responses; want 1", len(responsesTwo))
	}
}

func TestQuizSessionSummaryIncludesParticipationCorrectnessStarsAndSpeed(t *testing.T) {
	db, err := New(filepath.Join(t.TempDir(), "lokal-quiz-summary-test.db"))
	if err != nil {
		t.Fatalf("open test database: %v", err)
	}
	defer db.Close()

	teacher, _ := db.CreateTeacher("summary-teacher", "summary@example.test", "hash", "Teacher")
	class, _ := db.CreateClass(teacher.ID, "Summary Class", "SUMM1", "#0f766e")
	learner, _ := db.AddParticipant(class.ID, "Learner", "summary-device-1", "")
	_, _ = db.AddParticipant(class.ID, "No response", "summary-device-2", "")
	session, _ := db.StartSession(class.ID)
	config := json.RawMessage(`{"correct_answer":[0],"difficulty":3}`)
	activity, _ := db.CreateActivity(models.StartActivityRequest{
		SessionID: session.ID, ClassID: class.ID, Type: models.ActivityMultipleChoice,
		Config: config, IsQuizMode: true,
	})
	if _, err = db.SubmitResponse(activity.ID, learner.ID, json.RawMessage(`[0]`), 1250); err != nil {
		t.Fatalf("submit quiz response: %v", err)
	}

	summary, err := db.GetQuizSessionSummary(session.ID)
	if err != nil {
		t.Fatalf("get quiz summary: %v", err)
	}
	if summary.QuestionCount != 1 || len(summary.Rows) != 2 {
		t.Fatalf("summary questions=%d rows=%d; want 1 and 2", summary.QuestionCount, len(summary.Rows))
	}
	var learnerRow models.QuizSummaryRow
	for _, row := range summary.Rows {
		if row.Name == "Learner" {
			learnerRow = row
		}
	}
	if learnerRow.SubmittedCount != 1 || learnerRow.CorrectCount != 1 ||
		learnerRow.StarsEarned != 3 || learnerRow.AverageTimeMs != 1250 {
		t.Fatalf("unexpected learner summary: %#v", learnerRow)
	}
}
