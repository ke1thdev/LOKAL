package database

import (
	"context"
	"database/sql"
	"encoding/json"
	"fmt"
	"log"
	"time"

	"lokal-thesis/internal/models"
)

// DB wraps the sql.DB connection
type DB struct {
	*sql.DB
	provider Provider
}

// New preserves the original offline API and opens the default SQLite provider.
func New(dbPath string) (*DB, error) {
	return Open(Config{Provider: DefaultProvider, Path: dbPath})
}

// Open initializes a registered database provider.
func Open(config Config) (*DB, error) {
	provider, err := lookupProvider(config.Provider)
	if err != nil {
		return nil, err
	}
	dsn, err := provider.DataSourceName(config)
	if err != nil {
		return nil, fmt.Errorf("configure %s database: %w", provider.Name(), err)
	}
	db, err := sql.Open(provider.DriverName(), dsn)
	if err != nil {
		return nil, fmt.Errorf("open %s database: %w", provider.Name(), err)
	}
	if config.MaxOpenConns > 0 {
		db.SetMaxOpenConns(config.MaxOpenConns)
	}
	if config.MaxIdleConns > 0 {
		db.SetMaxIdleConns(config.MaxIdleConns)
	}
	if config.ConnMaxLifetime > 0 {
		db.SetConnMaxLifetime(config.ConnMaxLifetime)
	}
	connectTimeout := config.ConnectTimeout
	if connectTimeout <= 0 {
		connectTimeout = 10 * time.Second
	}
	pingContext, cancelPing := context.WithTimeout(context.Background(), connectTimeout)
	defer cancelPing()
	if err := db.PingContext(pingContext); err != nil {
		db.Close()
		return nil, fmt.Errorf("ping %s database: %w", provider.Name(), err)
	}
	if err := provider.Migrate(db); err != nil {
		db.Close()
		return nil, fmt.Errorf("migrate %s database: %w", provider.Name(), err)
	}
	d := &DB{DB: db, provider: provider}
	log.Printf("[DB] %s database ready", provider.Name())
	return d, nil
}

func (d *DB) ProviderName() string { return d.provider.Name() }

func (d *DB) Exec(query string, args ...any) (sql.Result, error) {
	return d.DB.Exec(d.provider.Rebind(query), args...)
}

func (d *DB) Query(query string, args ...any) (*sql.Rows, error) {
	return d.DB.Query(d.provider.Rebind(query), args...)
}

func (d *DB) QueryRow(query string, args ...any) *sql.Row {
	return d.DB.QueryRow(d.provider.Rebind(query), args...)
}

func (d *DB) insertID(query, idColumn string, args ...any) (int64, error) {
	return d.provider.InsertID(d.DB, d.provider.Rebind(query), idColumn, args...)
}

// Tx preserves provider placeholder handling inside transactions.
type Tx struct {
	*sql.Tx
	provider Provider
}

func (d *DB) Begin() (*Tx, error) {
	tx, err := d.DB.Begin()
	if err != nil {
		return nil, err
	}
	return &Tx{Tx: tx, provider: d.provider}, nil
}

func (tx *Tx) Exec(query string, args ...any) (sql.Result, error) {
	return tx.Tx.Exec(tx.provider.Rebind(query), args...)
}

func (tx *Tx) Query(query string, args ...any) (*sql.Rows, error) {
	return tx.Tx.Query(tx.provider.Rebind(query), args...)
}

func (tx *Tx) QueryRow(query string, args ...any) *sql.Row {
	return tx.Tx.QueryRow(tx.provider.Rebind(query), args...)
}

// ========== Teacher CRUD ==========

func (d *DB) CreateTeacher(username, email, passwordHash, displayName string) (*models.Teacher, error) {
	id, err := d.insertID(
		`INSERT INTO teachers (username, email, password_hash, display_name) VALUES (?, ?, ?, ?)`,
		"id",
		username, email, passwordHash, displayName,
	)
	if err != nil {
		return nil, err
	}
	return d.GetTeacherByID(id)
}

func (d *DB) GetTeacherByUsername(username string) (*models.Teacher, error) {
	t := &models.Teacher{}
	err := d.QueryRow(
		`SELECT id, username, COALESCE(email,''), password_hash, COALESCE(display_name,''), COALESCE(avatar_url,''), COALESCE(organization,''), COALESCE(profession,''), created_at FROM teachers WHERE username = ?`, username,
	).Scan(&t.ID, &t.Username, &t.Email, &t.PasswordHash, &t.DisplayName, &t.AvatarURL, &t.Organization, &t.Profession, &t.CreatedAt)
	if err != nil {
		return nil, err
	}
	return t, nil
}

func (d *DB) GetTeacherByID(id int64) (*models.Teacher, error) {
	t := &models.Teacher{}
	err := d.QueryRow(
		`SELECT id, username, COALESCE(email,''), password_hash, COALESCE(display_name,''), COALESCE(avatar_url,''), COALESCE(organization,''), COALESCE(profession,''), created_at FROM teachers WHERE id = ?`, id,
	).Scan(&t.ID, &t.Username, &t.Email, &t.PasswordHash, &t.DisplayName, &t.AvatarURL, &t.Organization, &t.Profession, &t.CreatedAt)
	if err != nil {
		return nil, err
	}
	return t, nil
}

func (d *DB) UpdateTeacherProfile(id int64, displayName, email, avatarURL, organization, profession string) (*models.Teacher, error) {
	_, err := d.Exec(
		`UPDATE teachers SET display_name = ?, email = NULLIF(?, ''), avatar_url = ?, organization = ?, profession = ? WHERE id = ?`,
		displayName, email, avatarURL, organization, profession, id,
	)
	if err != nil {
		return nil, err
	}
	return d.GetTeacherByID(id)
}

// ========== Class CRUD ==========

func (d *DB) CreateClass(teacherID int64, name, code, avatarColor string) (*models.Class, error) {
	id, err := d.insertID(
		`INSERT INTO classes (teacher_id, name, code, avatar_color) VALUES (?, ?, ?, ?)`,
		"id",
		teacherID, name, code, avatarColor,
	)
	if err != nil {
		return nil, err
	}
	return d.GetClassByID(id)
}

func (d *DB) GetClassByID(id int64) (*models.Class, error) {
	c := &models.Class{}
	err := d.QueryRow(`
		SELECT c.id, c.teacher_id, c.name, c.code, c.avatar_color, c.is_locked, c.max_participants, c.created_at,
			(SELECT COUNT(*) FROM participants WHERE class_id = c.id) as participant_count,
			(SELECT COUNT(*) FROM groups WHERE class_id = c.id) as group_count,
			COALESCE(NULLIF(t.display_name, ''), t.username) as teacher_name
		FROM classes c 
		JOIN teachers t ON t.id = c.teacher_id
		WHERE c.id = ?`, id,
	).Scan(&c.ID, &c.TeacherID, &c.Name, &c.Code, &c.AvatarColor, &c.IsLocked, &c.MaxParticipants, &c.CreatedAt, &c.ParticipantCount, &c.GroupCount, &c.TeacherName)
	if err != nil {
		return nil, err
	}
	return c, nil
}

func (d *DB) GetClassByCode(code string) (*models.Class, error) {
	c := &models.Class{}
	err := d.QueryRow(`
		SELECT c.id, c.teacher_id, c.name, c.code, c.avatar_color, c.is_locked, c.max_participants, c.created_at,
			(SELECT COUNT(*) FROM participants WHERE class_id = c.id) as participant_count,
			(SELECT COUNT(*) FROM groups WHERE class_id = c.id) as group_count,
			COALESCE(NULLIF(t.display_name, ''), t.username) as teacher_name
		FROM classes c 
		JOIN teachers t ON t.id = c.teacher_id
		WHERE UPPER(c.code) = UPPER(?)`, code,
	).Scan(&c.ID, &c.TeacherID, &c.Name, &c.Code, &c.AvatarColor, &c.IsLocked, &c.MaxParticipants, &c.CreatedAt, &c.ParticipantCount, &c.GroupCount, &c.TeacherName)
	if err != nil {
		return nil, err
	}
	return c, nil
}

func (d *DB) GetClassesByTeacher(teacherID int64) ([]models.Class, error) {
	rows, err := d.Query(`
		SELECT c.id, c.teacher_id, c.name, c.code, c.avatar_color, c.is_locked, c.max_participants, c.created_at,
			(SELECT COUNT(*) FROM participants WHERE class_id = c.id) as participant_count,
			(SELECT COUNT(*) FROM groups WHERE class_id = c.id) as group_count,
			COALESCE(NULLIF(t.display_name, ''), t.username) as teacher_name
		FROM classes c 
		JOIN teachers t ON t.id = c.teacher_id
		WHERE c.teacher_id = ? 
		ORDER BY c.created_at ASC`, teacherID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var classes []models.Class
	for rows.Next() {
		c := models.Class{}
		if err := rows.Scan(&c.ID, &c.TeacherID, &c.Name, &c.Code, &c.AvatarColor, &c.IsLocked, &c.MaxParticipants, &c.CreatedAt, &c.ParticipantCount, &c.GroupCount, &c.TeacherName); err != nil {
			return nil, err
		}
		classes = append(classes, c)
	}
	return classes, nil
}

func (d *DB) UpdateClass(id int64, name, code, avatarColor string) (*models.Class, error) {
	_, err := d.Exec(
		`UPDATE classes SET name = ?, code = ?, avatar_color = ? WHERE id = ?`,
		name, code, avatarColor, id,
	)
	if err != nil {
		return nil, err
	}
	return d.GetClassByID(id)
}

// SetClassLocked changes only the join lock without overwriting class metadata.
func (d *DB) SetClassLocked(id int64, locked bool) error {
	_, err := d.Exec(`UPDATE classes SET is_locked = ? WHERE id = ?`, locked, id)
	return err
}

func (d *DB) DeleteClass(id int64) error {
	_, err := d.Exec(`DELETE FROM classes WHERE id = ?`, id)
	return err
}

// ========== Participants ==========

func (d *DB) AddParticipant(classID int64, name, deviceID, avatarURL string) (*models.Participant, error) {
	// Check if participant with this name already exists in class
	var existing models.Participant
	err := d.QueryRow(
		`SELECT id, class_id, name, COALESCE(device_id,''), COALESCE(avatar_url,''), total_stars, level, joined_at FROM participants WHERE class_id = ? AND name = ?`,
		classID, name,
	).Scan(&existing.ID, &existing.ClassID, &existing.Name, &existing.DeviceID, &existing.AvatarURL, &existing.TotalStars, &existing.Level, &existing.JoinedAt)
	if err == nil {
		// Update avatar if provided
		if avatarURL != "" && avatarURL != existing.AvatarURL {
			d.Exec(`UPDATE participants SET avatar_url = ? WHERE id = ?`, avatarURL, existing.ID)
			existing.AvatarURL = avatarURL
		}
		return &existing, nil // Already exists, return existing
	}

	id, err := d.insertID(
		`INSERT INTO participants (class_id, name, device_id, avatar_url) VALUES (?, ?, ?, ?)`,
		"id",
		classID, name, deviceID, avatarURL,
	)
	if err != nil {
		return nil, err
	}
	p := &models.Participant{
		ID: id, ClassID: classID, Name: name, DeviceID: deviceID,
		TotalStars: 0, Level: 1, JoinedAt: time.Now(),
	}
	return p, nil
}

func (d *DB) GetParticipantsByClass(classID int64) ([]models.Participant, error) {
	rows, err := d.Query(
		`SELECT p.id, p.class_id, p.name, COALESCE(p.device_id,''), COALESCE(p.avatar_url,''), p.total_stars, p.level, p.joined_at,
		        COALESCE(g.id,0), COALESCE(g.name,''), COALESCE(g.color,'')
		   FROM participants p
		   LEFT JOIN group_members gm ON gm.participant_id = p.id
		   LEFT JOIN groups g ON g.id = gm.group_id
		  WHERE p.class_id = ? ORDER BY p.name ASC`, classID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var participants []models.Participant
	for rows.Next() {
		p := models.Participant{}
		if err := rows.Scan(&p.ID, &p.ClassID, &p.Name, &p.DeviceID, &p.AvatarURL, &p.TotalStars, &p.Level, &p.JoinedAt, &p.GroupID, &p.GroupName, &p.GroupColor); err != nil {
			return nil, err
		}
		participants = append(participants, p)
	}
	return participants, nil
}

// ========== Groups ==========

func (d *DB) GetGroupsByClass(classID int64) ([]models.Group, error) {
	rows, err := d.Query(`
		SELECT g.id, g.class_id, g.name, g.color, g.created_at, COUNT(gm.id)
		  FROM groups g
		  LEFT JOIN group_members gm ON gm.group_id = g.id
		 WHERE g.class_id = ?
		 GROUP BY g.id
		 ORDER BY g.created_at ASC`, classID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	groups := []models.Group{}
	for rows.Next() {
		var g models.Group
		if err := rows.Scan(&g.ID, &g.ClassID, &g.Name, &g.Color, &g.CreatedAt, &g.MemberCount); err != nil {
			return nil, err
		}
		groups = append(groups, g)
	}
	return groups, rows.Err()
}

func (d *DB) CreateGroup(classID int64, name, color string) (*models.Group, error) {
	id, err := d.insertID(`INSERT INTO groups (class_id, name, color) VALUES (?, ?, ?)`, "id", classID, name, color)
	if err != nil {
		return nil, err
	}
	var g models.Group
	err = d.QueryRow(`SELECT id, class_id, name, color, created_at, 0 FROM groups WHERE id = ?`, id).
		Scan(&g.ID, &g.ClassID, &g.Name, &g.Color, &g.CreatedAt, &g.MemberCount)
	return &g, err
}

func (d *DB) UpdateGroup(id, classID int64, name, color string) error {
	res, err := d.Exec(`UPDATE groups SET name = ?, color = ? WHERE id = ? AND class_id = ?`, name, color, id, classID)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (d *DB) DeleteGroup(id, classID int64) error {
	res, err := d.Exec(`DELETE FROM groups WHERE id = ? AND class_id = ?`, id, classID)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (d *DB) SetParticipantGroup(classID, participantID, groupID int64) error {
	tx, err := d.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	var participantClassID int64
	if err := tx.QueryRow(`SELECT class_id FROM participants WHERE id = ?`, participantID).Scan(&participantClassID); err != nil {
		return err
	}
	if participantClassID != classID {
		return sql.ErrNoRows
	}
	if groupID != 0 {
		var groupClassID int64
		if err := tx.QueryRow(`SELECT class_id FROM groups WHERE id = ?`, groupID).Scan(&groupClassID); err != nil {
			return err
		}
		if groupClassID != classID {
			return sql.ErrNoRows
		}
	}
	if _, err := tx.Exec(`DELETE FROM group_members WHERE participant_id = ?`, participantID); err != nil {
		return err
	}
	if groupID != 0 {
		if _, err := tx.Exec(`INSERT INTO group_members (group_id, participant_id) VALUES (?, ?)`, groupID, participantID); err != nil {
			return err
		}
	}
	return tx.Commit()
}

func (d *DB) ResetStars(classID int64) error {
	_, err := d.Exec(`UPDATE participants SET total_stars = 0, level = 1 WHERE class_id = ?`, classID)
	return err
}

func (d *DB) UpdateParticipant(id int64, name string, stars int, level int, avatarUrl string) error {
	_, err := d.Exec(`UPDATE participants SET name = ?, total_stars = ?, level = ?, avatar_url = ? WHERE id = ?`, name, stars, level, avatarUrl, id)
	return err
}

func (d *DB) DeleteParticipant(id int64) error {
	_, err := d.Exec(`DELETE FROM participants WHERE id = ?`, id)
	return err
}

func (d *DB) AwardStars(participantID int64, stars int) error {
	_, err := d.Exec(`UPDATE participants SET total_stars = total_stars + ? WHERE id = ?`, stars, participantID)
	return err
}

func (d *DB) GetParticipantByID(id int64) (*models.Participant, error) {
	p := &models.Participant{}
	err := d.QueryRow(
		`SELECT id, class_id, name, COALESCE(device_id,''), COALESCE(avatar_url,''), total_stars, level, joined_at FROM participants WHERE id = ?`, id,
	).Scan(&p.ID, &p.ClassID, &p.Name, &p.DeviceID, &p.AvatarURL, &p.TotalStars, &p.Level, &p.JoinedAt)
	if err != nil {
		return nil, err
	}
	return p, nil
}

func (d *DB) SetParticipantStarsAndLevel(id int64, stars int, level int) error {
	_, err := d.Exec(`UPDATE participants SET total_stars = ?, level = ? WHERE id = ?`, stars, level, id)
	return err
}

func (d *DB) AwardStarsToAll(activityID int64, stars int) error {
	_, err := d.Exec(`
		UPDATE participants SET total_stars = total_stars + ?
		WHERE id IN (SELECT DISTINCT participant_id FROM responses WHERE activity_id = ?)
	`, stars, activityID)
	return err
}

func (d *DB) AwardStarsToCorrect(activityID int64, stars int) error {
	_, err := d.Exec(`
		UPDATE participants SET total_stars = total_stars + ?
		WHERE id IN (SELECT DISTINCT participant_id FROM responses WHERE activity_id = ? AND is_correct = TRUE)
	`, stars, activityID)
	return err
}

// ========== Sessions ==========

func (d *DB) StartSession(classID int64) (*models.Session, error) {
	// Close any active sessions for this class first
	d.Exec(`UPDATE sessions SET is_active = FALSE, ended_at = CURRENT_TIMESTAMP WHERE class_id = ? AND is_active = TRUE`, classID)

	id, err := d.insertID(`INSERT INTO sessions (class_id) VALUES (?)`, "id", classID)
	if err != nil {
		return nil, err
	}
	return &models.Session{ID: id, ClassID: classID, StartedAt: time.Now(), IsActive: true}, nil
}

func (d *DB) StopSession(sessionID int64) error {
	_, err := d.Exec(`UPDATE sessions SET is_active = FALSE, ended_at = CURRENT_TIMESTAMP WHERE id = ?`, sessionID)
	return err
}

func (d *DB) GetActiveSession(classID int64) (*models.Session, error) {
	s := &models.Session{}
	err := d.QueryRow(
		`SELECT id, class_id, started_at, ended_at, is_active FROM sessions WHERE class_id = ? AND is_active = TRUE`,
		classID,
	).Scan(&s.ID, &s.ClassID, &s.StartedAt, &s.EndedAt, &s.IsActive)
	if err != nil {
		return nil, err
	}
	return s, nil
}

// ========== Activities ==========

func (d *DB) CreateActivity(req models.StartActivityRequest) (*models.Activity, error) {
	configBytes := req.Config
	if len(configBytes) > 0 && configBytes[0] == '"' {
		var s string
		json.Unmarshal(configBytes, &s)
		if s != "" {
			configBytes = []byte(s)
		}
	}

	id, err := d.insertID(
		`INSERT INTO activities (session_id, class_id, type, question_text, config, is_quiz_mode, auto_close_seconds) VALUES (?, ?, ?, ?, ?, ?, ?)`,
		"id",
		req.SessionID, req.ClassID, req.Type, req.QuestionText, string(configBytes), req.IsQuizMode, req.AutoCloseSeconds,
	)
	if err != nil {
		return nil, err
	}
	return &models.Activity{
		ID: id, SessionID: req.SessionID, ClassID: req.ClassID, Type: req.Type,
		QuestionText: req.QuestionText, Config: configBytes, IsQuizMode: req.IsQuizMode,
		AutoCloseSeconds: req.AutoCloseSeconds, StartedAt: time.Now(),
	}, nil
}

func (d *DB) CloseActivity(activityID int64) error {
	_, err := d.Exec(`UPDATE activities SET closed_at = CURRENT_TIMESTAMP WHERE id = ?`, activityID)
	return err
}

// CloseOpenActivities closes every still-open activity for a class
// (used when a session stops so stale activities don't linger).
func (d *DB) CloseOpenActivities(classID int64) error {
	_, err := d.Exec(`UPDATE activities SET closed_at = CURRENT_TIMESTAMP WHERE class_id = ? AND closed_at IS NULL`, classID)
	return err
}

func (d *DB) GetActivitiesByTeacher(teacherID int64, activityType string) ([]models.Activity, error) {
	query := `
		SELECT a.id, a.session_id, a.class_id, a.type, COALESCE(a.question_text,''), COALESCE(a.config,'{}'),
			a.is_quiz_mode, a.auto_close_seconds, a.started_at, a.closed_at, a.is_favorite,
			(SELECT COUNT(*) FROM responses WHERE activity_id = a.id) as response_count,
			c.name as class_name
		FROM activities a
		JOIN classes c ON c.id = a.class_id
		WHERE c.teacher_id = ?`
	args := []interface{}{teacherID}

	if activityType != "" && activityType != "all" {
		query += ` AND a.type = ?`
		args = append(args, activityType)
	}
	query += ` ORDER BY a.started_at DESC`

	rows, err := d.Query(query, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var activities []models.Activity
	for rows.Next() {
		a := models.Activity{}
		var config string // json.RawMessage can't be scanned directly
		if err := rows.Scan(&a.ID, &a.SessionID, &a.ClassID, &a.Type, &a.QuestionText, &config,
			&a.IsQuizMode, &a.AutoCloseSeconds, &a.StartedAt, &a.ClosedAt, &a.IsFavorite, &a.ResponseCount, &a.ClassName); err != nil {
			return nil, err
		}
		a.Config = json.RawMessage(config)
		activities = append(activities, a)
	}
	return activities, nil
}

// GetActiveActivity returns the latest still-open activity for a class,
// so late-joining students can pick up an activity already in progress.
func (d *DB) GetActiveActivity(classID int64) (*models.Activity, error) {
	a := &models.Activity{}
	var config string // json.RawMessage can't be scanned directly
	err := d.QueryRow(`
		SELECT a.id, a.session_id, a.class_id, a.type, COALESCE(a.question_text,''), COALESCE(a.config,'{}'),
			a.is_quiz_mode, a.auto_close_seconds, a.started_at, a.closed_at, a.is_favorite,
			(SELECT COUNT(*) FROM responses WHERE activity_id = a.id) as response_count,
			c.name as class_name
		FROM activities a JOIN classes c ON c.id = a.class_id
		WHERE a.class_id = ? AND a.closed_at IS NULL
		ORDER BY a.started_at DESC LIMIT 1`, classID,
	).Scan(&a.ID, &a.SessionID, &a.ClassID, &a.Type, &a.QuestionText, &config,
		&a.IsQuizMode, &a.AutoCloseSeconds, &a.StartedAt, &a.ClosedAt, &a.IsFavorite, &a.ResponseCount, &a.ClassName)
	if err != nil {
		return nil, err
	}
	a.Config = json.RawMessage(config)
	return a, nil
}

func (d *DB) GetActivityByID(id int64) (*models.Activity, error) {
	a := &models.Activity{}
	var config string // json.RawMessage can't be scanned directly
	err := d.QueryRow(`
		SELECT a.id, a.session_id, a.class_id, a.type, COALESCE(a.question_text,''), COALESCE(a.config,'{}'),
			a.is_quiz_mode, a.auto_close_seconds, a.started_at, a.closed_at, a.is_favorite,
			(SELECT COUNT(*) FROM responses WHERE activity_id = a.id) as response_count,
			c.name as class_name
		FROM activities a JOIN classes c ON c.id = a.class_id WHERE a.id = ?`, id,
	).Scan(&a.ID, &a.SessionID, &a.ClassID, &a.Type, &a.QuestionText, &config,
		&a.IsQuizMode, &a.AutoCloseSeconds, &a.StartedAt, &a.ClosedAt, &a.IsFavorite, &a.ResponseCount, &a.ClassName)
	if err != nil {
		return nil, err
	}
	a.Config = json.RawMessage(config)
	return a, nil
}

func (d *DB) DeleteActivity(id int64) error {
	// First delete responses associated with this activity (if any)
	_, err := d.Exec(`DELETE FROM responses WHERE activity_id = ?`, id)
	if err != nil {
		return err
	}
	// Delete the activity itself
	_, err = d.Exec(`DELETE FROM activities WHERE id = ?`, id)
	return err
}

// DeleteResponsesBySession clears collected answers while preserving the
// activities themselves. This lets PowerPoint reset question buttons and run
// the same questions again without deleting the slide/activity definitions.
func (d *DB) DeleteResponsesBySession(sessionID int64) error {
	_, err := d.Exec(`
		DELETE FROM responses
		WHERE activity_id IN (
			SELECT id FROM activities WHERE session_id = ?
		)`, sessionID)
	return err
}

func (d *DB) ToggleActivityFavorite(id int64) (bool, error) {
	_, err := d.Exec(`UPDATE activities SET is_favorite = NOT is_favorite WHERE id = ?`, id)
	if err != nil {
		return false, err
	}
	// Fetch new status
	var isFav bool
	err = d.QueryRow(`SELECT is_favorite FROM activities WHERE id = ?`, id).Scan(&isFav)
	return isFav, err
}

// ========== Responses ==========

func (d *DB) SubmitResponse(activityID, participantID int64, answer json.RawMessage, responseTimeMs int64) (*models.Response, error) {
	// Check if correct for auto-scoring
	activity, err := d.GetActivityByID(activityID)
	if err != nil {
		return nil, err
	}
	if activity.ClosedAt != nil {
		return nil, fmt.Errorf("submissions are closed")
	}

	var isCorrect *bool
	var starsEarned int

	if activity.Type == models.ActivityMultipleChoice {
		var config models.MultipleChoiceConfig
		json.Unmarshal(activity.Config, &config)
		if len(config.CorrectAnswer) > 0 {
			var studentAnswer []int
			json.Unmarshal(answer, &studentAnswer)
			correct := isAnswerCorrect(config.CorrectAnswer, studentAnswer)
			isCorrect = &correct
			if correct && activity.IsQuizMode {
				starsEarned = config.Difficulty
				if starsEarned <= 0 {
					starsEarned = 1
				}
			}
		}
	}

	// Check for existing response
	var existingID int64
	var existingStars int
	err = d.QueryRow(`SELECT id, stars_earned FROM responses WHERE activity_id = ? AND participant_id = ?`, activityID, participantID).Scan(&existingID, &existingStars)

	if err == nil {
		// Response exists, so we update it (UPSERT)
		_, err = d.Exec(
			`UPDATE responses SET answer = ?, is_correct = ?, stars_earned = ?, response_time_ms = ?, submitted_at = CURRENT_TIMESTAMP WHERE id = ?`,
			string(answer), isCorrect, starsEarned, responseTimeMs, existingID,
		)
		if err != nil {
			return nil, err
		}

		return &models.Response{
			ID: existingID, ActivityID: activityID, ParticipantID: participantID,
			Answer: answer, IsCorrect: isCorrect, StarsEarned: starsEarned,
			ResponseTimeMs: responseTimeMs, SubmittedAt: time.Now(),
		}, nil
	}

	// No existing response, INSERT new
	id, err := d.insertID(
		`INSERT INTO responses (activity_id, participant_id, answer, is_correct, stars_earned, response_time_ms) VALUES (?, ?, ?, ?, ?, ?)`,
		"id",
		activityID, participantID, string(answer), isCorrect, starsEarned, responseTimeMs,
	)
	if err != nil {
		return nil, err
	}

	return &models.Response{
		ID: id, ActivityID: activityID, ParticipantID: participantID,
		Answer: answer, IsCorrect: isCorrect, StarsEarned: starsEarned,
		ResponseTimeMs: responseTimeMs, SubmittedAt: time.Now(),
	}, nil
}

func isAnswerCorrect(correct, student []int) bool {
	if len(correct) != len(student) {
		return false
	}
	m := make(map[int]bool)
	for _, v := range correct {
		m[v] = true
	}
	for _, v := range student {
		if !m[v] {
			return false
		}
	}
	return true
}

func (d *DB) GetResponsesByActivity(activityID int64) ([]models.Response, error) {
	rows, err := d.Query(`
		SELECT r.id, r.activity_id, r.participant_id, r.answer, r.is_correct, r.stars_earned,
			r.response_time_ms, r.submitted_at, p.name as participant_name
		FROM responses r
		JOIN participants p ON p.id = r.participant_id
		WHERE r.activity_id = ? ORDER BY r.submitted_at ASC`, activityID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var responses []models.Response
	for rows.Next() {
		r := models.Response{}
		var answerBytes []byte
		var isCorrect sql.NullBool
		if err := rows.Scan(&r.ID, &r.ActivityID, &r.ParticipantID, &answerBytes, &isCorrect,
			&r.StarsEarned, &r.ResponseTimeMs, &r.SubmittedAt, &r.ParticipantName); err != nil {
			return nil, err
		}
		if answerBytes != nil {
			r.Answer = answerBytes
		}
		if isCorrect.Valid {
			b := isCorrect.Bool
			r.IsCorrect = &b
		}
		responses = append(responses, r)
	}
	return responses, nil
}

// ========== Reports ==========

func (d *DB) GetReportsByTeacher(teacherID int64) ([]models.ReportSummary, error) {
	query := fmt.Sprintf(`
		SELECT s.id, c.name, c.code, s.started_at,
			(SELECT COUNT(*) FROM activities WHERE session_id = s.id) as activities_count,
			(SELECT COUNT(DISTINCT r.participant_id) FROM responses r JOIN activities a ON a.id = r.activity_id WHERE a.session_id = s.id) as participant_count,
			COALESCE((
				SELECT SUM(p.total_stars) 
				FROM participants p
				WHERE p.id IN (
					SELECT r.participant_id 
					FROM responses r 
					JOIN activities a ON a.id = r.activity_id 
					WHERE a.session_id = s.id
				)
			), 0) as stars_awarded,
			COALESCE((
				SELECT %s
				FROM (
					SELECT p.name
					FROM participants p
					WHERE p.id IN (
						SELECT r.participant_id 
						FROM responses r 
						JOIN activities a ON a.id = r.activity_id 
						WHERE a.session_id = s.id
					)
					ORDER BY p.total_stars DESC
					LIMIT 2
				) name_list
			), '') as top_players,
			COALESCE(s.is_favorite, FALSE) as is_favorite
		FROM sessions s
		JOIN classes c ON c.id = s.class_id
		WHERE c.teacher_id = ?
		ORDER BY s.started_at DESC`, d.provider.StringAggregate("name_list.name", ", "))
	rows, err := d.Query(query, teacherID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var reports []models.ReportSummary
	for rows.Next() {
		r := models.ReportSummary{}
		if err := rows.Scan(&r.SessionID, &r.ClassName, &r.ClassCode, &r.SessionDate, &r.ActivitiesCount, &r.ParticipantCount, &r.StarsAwarded, &r.TopPlayers, &r.IsFavorite); err != nil {
			return nil, err
		}
		reports = append(reports, r)
	}
	return reports, nil
}

func (d *DB) GetReportsByClass(classID int64) ([]models.ReportSummary, error) {
	query := fmt.Sprintf(`
		SELECT s.id, c.name, c.code, s.started_at,
			(SELECT COUNT(*) FROM activities WHERE session_id = s.id) as activities_count,
			(SELECT COUNT(DISTINCT r.participant_id) FROM responses r JOIN activities a ON a.id = r.activity_id WHERE a.session_id = s.id) as participant_count,
			COALESCE((
				SELECT SUM(p.total_stars) 
				FROM participants p
				WHERE p.id IN (
					SELECT r.participant_id 
					FROM responses r 
					JOIN activities a ON a.id = r.activity_id 
					WHERE a.session_id = s.id
				)
			), 0) as stars_awarded,
			COALESCE((
				SELECT %s
				FROM (
					SELECT p.name
					FROM participants p
					WHERE p.id IN (
						SELECT r.participant_id 
						FROM responses r 
						JOIN activities a ON a.id = r.activity_id 
						WHERE a.session_id = s.id
					)
					ORDER BY p.total_stars DESC
					LIMIT 2
				) name_list
			), '') as top_players,
			COALESCE(s.is_favorite, FALSE) as is_favorite
		FROM sessions s
		JOIN classes c ON c.id = s.class_id
		WHERE c.id = ?
		ORDER BY s.started_at DESC`, d.provider.StringAggregate("name_list.name", ", "))
	rows, err := d.Query(query, classID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var reports []models.ReportSummary
	for rows.Next() {
		r := models.ReportSummary{}
		if err := rows.Scan(&r.SessionID, &r.ClassName, &r.ClassCode, &r.SessionDate, &r.ActivitiesCount, &r.ParticipantCount, &r.StarsAwarded, &r.TopPlayers, &r.IsFavorite); err != nil {
			return nil, err
		}
		reports = append(reports, r)
	}
	return reports, nil
}

func (d *DB) GetSessionParticipants(sessionID int64) ([]models.SessionParticipantResult, error) {
	rows, err := d.Query(`
		SELECT p.name, p.total_stars as score, COALESCE(p.avatar_url, '')
		FROM participants p
		JOIN responses r ON r.participant_id = p.id
		JOIN activities a ON a.id = r.activity_id
		WHERE a.session_id = ?
		GROUP BY p.id
		ORDER BY score DESC`, sessionID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var participants []models.SessionParticipantResult
	for rows.Next() {
		p := models.SessionParticipantResult{}
		if err := rows.Scan(&p.Name, &p.Score, &p.AvatarURL); err != nil {
			return nil, err
		}
		participants = append(participants, p)
	}
	return participants, nil
}

// GetQuizSessionSummary returns the official Quiz Mode review metrics for all
// students in the session's class, including students who did not submit.
func (d *DB) GetQuizSessionSummary(sessionID int64) (*models.QuizSessionSummary, error) {
	summary := &models.QuizSessionSummary{SessionID: sessionID, Rows: []models.QuizSummaryRow{}}
	if err := d.QueryRow(`
		SELECT COUNT(*)
		FROM activities
		WHERE session_id = ? AND is_quiz_mode = TRUE`, sessionID,
	).Scan(&summary.QuestionCount); err != nil {
		return nil, err
	}

	rows, err := d.Query(`
		SELECT p.id, p.name,
			COUNT(DISTINCT CASE WHEN a.id IS NOT NULL THEN r.activity_id END) AS submitted_count,
			COALESCE(SUM(CASE WHEN a.id IS NOT NULL AND r.is_correct = TRUE THEN 1 ELSE 0 END), 0) AS correct_count,
			COALESCE(SUM(CASE WHEN a.id IS NOT NULL THEN r.stars_earned ELSE 0 END), 0) AS stars_earned,
			COALESCE(AVG(CASE WHEN a.id IS NOT NULL THEN r.response_time_ms END), 0) AS average_time_ms
		FROM sessions s
		JOIN participants p ON p.class_id = s.class_id
		LEFT JOIN responses r ON r.participant_id = p.id
		LEFT JOIN activities a ON a.id = r.activity_id
			AND a.session_id = s.id
			AND a.is_quiz_mode = TRUE
		WHERE s.id = ?
		GROUP BY p.id, p.name
		ORDER BY correct_count DESC, stars_earned DESC,
			CASE WHEN average_time_ms = 0 THEN 1 ELSE 0 END,
			average_time_ms ASC, p.name ASC`, sessionID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	for rows.Next() {
		row := models.QuizSummaryRow{}
		if err := rows.Scan(&row.ParticipantID, &row.Name, &row.SubmittedCount,
			&row.CorrectCount, &row.StarsEarned, &row.AverageTimeMs); err != nil {
			return nil, err
		}
		summary.Rows = append(summary.Rows, row)
	}
	return summary, rows.Err()
}

func (d *DB) ToggleFavoriteSession(sessionID int64) (bool, error) {
	// First get current state
	var isFav bool
	err := d.QueryRow("SELECT COALESCE(is_favorite, FALSE) FROM sessions WHERE id = ?", sessionID).Scan(&isFav)
	if err != nil {
		return false, err
	}

	newState := !isFav
	_, err = d.Exec("UPDATE sessions SET is_favorite = ? WHERE id = ?", newState, sessionID)
	return newState, err
}

func (d *DB) DeleteSession(sessionID int64) error {
	_, err := d.Exec("DELETE FROM sessions WHERE id = ?", sessionID)
	return err
}

// ========== Star Levels ==========

func (d *DB) GetStarLevels(teacherID int64) ([]models.StarLevel, error) {
	rows, err := d.Query(`SELECT id, teacher_id, level, stars_required, COALESCE(badge_name,'') FROM star_levels WHERE teacher_id = ? ORDER BY level ASC`, teacherID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var levels []models.StarLevel
	for rows.Next() {
		l := models.StarLevel{}
		if err := rows.Scan(&l.ID, &l.TeacherID, &l.Level, &l.StarsRequired, &l.BadgeName); err != nil {
			return nil, err
		}
		levels = append(levels, l)
	}

	// If no levels exist, create defaults
	if len(levels) == 0 {
		levels = d.createDefaultStarLevels(teacherID)
	}
	return levels, nil
}

func (d *DB) createDefaultStarLevels(teacherID int64) []models.StarLevel {
	defaults := []struct {
		level int
		stars int
		badge string
	}{
		{1, 0, "Beginner"}, {2, 5, "Learner"}, {3, 10, "Achiever"},
		{4, 20, "Scholar"}, {5, 30, "Expert"}, {6, 40, "Master"},
		{7, 50, "Champion"}, {8, 60, "Legend"}, {9, 80, "Hero"},
		{10, 100, "Supreme"},
	}

	var levels []models.StarLevel
	for _, dl := range defaults {
		id, err := d.insertID(
			`INSERT INTO star_levels (teacher_id, level, stars_required, badge_name) VALUES (?, ?, ?, ?)`,
			"id",
			teacherID, dl.level, dl.stars, dl.badge,
		)
		if err != nil {
			continue
		}
		levels = append(levels, models.StarLevel{
			ID: id, TeacherID: teacherID, Level: dl.level,
			StarsRequired: dl.stars, BadgeName: dl.badge,
		})
	}
	return levels
}

func (d *DB) UpdateStarLevels(teacherID int64, levels []models.StarLevel) error {
	tx, err := d.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	// Delete existing
	tx.Exec(`DELETE FROM star_levels WHERE teacher_id = ?`, teacherID)

	// Insert new
	for _, l := range levels {
		_, err := tx.Exec(
			`INSERT INTO star_levels (teacher_id, level, stars_required, badge_name) VALUES (?, ?, ?, ?)`,
			teacherID, l.Level, l.StarsRequired, l.BadgeName,
		)
		if err != nil {
			return err
		}
	}
	return tx.Commit()
}

// ========== Leaderboard ==========

func (d *DB) GetLeaderboard(classID, sessionID int64) ([]models.Participant, error) {
	rows, err := d.Query(
		`SELECT p.id, p.class_id, p.name, COALESCE(p.device_id,''), COALESCE(p.avatar_url,''),
			p.total_stars,
			COALESCE(SUM(CASE
				WHEN a.session_id = ? AND a.closed_at IS NOT NULL THEN r.stars_earned
				ELSE 0
			END), 0) AS session_stars,
			COALESCE(SUM(CASE
				WHEN a.session_id = ? AND a.closed_at IS NOT NULL AND r.is_correct = TRUE
				THEN r.response_time_ms
				ELSE 0
			END), 0) AS session_response_time_ms,
			p.level, p.joined_at
		FROM participants p
		LEFT JOIN responses r ON r.participant_id = p.id
		LEFT JOIN activities a ON a.id = r.activity_id
		WHERE p.class_id = ?
		GROUP BY p.id, p.class_id, p.name, p.device_id, p.avatar_url, p.total_stars, p.level, p.joined_at
		ORDER BY p.total_stars DESC, p.name ASC`, sessionID, sessionID, classID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var participants []models.Participant
	for rows.Next() {
		p := models.Participant{}
		if err := rows.Scan(&p.ID, &p.ClassID, &p.Name, &p.DeviceID, &p.AvatarURL,
			&p.TotalStars, &p.SessionStars, &p.SessionResponseTimeMs,
			&p.Level, &p.JoinedAt); err != nil {
			return nil, err
		}
		participants = append(participants, p)
	}
	return participants, nil
}
