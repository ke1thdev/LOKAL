package database

import (
	"fmt"
	"os"
	"slices"
	"strings"
	"testing"
	"time"
)

func TestPostgresProviderRegistrationAndAlias(t *testing.T) {
	if !slices.Contains(AvailableProviders(), "postgres") {
		t.Fatalf("available providers %v do not include postgres", AvailableProviders())
	}
	provider, err := lookupProvider("PostgreSQL")
	if err != nil {
		t.Fatal(err)
	}
	if provider.Name() != "postgres" || provider.DriverName() != "pgx" {
		t.Fatalf("unexpected provider: %s/%s", provider.Name(), provider.DriverName())
	}
}

func TestPostgresRequiresDSN(t *testing.T) {
	provider, err := lookupProvider("postgres")
	if err != nil {
		t.Fatal(err)
	}
	if _, err := provider.DataSourceName(Config{}); err == nil || !strings.Contains(err.Error(), "LOKAL_DB_DSN") {
		t.Fatalf("DataSourceName() error = %v, want missing DSN error", err)
	}
}

func TestPostgresRebind(t *testing.T) {
	provider := postgresProvider{}
	query := "SELECT ?, '?', \"?\", $$?$$, $body$?$body$ -- ?\n/* ? */ WHERE a = ? AND b = ?"
	want := "SELECT $1, '?', \"?\", $$?$$, $body$?$body$ -- ?\n/* ? */ WHERE a = $2 AND b = $3"
	if got := provider.Rebind(query); got != want {
		t.Fatalf("Rebind()\n got: %s\nwant: %s", got, want)
	}
}

func TestPostgresStringAggregate(t *testing.T) {
	got := (postgresProvider{}).StringAggregate("p.name", ", '")
	if got != "STRING_AGG(p.name, ', ''')" {
		t.Fatalf("StringAggregate() = %q", got)
	}
}

func TestPostgresCloudDefaultsFromEnvironment(t *testing.T) {
	t.Setenv("LOKAL_DB_PROVIDER", "postgresql")
	t.Setenv("LOKAL_DB_DSN", "postgres://example.invalid/lokal")
	t.Setenv("LOKAL_DB_CONNECT_TIMEOUT", "3s")
	t.Setenv("LOKAL_DB_MAX_OPEN_CONNS", "12")
	t.Setenv("LOKAL_DB_MAX_IDLE_CONNS", "4")
	t.Setenv("LOKAL_DB_CONN_MAX_LIFETIME", "15m")
	config := ConfigFromEnvironment("unused.db")
	if config.ConnectTimeout != 3*time.Second || config.MaxOpenConns != 12 || config.MaxIdleConns != 4 || config.ConnMaxLifetime != 15*time.Minute {
		t.Fatalf("unexpected PostgreSQL pool config: %+v", config)
	}
}

// This integration test is intentionally opt-in because it creates the LOKAL
// schema. LOKAL_TEST_POSTGRES_DSN must identify a disposable test database.
func TestPostgresIntegration(t *testing.T) {
	dsn := strings.TrimSpace(os.Getenv("LOKAL_TEST_POSTGRES_DSN"))
	if dsn == "" {
		t.Skip("set LOKAL_TEST_POSTGRES_DSN to a disposable PostgreSQL database")
	}
	db, err := Open(Config{Provider: "postgres", DSN: dsn, ConnectTimeout: 15 * time.Second})
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()

	suffix := time.Now().UnixNano()
	teacher, err := db.CreateTeacher(
		fmt.Sprintf("postgres_test_%d", suffix),
		fmt.Sprintf("postgres_test_%d@example.invalid", suffix),
		"test-only-hash",
		"PostgreSQL Integration Test",
	)
	if err != nil {
		t.Fatal(err)
	}
	defer db.Exec("DELETE FROM teachers WHERE id = ?", teacher.ID)

	class, err := db.CreateClass(teacher.ID, "Cloud test class", fmt.Sprintf("PG%d", suffix), "#0B1F1C")
	if err != nil {
		t.Fatal(err)
	}
	if err := db.SetClassLocked(class.ID, true); err != nil {
		t.Fatal(err)
	}
	loaded, err := db.GetClassByID(class.ID)
	if err != nil {
		t.Fatal(err)
	}
	if !loaded.IsLocked {
		t.Fatal("PostgreSQL boolean round trip did not preserve locked=true")
	}
}
