package application

import (
	"os"
	"path/filepath"
	"testing"
)

func TestResourceRootOverride(t *testing.T) {
	root := t.TempDir()
	t.Setenv("LOKAL_RESOURCE_DIR", root)
	want, err := filepath.Abs(root)
	if err != nil {
		t.Fatal(err)
	}
	if got := ResourceRoot(); got != want {
		t.Fatalf("ResourceRoot() = %q, want %q", got, want)
	}
}

func TestResourceRootDevelopmentFallback(t *testing.T) {
	t.Setenv("LOKAL_RESOURCE_DIR", "")
	root := t.TempDir()
	if err := os.Mkdir(filepath.Join(root, "web"), 0o755); err != nil {
		t.Fatal(err)
	}
	old, err := os.Getwd()
	if err != nil {
		t.Fatal(err)
	}
	if err := os.Chdir(root); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = os.Chdir(old) })
	if got := ResourceRoot(); got != root {
		t.Fatalf("ResourceRoot() = %q, want %q", got, root)
	}
}
