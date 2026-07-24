package runtimepaths

import (
	"os"
	"path/filepath"
	"testing"
)

func TestResolveUsesConfiguredDataDirectory(t *testing.T) {
	root := t.TempDir()
	t.Setenv("LOKAL_DATA_DIR", root)
	t.Setenv("LOKAL_DB_PATH", "")
	t.Setenv("LOKAL_UPLOADS_PATH", "")
	t.Setenv("LOKAL_LOG_PATH", "")
	t.Setenv("LOKAL_MIGRATE_LEGACY", "")

	paths, err := Resolve()
	if err != nil {
		t.Fatal(err)
	}

	wantRoot, _ := filepath.Abs(root)
	assertPathEqual(t, paths.Root, wantRoot)
	assertPathEqual(t, paths.Database, filepath.Join(wantRoot, "data", "lokal.db"))
	assertPathEqual(t, paths.UploadsDir, filepath.Join(wantRoot, "uploads"))
	assertPathEqual(t, paths.ServerConfig, filepath.Join(wantRoot, "config", "server.json"))
	assertPathEqual(t, paths.LogFile, filepath.Join(wantRoot, "logs", "lokal.log"))
	if paths.allowLegacyMigration {
		t.Fatal("an explicit data directory must not automatically import legacy data")
	}
}

func TestEnsureCreatesRuntimeLayout(t *testing.T) {
	root := t.TempDir()
	paths := Paths{
		Root:         root,
		Database:     filepath.Join(root, "data", "lokal.db"),
		UploadsDir:   filepath.Join(root, "uploads"),
		LogsDir:      filepath.Join(root, "logs"),
		LogFile:      filepath.Join(root, "logs", "lokal.log"),
		ConfigDir:    filepath.Join(root, "config"),
		ServerConfig: filepath.Join(root, "config", "server.json"),
	}
	if err := paths.Ensure(); err != nil {
		t.Fatal(err)
	}

	for _, directory := range []string{
		filepath.Join(root, "data"),
		filepath.Join(root, "uploads", "slides"),
		filepath.Join(root, "uploads", "avatars"),
		filepath.Join(root, "config"),
		filepath.Join(root, "logs"),
	} {
		info, err := os.Stat(directory)
		if err != nil {
			t.Fatalf("expected %q to exist: %v", directory, err)
		}
		if !info.IsDir() {
			t.Fatalf("expected %q to be a directory", directory)
		}
	}
}

func TestMigrateLegacyIsIdempotentAndPreservesDestinations(t *testing.T) {
	legacy := t.TempDir()
	destination := t.TempDir()

	writeTestFile(t, filepath.Join(legacy, "lokal.db"), "legacy-db")
	writeTestFile(t, filepath.Join(legacy, "lokal.db-wal"), "legacy-wal")
	writeTestFile(t, filepath.Join(legacy, "uploads", "slides", "one.png"), "slide")
	writeTestFile(t, filepath.Join(legacy, "uploads", "avatars", "existing.png"), "legacy-avatar")
	writeTestFile(t, filepath.Join(legacy, "slide_error.txt"), "old diagnostic")

	paths := Paths{
		Root:                 destination,
		Database:             filepath.Join(destination, "data", "lokal.db"),
		UploadsDir:           filepath.Join(destination, "uploads"),
		ConfigDir:            filepath.Join(destination, "config"),
		ServerConfig:         filepath.Join(destination, "config", "server.json"),
		LogsDir:              filepath.Join(destination, "logs"),
		LogFile:              filepath.Join(destination, "logs", "lokal.log"),
		allowLegacyMigration: true,
	}
	if err := paths.Ensure(); err != nil {
		t.Fatal(err)
	}
	writeTestFile(t, filepath.Join(paths.UploadsDir, "avatars", "existing.png"), "new-avatar")

	migrated, err := paths.MigrateLegacy(legacy)
	if err != nil {
		t.Fatal(err)
	}
	if len(migrated) != 3 {
		t.Fatalf("expected database, uploads, and diagnostic migrations; got %v", migrated)
	}
	assertTestFile(t, paths.Database, "legacy-db")
	assertTestFile(t, paths.Database+"-wal", "legacy-wal")
	assertTestFile(t, filepath.Join(paths.UploadsDir, "slides", "one.png"), "slide")
	assertTestFile(t, filepath.Join(paths.UploadsDir, "avatars", "existing.png"), "new-avatar")
	assertTestFile(t, filepath.Join(paths.LogsDir, "slide-error.log"), "old diagnostic")

	again, err := paths.MigrateLegacy(legacy)
	if err != nil {
		t.Fatal(err)
	}
	if len(again) != 0 {
		t.Fatalf("migration should be idempotent; got %v", again)
	}
}

func writeTestFile(t *testing.T, path, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(content), 0644); err != nil {
		t.Fatal(err)
	}
}

func assertTestFile(t *testing.T, path, want string) {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read %q: %v", path, err)
	}
	if string(content) != want {
		t.Fatalf("%q contained %q, want %q", path, content, want)
	}
}

func assertPathEqual(t *testing.T, got, want string) {
	t.Helper()
	if !samePath(got, want) {
		t.Fatalf("path %q does not match %q", got, want)
	}
}
