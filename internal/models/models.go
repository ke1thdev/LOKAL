package models

import (
	"encoding/json"
	"time"
)

// Teacher represents a teacher user account
type Teacher struct {
	ID           int64     `json:"id"`
	Username     string    `json:"username"`
	Email        string    `json:"email,omitempty"`
	PasswordHash string    `json:"-"`
	DisplayName  string    `json:"display_name"`
	AvatarURL    string    `json:"avatar_url,omitempty"`
	Organization string    `json:"organization,omitempty"`
	Profession   string    `json:"profession,omitempty"`
	CreatedAt    time.Time `json:"created_at"`
}

// Class represents a class/group of students
type Class struct {
	ID              int64     `json:"id"`
	TeacherID       int64     `json:"teacher_id"`
	Name            string    `json:"name"`
	Code            string    `json:"code"`
	AvatarColor     string    `json:"avatar_color"`
	IsLocked        bool      `json:"is_locked"`
	MaxParticipants int       `json:"max_participants"`
	CreatedAt       time.Time `json:"created_at"`
	// Virtual fields (from joins)
	ParticipantCount int    `json:"participant_count"`
	GroupCount       int    `json:"group_count"`
	TeacherName      string `json:"teacher_name,omitempty"`
}

// Group represents a named group of participants inside a class.
type Group struct {
	ID          int64     `json:"id"`
	ClassID     int64     `json:"class_id"`
	Name        string    `json:"name"`
	Color       string    `json:"color"`
	CreatedAt   time.Time `json:"created_at"`
	MemberCount int       `json:"member_count"`
}

// Participant represents a student who joined a class
type Participant struct {
	ID           int64  `json:"id"`
	ClassID      int64  `json:"class_id"`
	Name         string `json:"name"`
	DeviceID     string `json:"device_id,omitempty"`
	AvatarURL    string `json:"avatar_url,omitempty"`
	TotalStars   int    `json:"total_stars"`
	SessionStars int    `json:"session_stars"`
	// Sum of correct-answer response times for the active session. It is used
	// only as the Quiz Mode tiebreaker; lower is faster.
	SessionResponseTimeMs int64     `json:"session_response_time_ms"`
	Level                 int       `json:"level"`
	JoinedAt              time.Time `json:"joined_at"`
	GroupID               int64     `json:"group_id,omitempty"`
	GroupName             string    `json:"group_name,omitempty"`
	GroupColor            string    `json:"group_color,omitempty"`
}

// Session represents a live class session
type Session struct {
	ID        int64      `json:"id"`
	ClassID   int64      `json:"class_id"`
	StartedAt time.Time  `json:"started_at"`
	EndedAt   *time.Time `json:"ended_at,omitempty"`
	IsActive  bool       `json:"is_active"`
}

// ActivityType enum
type ActivityType string

const (
	ActivityMultipleChoice ActivityType = "multiple_choice"
	ActivityWordCloud      ActivityType = "word_cloud"
	ActivityShortAnswer    ActivityType = "short_answer"
	ActivityFillBlanks     ActivityType = "fill_blanks"
	ActivitySlideDrawing   ActivityType = "slide_drawing"
	ActivityImageUpload    ActivityType = "image_upload"
	ActivityAudioRecord    ActivityType = "audio_record"
	ActivityVideoUpload    ActivityType = "video_upload"
)

// Activity represents a quiz/activity launched during a session
type Activity struct {
	ID               int64           `json:"id"`
	SessionID        int64           `json:"session_id"`
	ClassID          int64           `json:"class_id"`
	Type             ActivityType    `json:"type"`
	QuestionText     string          `json:"question_text"`
	Config           json.RawMessage `json:"config"`
	IsQuizMode       bool            `json:"is_quiz_mode"`
	AutoCloseSeconds int             `json:"auto_close_seconds"`
	StartedAt        time.Time       `json:"started_at"`
	ClosedAt         *time.Time      `json:"closed_at,omitempty"`
	// Virtual fields
	ResponseCount int    `json:"response_count,omitempty"`
	ClassName     string `json:"class_name,omitempty"`
	IsFavorite    bool   `json:"is_favorite"`
}

// MultipleChoiceConfig holds configuration for multiple choice activities
type MultipleChoiceConfig struct {
	Choices       []string `json:"choices"`
	CorrectAnswer []int    `json:"correct_answer"` // indices of correct choices
	AllowMultiple bool     `json:"allow_multiple"`
	Difficulty    int      `json:"difficulty"`
}

// WordCloudConfig holds configuration for word cloud activities
type WordCloudConfig struct {
	MaxSubmissions int `json:"max_submissions"`
}

// ShortAnswerConfig holds configuration for short answer activities
type ShortAnswerConfig struct {
	CharacterLimit int `json:"character_limit"`
}

// FillBlanksConfig holds configuration for fill in the blanks activities
type FillBlanksConfig struct {
	Blanks []BlankField `json:"blanks"`
}

// BlankField represents a single blank field in fill-in-the-blanks
type BlankField struct {
	ID              string   `json:"id"`
	AcceptedAnswers []string `json:"accepted_answers"`
}

// Response represents a student's submission for an activity
type Response struct {
	ID             int64           `json:"id"`
	ActivityID     int64           `json:"activity_id"`
	ParticipantID  int64           `json:"participant_id"`
	Answer         json.RawMessage `json:"answer"`
	IsCorrect      *bool           `json:"is_correct,omitempty"`
	StarsEarned    int             `json:"stars_earned"`
	ResponseTimeMs int64           `json:"response_time_ms"`
	SubmittedAt    time.Time       `json:"submitted_at"`
	// Virtual fields
	ParticipantName string `json:"participant_name,omitempty"`
}

// StarLevel represents a star level configuration
type StarLevel struct {
	ID            int64  `json:"id"`
	TeacherID     int64  `json:"teacher_id"`
	Level         int    `json:"level"`
	StarsRequired int    `json:"stars_required"`
	BadgeName     string `json:"badge_name"`
}

// ---- API Request/Response Types ----

// LoginRequest for teacher login
type LoginRequest struct {
	Username string             `json:"username"`
	Password string             `json:"password"`
	Device   DeviceRegistration `json:"device"`
}

// RegisterRequest for teacher registration
type RegisterRequest struct {
	Username    string             `json:"username"`
	Email       string             `json:"email"`
	Password    string             `json:"password"`
	DisplayName string             `json:"display_name"`
	Device      DeviceRegistration `json:"device"`
}

// AuthResponse returned after login/register
type AuthResponse struct {
	Token     string    `json:"token"`
	Teacher   *Teacher  `json:"teacher"`
	Device    *Device   `json:"device,omitempty"`
	ExpiresAt time.Time `json:"expires_at"`
}

// DeviceRegistration identifies an application installation, not a physical
// hardware fingerprint. Clients generate and retain DeviceUID locally.
type DeviceRegistration struct {
	DeviceUID string `json:"id"`
	Name      string `json:"name"`
	Platform  string `json:"platform"`
	UserAgent string `json:"user_agent,omitempty"`
}

type Device struct {
	ID         int64      `json:"id"`
	DeviceUID  string     `json:"device_uid"`
	Name       string     `json:"name"`
	Platform   string     `json:"platform"`
	UserAgent  string     `json:"user_agent,omitempty"`
	CreatedAt  time.Time  `json:"created_at"`
	LastSeenAt time.Time  `json:"last_seen_at"`
	RevokedAt  *time.Time `json:"revoked_at,omitempty"`
	Active     bool       `json:"active"`
}

// CreateClassRequest for creating a new class
type CreateClassRequest struct {
	Name        string `json:"name"`
	Code        string `json:"code"`
	AvatarColor string `json:"avatar_color"`
}

// StudentJoinRequest for a student joining a class
type StudentJoinRequest struct {
	ClassCode string `json:"class_code"`
	Name      string `json:"name"`
	DeviceID  string `json:"device_id"`
	Avatar    string `json:"avatar,omitempty"` // base64 string
}

// StudentSubmitRequest for a student submitting a response
type StudentSubmitRequest struct {
	ActivityID     int64           `json:"activity_id"`
	ParticipantID  int64           `json:"participant_id"`
	Answer         json.RawMessage `json:"answer"`
	ResponseTimeMs int64           `json:"response_time_ms"`
}

// StartActivityRequest for starting an activity
type StartActivityRequest struct {
	SessionID        int64           `json:"session_id"`
	ClassID          int64           `json:"class_id"`
	Type             ActivityType    `json:"type"`
	QuestionText     string          `json:"question_text"`
	Config           json.RawMessage `json:"config"`
	IsQuizMode       bool            `json:"is_quiz_mode"`
	AutoCloseSeconds int             `json:"auto_close_seconds"`
}

// APIResponse is a generic API response wrapper
type APIResponse struct {
	Success bool        `json:"success"`
	Data    interface{} `json:"data,omitempty"`
	Error   string      `json:"error,omitempty"`
}

// WSMessage represents a WebSocket message
type WSMessage struct {
	Type    string          `json:"type"`
	Room    string          `json:"room,omitempty"`
	Payload json.RawMessage `json:"payload,omitempty"`
}

// ReportSummary for the reports page
type ReportSummary struct {
	SessionID        int64     `json:"session_id"`
	ClassName        string    `json:"class_name"`
	ClassCode        string    `json:"class_code"`
	SessionDate      time.Time `json:"session_date"`
	ActivitiesCount  int       `json:"activities_count"`
	ParticipantCount int       `json:"participant_count"`
	AvgScore         float64   `json:"avg_score"`
	StarsAwarded     int       `json:"stars_awarded"`
	TopPlayers       string    `json:"top_players"`
	IsFavorite       bool      `json:"is_favorite"`
}

// SessionParticipantResult represents a participant's score in a session
type SessionParticipantResult struct {
	Name      string `json:"name"`
	Score     int    `json:"score"`
	AvatarURL string `json:"avatar_url,omitempty"`
}

// QuizSummaryRow is the per-student row shown in the PowerPoint Quiz Mode
// summary. Response speed is kept in milliseconds so the add-in can display
// and sort it without losing precision.
type QuizSummaryRow struct {
	ParticipantID  int64   `json:"participant_id"`
	Name           string  `json:"name"`
	SubmittedCount int     `json:"submitted_count"`
	CorrectCount   int     `json:"correct_count"`
	StarsEarned    int     `json:"stars_earned"`
	AverageTimeMs  float64 `json:"average_time_ms"`
}

type QuizSessionSummary struct {
	SessionID     int64            `json:"session_id"`
	QuestionCount int              `json:"question_count"`
	Rows          []QuizSummaryRow `json:"rows"`
}
