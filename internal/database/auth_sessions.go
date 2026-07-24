package database

import (
	"database/sql"
	"errors"
	"strings"
	"time"

	"lokal-thesis/internal/models"
)

var ErrParticipantNameInUse = errors.New("participant name is already in use")

func (d *DB) RegisterDevice(reg models.DeviceRegistration) (*models.Device, error) {
	uid := strings.TrimSpace(reg.DeviceUID)
	if uid == "" {
		return nil, errors.New("device id is required")
	}
	now := time.Now().UTC()
	var device models.Device
	err := d.QueryRow(`SELECT id, device_uid, name, platform, COALESCE(user_agent, ''),
		created_at, last_seen_at, revoked_at FROM devices WHERE device_uid = ?`, uid).
		Scan(&device.ID, &device.DeviceUID, &device.Name, &device.Platform, &device.UserAgent,
			&device.CreatedAt, &device.LastSeenAt, &device.RevokedAt)
	if err == nil {
		name := strings.TrimSpace(reg.Name)
		if name == "" {
			name = device.Name
		}
		platform := strings.TrimSpace(reg.Platform)
		if platform == "" {
			platform = device.Platform
		}
		userAgent := strings.TrimSpace(reg.UserAgent)
		if userAgent == "" {
			userAgent = device.UserAgent
		}
		_, err = d.Exec(`UPDATE devices SET name = ?, platform = ?, user_agent = ?,
			last_seen_at = ?, revoked_at = NULL WHERE id = ?`,
			name, platform, userAgent, now, device.ID)
		if err != nil {
			return nil, err
		}
		device.Name, device.Platform, device.UserAgent = name, platform, userAgent
		device.LastSeenAt, device.RevokedAt = now, nil
		device.Active = true
		return &device, nil
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return nil, err
	}
	name := strings.TrimSpace(reg.Name)
	if name == "" {
		name = "LOKAL device"
	}
	platform := strings.TrimSpace(reg.Platform)
	if platform == "" {
		platform = "unknown"
	}
	id, err := d.insertID(`INSERT INTO devices (device_uid, name, platform, user_agent, created_at, last_seen_at)
		VALUES (?, ?, ?, ?, ?, ?)`, "id", uid, name, platform, reg.UserAgent, now, now)
	if err != nil {
		return nil, err
	}
	return &models.Device{ID: id, DeviceUID: uid, Name: name, Platform: platform,
		UserAgent: reg.UserAgent, CreatedAt: now, LastSeenAt: now, Active: true}, nil
}

func (d *DB) CreateTeacherAuthSession(teacherID, deviceID int64, tokenHash string, expiresAt time.Time) error {
	now := time.Now().UTC()
	if _, err := d.Exec(`UPDATE teacher_auth_sessions SET revoked_at = ?
		WHERE teacher_id = ? AND device_id = ? AND revoked_at IS NULL`, now, teacherID, deviceID); err != nil {
		return err
	}
	_, err := d.Exec(`INSERT INTO teacher_auth_sessions
		(teacher_id, device_id, token_hash, created_at, last_seen_at, expires_at)
		VALUES (?, ?, ?, ?, ?, ?)`, teacherID, deviceID, tokenHash, now, now, expiresAt.UTC())
	return err
}

func (d *DB) AuthenticateTeacherSession(tokenHash string) (*models.Teacher, error) {
	now := time.Now().UTC()
	var teacher models.Teacher
	var expiresAt time.Time
	var deviceID int64
	err := d.QueryRow(`SELECT t.id, t.username, t.email, t.password_hash,
		COALESCE(t.display_name, ''), COALESCE(t.avatar_url, ''), t.created_at,
		s.expires_at, s.device_id
		FROM teacher_auth_sessions s
		JOIN teachers t ON t.id = s.teacher_id
		JOIN devices d ON d.id = s.device_id
		WHERE s.token_hash = ? AND s.revoked_at IS NULL AND d.revoked_at IS NULL`,
		tokenHash).Scan(&teacher.ID, &teacher.Username, &teacher.Email, &teacher.PasswordHash,
		&teacher.DisplayName, &teacher.AvatarURL, &teacher.CreatedAt, &expiresAt, &deviceID)
	if err != nil {
		return nil, err
	}
	if !expiresAt.After(now) {
		return nil, errors.New("session expired")
	}
	_, _ = d.Exec(`UPDATE teacher_auth_sessions SET last_seen_at = ? WHERE token_hash = ?`, now, tokenHash)
	_, _ = d.Exec(`UPDATE devices SET last_seen_at = ? WHERE id = ?`, now, deviceID)
	return &teacher, nil
}

func (d *DB) RevokeTeacherSession(tokenHash string) error {
	_, err := d.Exec(`UPDATE teacher_auth_sessions SET revoked_at = ?
		WHERE token_hash = ? AND revoked_at IS NULL`, time.Now().UTC(), tokenHash)
	return err
}

func (d *DB) GetTeacherDevices(teacherID int64) ([]models.Device, error) {
	rows, err := d.Query(`SELECT DISTINCT d.id, d.device_uid, d.name, d.platform,
		COALESCE(d.user_agent, ''), d.created_at, d.last_seen_at, d.revoked_at,
		EXISTS (
			SELECT 1 FROM teacher_auth_sessions active
			WHERE active.teacher_id = ? AND active.device_id = d.id
			  AND active.revoked_at IS NULL AND active.expires_at > ?
		)
		FROM devices d JOIN teacher_auth_sessions s ON s.device_id = d.id
		WHERE s.teacher_id = ? ORDER BY d.last_seen_at DESC`,
		teacherID, time.Now().UTC(), teacherID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var result []models.Device
	for rows.Next() {
		var item models.Device
		if err := rows.Scan(&item.ID, &item.DeviceUID, &item.Name, &item.Platform,
			&item.UserAgent, &item.CreatedAt, &item.LastSeenAt, &item.RevokedAt, &item.Active); err != nil {
			return nil, err
		}
		result = append(result, item)
	}
	return result, rows.Err()
}

func (d *DB) RevokeTeacherDevice(teacherID, deviceID int64) error {
	result, err := d.Exec(`UPDATE teacher_auth_sessions SET revoked_at = ?
		WHERE teacher_id = ? AND device_id = ? AND revoked_at IS NULL`,
		time.Now().UTC(), teacherID, deviceID)
	if err != nil {
		return err
	}
	affected, _ := result.RowsAffected()
	if affected == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (d *DB) CreateStudentAuthSession(participantID, deviceID int64, tokenHash string, expiresAt time.Time) error {
	now := time.Now().UTC()
	if _, err := d.Exec(`UPDATE student_auth_sessions SET revoked_at = ?
		WHERE participant_id = ? AND device_id = ? AND revoked_at IS NULL`, now, participantID, deviceID); err != nil {
		return err
	}
	_, err := d.Exec(`INSERT INTO student_auth_sessions
		(participant_id, device_id, token_hash, created_at, last_seen_at, expires_at)
		VALUES (?, ?, ?, ?, ?, ?)`, participantID, deviceID, tokenHash, now, now, expiresAt.UTC())
	return err
}

func (d *DB) AuthenticateStudentSession(tokenHash string) (*models.Participant, error) {
	now := time.Now().UTC()
	var participant models.Participant
	var expiresAt time.Time
	var deviceID int64
	err := d.QueryRow(`SELECT p.id, p.class_id, p.name, COALESCE(p.device_id, ''),
		COALESCE(p.avatar_url, ''), p.total_stars, p.level, p.joined_at,
		s.expires_at, s.device_id
		FROM student_auth_sessions s
		JOIN participants p ON p.id = s.participant_id
		JOIN devices d ON d.id = s.device_id
		WHERE s.token_hash = ? AND s.revoked_at IS NULL AND d.revoked_at IS NULL`,
		tokenHash).Scan(&participant.ID, &participant.ClassID, &participant.Name,
		&participant.DeviceID, &participant.AvatarURL, &participant.TotalStars,
		&participant.Level, &participant.JoinedAt, &expiresAt, &deviceID)
	if err != nil {
		return nil, err
	}
	if !expiresAt.After(now) {
		return nil, errors.New("session expired")
	}
	_, _ = d.Exec(`UPDATE student_auth_sessions SET last_seen_at = ? WHERE token_hash = ?`, now, tokenHash)
	_, _ = d.Exec(`UPDATE devices SET last_seen_at = ? WHERE id = ?`, now, deviceID)
	return &participant, nil
}

// RegisterJoiningParticipant reconnects the same device, claims a manually
// created participant, or inserts a new participant without allowing a second
// device to silently take over an existing name.
func (d *DB) RegisterJoiningParticipant(classID int64, name, deviceUID, avatarURL string) (*models.Participant, error) {
	if deviceUID != "" {
		var id int64
		err := d.QueryRow(`SELECT id FROM participants WHERE class_id = ? AND device_id = ?
			ORDER BY id DESC LIMIT 1`, classID, deviceUID).Scan(&id)
		if err == nil {
			_, err = d.Exec(`UPDATE participants SET name = ?, avatar_url = ? WHERE id = ?`, name, avatarURL, id)
			if err != nil {
				return nil, err
			}
			return d.GetParticipantByID(id)
		}
		if !errors.Is(err, sql.ErrNoRows) {
			return nil, err
		}
	}
	var existingID int64
	var existingDevice sql.NullString
	err := d.QueryRow(`SELECT id, device_id FROM participants
		WHERE class_id = ? AND LOWER(name) = LOWER(?) ORDER BY id LIMIT 1`, classID, name).
		Scan(&existingID, &existingDevice)
	if err == nil {
		if existingDevice.Valid && strings.TrimSpace(existingDevice.String) != "" && existingDevice.String != deviceUID {
			return nil, ErrParticipantNameInUse
		}
		_, err = d.Exec(`UPDATE participants SET device_id = ?, avatar_url = ? WHERE id = ?`,
			deviceUID, avatarURL, existingID)
		if err != nil {
			return nil, err
		}
		return d.GetParticipantByID(existingID)
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return nil, err
	}
	return d.AddParticipant(classID, name, deviceUID, avatarURL)
}
