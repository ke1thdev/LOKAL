package database

import (
	"path/filepath"
	"slices"
	"strings"
	"testing"
)

func TestSQLiteProviderIsRegisteredAndOpens(t *testing.T) {
	if !slices.Contains(AvailableProviders(), DefaultProvider) {
		t.Fatalf("available providers %v do not include %q", AvailableProviders(), DefaultProvider)
	}

	db, err := Open(Config{Provider: "SQLITE", Path: filepath.Join(t.TempDir(), "lokal.db")})
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	if got := db.ProviderName(); got != DefaultProvider {
		t.Fatalf("ProviderName() = %q, want %q", got, DefaultProvider)
	}
}

func TestConfigFromEnvironment(t *testing.T) {
	t.Setenv("LOKAL_DB_PROVIDER", " Hosted-Test ")
	t.Setenv("LOKAL_DB_DSN", "secret-dsn")
	config := ConfigFromEnvironment("fallback.db")
	if config.Provider != "hosted-test" || config.DSN != "secret-dsn" || config.Path != "fallback.db" {
		t.Fatalf("unexpected environment config: %+v", config)
	}
}

func TestOpenRejectsUnregisteredProvider(t *testing.T) {
	_, err := Open(Config{Provider: "not-installed"})
	if err == nil || !strings.Contains(err.Error(), "not registered") {
		t.Fatalf("Open() error = %v, want unregistered-provider error", err)
	}
}

func TestSQLiteDSNPreservesExistingQuery(t *testing.T) {
	provider, err := lookupProvider(DefaultProvider)
	if err != nil {
		t.Fatal(err)
	}
	dsn, err := provider.DataSourceName(Config{Path: filepath.Join(t.TempDir(), "lokal.db") + "?cache=shared"})
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(dsn, "?") != 1 || !strings.Contains(dsn, "&amp;") && !strings.Contains(dsn, "&_pragma=") {
		t.Fatalf("malformed SQLite DSN: %q", dsn)
	}
}
