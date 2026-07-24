package database

import (
	"fmt"
	"sort"
	"strings"
)

// RelayNodeID returns the durable identity assigned to this local database.
// SQLite creates sync_node during migration, so reinstalling or restarting the
// service does not make the hosted relay treat the same classroom as a new
// server.
func (d *DB) RelayNodeID() (string, error) {
	if d.ProviderName() != DefaultProvider {
		return "", fmt.Errorf("a relay node id must be configured for %s", d.ProviderName())
	}
	var id string
	if err := d.QueryRow(`SELECT node_uid FROM sync_node WHERE id = 1`).Scan(&id); err != nil {
		return "", fmt.Errorf("read relay node identity: %w", err)
	}
	id = strings.TrimSpace(id)
	if id == "" {
		return "", fmt.Errorf("relay node identity is empty")
	}
	return id, nil
}

// RelayRooms returns every classroom channel owned by this database. Keeping
// registration based on stored classes (rather than current browser presence)
// lets remote learners reconnect before the PowerPoint window is reopened.
func (d *DB) RelayRooms() ([]string, error) {
	rows, err := d.Query(`SELECT code FROM classes WHERE code IS NOT NULL AND TRIM(code) <> ''`)
	if err != nil {
		return nil, fmt.Errorf("list relay classrooms: %w", err)
	}
	defer rows.Close()

	rooms := make([]string, 0)
	seen := make(map[string]struct{})
	for rows.Next() {
		var code string
		if err := rows.Scan(&code); err != nil {
			return nil, err
		}
		room := "class:" + strings.TrimSpace(code)
		if room == "class:" {
			continue
		}
		if _, exists := seen[room]; exists {
			continue
		}
		seen[room] = struct{}{}
		rooms = append(rooms, room)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	sort.Strings(rooms)
	return rooms, nil
}
