package runtimepaths

import (
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
)

const applicationDirectory = "LOKAL"

// Paths contains every directory that LOKAL writes to at runtime. Application
// binaries and bundled web/assets remain in the installation directory; user
// data belongs under ProgramData so upgrades do not overwrite it.
type Paths struct {
	Root         string
	Database     string
	UploadsDir   string
	ConfigDir    string
	ServerConfig string
	AuthSecret   string
	LogsDir      string
	LogFile      string

	allowLegacyMigration bool
}

// Resolve returns the writable paths for the current installation.
//
// Environment overrides are intentionally supported for development, tests,
// portable diagnostics, and managed deployments:
//   - LOKAL_DATA_DIR
//   - LOKAL_DB_PATH
//   - LOKAL_UPLOADS_PATH
//   - LOKAL_LOG_PATH
//   - LOKAL_SERVER_CONFIG
func Resolve() (Paths, error) {
	rootOverride := strings.TrimSpace(os.Getenv("LOKAL_DATA_DIR"))
	root := rootOverride
	if root == "" {
		var err error
		root, err = defaultRoot()
		if err != nil {
			return Paths{}, err
		}
	}

	root, err := filepath.Abs(root)
	if err != nil {
		return Paths{}, fmt.Errorf("resolve data directory: %w", err)
	}

	p := Paths{
		Root:         root,
		Database:     filepath.Join(root, "data", "lokal.db"),
		UploadsDir:   filepath.Join(root, "uploads"),
		ConfigDir:    filepath.Join(root, "config"),
		ServerConfig: filepath.Join(root, "config", "server.json"),
		AuthSecret:   filepath.Join(root, "config", "auth.key"),
		LogsDir:      filepath.Join(root, "logs"),
		LogFile:      filepath.Join(root, "logs", "lokal.log"),
		// Automatic migration is for the default installed layout. Explicit
		// overrides must remain isolated unless migration is explicitly enabled.
		allowLegacyMigration: rootOverride == "",
	}

	if value := strings.TrimSpace(os.Getenv("LOKAL_DB_PATH")); value != "" {
		p.Database = value
		p.allowLegacyMigration = false
	}
	if value := strings.TrimSpace(os.Getenv("LOKAL_UPLOADS_PATH")); value != "" {
		p.UploadsDir = value
		p.allowLegacyMigration = false
	}
	if value := strings.TrimSpace(os.Getenv("LOKAL_LOG_PATH")); value != "" {
		p.LogFile = value
		p.LogsDir = filepath.Dir(value)
	}
	if value := strings.TrimSpace(os.Getenv("LOKAL_SERVER_CONFIG")); value != "" {
		p.ServerConfig = value
		p.ConfigDir = filepath.Dir(value)
	}
	if strings.EqualFold(strings.TrimSpace(os.Getenv("LOKAL_MIGRATE_LEGACY")), "1") ||
		strings.EqualFold(strings.TrimSpace(os.Getenv("LOKAL_MIGRATE_LEGACY")), "true") {
		p.allowLegacyMigration = true
	}

	p.Database, err = filepath.Abs(p.Database)
	if err != nil {
		return Paths{}, fmt.Errorf("resolve database path: %w", err)
	}
	p.UploadsDir, err = filepath.Abs(p.UploadsDir)
	if err != nil {
		return Paths{}, fmt.Errorf("resolve uploads path: %w", err)
	}
	p.LogFile, err = filepath.Abs(p.LogFile)
	if err != nil {
		return Paths{}, fmt.Errorf("resolve log path: %w", err)
	}
	p.LogsDir = filepath.Dir(p.LogFile)
	p.ServerConfig, err = filepath.Abs(p.ServerConfig)
	if err != nil {
		return Paths{}, fmt.Errorf("resolve server configuration path: %w", err)
	}
	p.ConfigDir = filepath.Dir(p.ServerConfig)
	p.AuthSecret = filepath.Join(p.ConfigDir, "auth.key")
	return p, nil
}

func defaultRoot() (string, error) {
	if programData := strings.TrimSpace(os.Getenv("PROGRAMDATA")); programData != "" {
		return filepath.Join(programData, applicationDirectory), nil
	}
	if runtime.GOOS == "windows" {
		systemDrive := strings.TrimSpace(os.Getenv("SystemDrive"))
		if systemDrive == "" {
			systemDrive = "C:"
		}
		return filepath.Join(systemDrive+string(os.PathSeparator), "ProgramData", applicationDirectory), nil
	}

	configDir, err := os.UserConfigDir()
	if err != nil {
		return "", fmt.Errorf("locate application data directory: %w", err)
	}
	return filepath.Join(configDir, applicationDirectory), nil
}

// Ensure creates the writable runtime directory tree.
func (p Paths) Ensure() error {
	dirs := []string{
		p.Root,
		filepath.Dir(p.Database),
		p.UploadsDir,
		filepath.Join(p.UploadsDir, "slides"),
		filepath.Join(p.UploadsDir, "avatars"),
		p.ConfigDir,
		p.LogsDir,
	}
	for _, dir := range dirs {
		if err := os.MkdirAll(dir, 0755); err != nil {
			return fmt.Errorf("create runtime directory %q: %w", dir, err)
		}
	}
	return nil
}

// OpenLog opens the application log for append. The caller owns the file.
func (p Paths) OpenLog() (*os.File, error) {
	if err := os.MkdirAll(filepath.Dir(p.LogFile), 0755); err != nil {
		return nil, fmt.Errorf("create log directory: %w", err)
	}
	file, err := os.OpenFile(p.LogFile, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0644)
	if err != nil {
		return nil, fmt.Errorf("open application log: %w", err)
	}
	return file, nil
}

// MigrateLegacy copies data from old working-directory/executable-directory
// layouts into ProgramData. Existing destination files always win, making this
// operation safe and idempotent.
func (p Paths) MigrateLegacy(legacyRoots ...string) ([]string, error) {
	return p.MigrateLegacyForProvider(true, legacyRoots...)
}

// MigrateLegacyForProvider migrates shared runtime assets and optionally the
// legacy SQLite file. Hosted database providers must pass false so startup does
// not perform SQLite-specific storage work that cannot affect their database.
func (p Paths) MigrateLegacyForProvider(includeSQLiteDatabase bool, legacyRoots ...string) ([]string, error) {
	if !p.allowLegacyMigration {
		return nil, nil
	}

	var migrated []string
	var migrationErrors []error
	seen := make(map[string]bool)
	for _, root := range legacyRoots {
		if strings.TrimSpace(root) == "" {
			continue
		}
		absolute, err := filepath.Abs(root)
		if err != nil {
			migrationErrors = append(migrationErrors, err)
			continue
		}
		key := strings.ToLower(filepath.Clean(absolute))
		if seen[key] || samePath(absolute, p.Root) {
			continue
		}
		seen[key] = true

		if includeSQLiteDatabase {
			sourceDB := filepath.Join(absolute, "lokal.db")
			if _, err := os.Stat(p.Database); errors.Is(err, os.ErrNotExist) {
				if copied, copyErr := copySQLiteDatabase(sourceDB, p.Database); copyErr != nil {
					migrationErrors = append(migrationErrors, copyErr)
				} else if copied {
					migrated = append(migrated, sourceDB+" -> "+p.Database)
				}
			}
		}

		sourceUploads := filepath.Join(absolute, "uploads")
		if copied, copyErr := mergeDirectory(sourceUploads, p.UploadsDir); copyErr != nil {
			migrationErrors = append(migrationErrors, copyErr)
		} else if copied {
			migrated = append(migrated, sourceUploads+" -> "+p.UploadsDir)
		}

		sourceDiagnostic := filepath.Join(absolute, "slide_error.txt")
		destinationDiagnostic := filepath.Join(p.LogsDir, "slide-error.log")
		if copied, copyErr := copyFileIfMissing(sourceDiagnostic, destinationDiagnostic); copyErr != nil {
			migrationErrors = append(migrationErrors, copyErr)
		} else if copied {
			migrated = append(migrated, sourceDiagnostic+" -> "+destinationDiagnostic)
		}
	}
	return migrated, errors.Join(migrationErrors...)
}

func copySQLiteDatabase(source, destination string) (bool, error) {
	copied, err := copyFileIfMissing(source, destination)
	if err != nil || !copied {
		return copied, err
	}
	// A clean shutdown normally removes these files. Copying them when present
	// preserves committed WAL transactions from the legacy installation.
	for _, suffix := range []string{"-wal", "-shm"} {
		if _, sidecarErr := copyFileIfMissing(source+suffix, destination+suffix); sidecarErr != nil {
			return true, sidecarErr
		}
	}
	return true, nil
}

func mergeDirectory(source, destination string) (bool, error) {
	info, err := os.Stat(source)
	if errors.Is(err, os.ErrNotExist) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	if !info.IsDir() {
		return false, fmt.Errorf("legacy upload path %q is not a directory", source)
	}

	copiedAny := false
	err = filepath.WalkDir(source, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		relative, err := filepath.Rel(source, path)
		if err != nil {
			return err
		}
		target := filepath.Join(destination, relative)
		if entry.IsDir() {
			return os.MkdirAll(target, 0755)
		}
		copied, err := copyFileIfMissing(path, target)
		copiedAny = copiedAny || copied
		return err
	})
	return copiedAny, err
}

func copyFileIfMissing(source, destination string) (bool, error) {
	sourceInfo, err := os.Stat(source)
	if errors.Is(err, os.ErrNotExist) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	if sourceInfo.IsDir() {
		return false, fmt.Errorf("source %q is a directory", source)
	}
	if _, err := os.Stat(destination); err == nil {
		return false, nil
	} else if !errors.Is(err, os.ErrNotExist) {
		return false, err
	}

	if err := os.MkdirAll(filepath.Dir(destination), 0755); err != nil {
		return false, err
	}
	input, err := os.Open(source)
	if err != nil {
		return false, err
	}
	defer input.Close()

	output, err := os.OpenFile(destination, os.O_CREATE|os.O_EXCL|os.O_WRONLY, sourceInfo.Mode().Perm())
	if err != nil {
		if errors.Is(err, os.ErrExist) {
			return false, nil
		}
		return false, err
	}
	removeIncomplete := true
	defer func() {
		output.Close()
		if removeIncomplete {
			_ = os.Remove(destination)
		}
	}()
	if _, err := io.Copy(output, input); err != nil {
		return false, err
	}
	if err := output.Sync(); err != nil {
		return false, err
	}
	if err := output.Close(); err != nil {
		return false, err
	}
	removeIncomplete = false
	return true, nil
}

func samePath(left, right string) bool {
	leftAbs, leftErr := filepath.Abs(left)
	rightAbs, rightErr := filepath.Abs(right)
	if leftErr != nil || rightErr != nil {
		return false
	}
	return strings.EqualFold(filepath.Clean(leftAbs), filepath.Clean(rightAbs))
}
