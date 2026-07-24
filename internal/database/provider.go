package database

import (
	"database/sql"
	"fmt"
	"os"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

const DefaultProvider = "sqlite"

// Config describes a database connection without coupling the application to a
// particular SQL driver. Path is used by file-backed providers; DSN is intended
// for server-hosted providers and takes precedence when supplied.
type Config struct {
	Provider        string
	Path            string
	DSN             string
	ConnectTimeout  time.Duration
	MaxOpenConns    int
	MaxIdleConns    int
	ConnMaxLifetime time.Duration
}

// Provider owns every database-engine-specific operation. Adding a hosted SQL
// engine therefore does not require changes to the repository methods.
type Provider interface {
	Name() string
	DriverName() string
	DataSourceName(Config) (string, error)
	Rebind(string) string
	Migrate(*sql.DB) error
	InsertID(*sql.DB, string, string, ...any) (int64, error)
	StringAggregate(string, string) string
}

var providerRegistry = struct {
	sync.RWMutex
	items map[string]Provider
}{items: make(map[string]Provider)}

// RegisterProvider makes an SQL engine available to Open. Provider packages
// normally call this from init.
func RegisterProvider(provider Provider) {
	if provider == nil || strings.TrimSpace(provider.Name()) == "" {
		panic("database: cannot register an unnamed provider")
	}
	name := strings.ToLower(strings.TrimSpace(provider.Name()))
	providerRegistry.Lock()
	defer providerRegistry.Unlock()
	if _, exists := providerRegistry.items[name]; exists {
		panic("database: provider already registered: " + name)
	}
	providerRegistry.items[name] = provider
}

func AvailableProviders() []string {
	providerRegistry.RLock()
	defer providerRegistry.RUnlock()
	names := make([]string, 0, len(providerRegistry.items))
	for name := range providerRegistry.items {
		names = append(names, name)
	}
	sort.Strings(names)
	return names
}

// ConfigFromEnvironment keeps SQLite as the offline default while providing a
// stable configuration seam for a future hosted provider. DSNs are deliberately
// never logged because they may contain credentials.
func ConfigFromEnvironment(defaultPath string) Config {
	provider := strings.ToLower(strings.TrimSpace(os.Getenv("LOKAL_DB_PROVIDER")))
	if provider == "" {
		provider = DefaultProvider
	}
	config := Config{
		Provider: provider,
		Path:     defaultPath,
		DSN:      strings.TrimSpace(os.Getenv("LOKAL_DB_DSN")),
	}
	if provider == "postgres" || provider == "postgresql" {
		config.ConnectTimeout = 10 * time.Second
		config.MaxOpenConns = 20
		config.MaxIdleConns = 5
		config.ConnMaxLifetime = 30 * time.Minute
	}
	config.ConnectTimeout = environmentDuration("LOKAL_DB_CONNECT_TIMEOUT", config.ConnectTimeout)
	config.ConnMaxLifetime = environmentDuration("LOKAL_DB_CONN_MAX_LIFETIME", config.ConnMaxLifetime)
	config.MaxOpenConns = environmentInt("LOKAL_DB_MAX_OPEN_CONNS", config.MaxOpenConns)
	config.MaxIdleConns = environmentInt("LOKAL_DB_MAX_IDLE_CONNS", config.MaxIdleConns)
	return config
}

func environmentDuration(name string, fallback time.Duration) time.Duration {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return fallback
	}
	parsed, err := time.ParseDuration(value)
	if err != nil || parsed <= 0 {
		return fallback
	}
	return parsed
}

func environmentInt(name string, fallback int) int {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return fallback
	}
	parsed, err := strconv.Atoi(value)
	if err != nil || parsed < 0 {
		return fallback
	}
	return parsed
}

func lookupProvider(name string) (Provider, error) {
	name = strings.ToLower(strings.TrimSpace(name))
	if name == "" {
		name = DefaultProvider
	}
	// Accept PostgreSQL's two conventional provider spellings while keeping a
	// single canonical registry entry and ProviderName value.
	if name == "postgresql" {
		name = "postgres"
	}
	providerRegistry.RLock()
	provider, ok := providerRegistry.items[name]
	providerRegistry.RUnlock()
	if !ok {
		return nil, fmt.Errorf("database provider %q is not registered (available: %s)", name, strings.Join(AvailableProviders(), ", "))
	}
	return provider, nil
}
