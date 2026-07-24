package wsrelay

import (
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"
)

type Config struct {
	URL             string
	Secret          string
	NodeID          string
	HostEnabled     bool
	QueueSize       int
	ConnectTimeout  time.Duration
	RefreshInterval time.Duration
	MinBackoff      time.Duration
	MaxBackoff      time.Duration
}

func ConfigFromEnvironment() Config {
	secret := strings.TrimSpace(os.Getenv("LOKAL_RELAY_SECRET"))
	if secret == "" {
		secret = strings.TrimSpace(os.Getenv("LOKAL_SYNC_SECRET"))
	}
	return Config{
		URL:             strings.TrimSpace(os.Getenv("LOKAL_RELAY_URL")),
		Secret:          secret,
		NodeID:          strings.TrimSpace(os.Getenv("LOKAL_RELAY_NODE_ID")),
		HostEnabled:     envBool("LOKAL_RELAY_HOST_ENABLED"),
		QueueSize:       envInt("LOKAL_RELAY_QUEUE_SIZE", 1024),
		ConnectTimeout:  10 * time.Second,
		RefreshInterval: 30 * time.Second,
		MinBackoff:      time.Second,
		MaxBackoff:      30 * time.Second,
	}
}

func (c Config) EdgeEnabled() bool {
	return strings.TrimSpace(c.URL) != "" && strings.TrimSpace(c.Secret) != ""
}

func (c Config) Active() bool {
	return c.EdgeEnabled() || (c.HostEnabled && strings.TrimSpace(c.Secret) != "")
}

func (c Config) relayEndpoint() (string, error) {
	raw := strings.TrimSpace(c.URL)
	if raw == "" {
		return "", nil
	}
	if !strings.Contains(raw, "://") {
		raw = "https://" + raw
	}
	parsed, err := url.Parse(raw)
	if err != nil {
		return "", err
	}
	switch parsed.Scheme {
	case "http":
		parsed.Scheme = "ws"
	case "https":
		parsed.Scheme = "wss"
	case "ws", "wss":
	default:
		parsed.Scheme = "wss"
	}
	path := strings.TrimRight(parsed.Path, "/")
	if !strings.HasSuffix(path, "/api/v1/relay/edge") {
		path += "/api/v1/relay/edge"
	}
	parsed.Path = path
	return parsed.String(), nil
}

func envBool(name string) bool {
	value, _ := strconv.ParseBool(strings.TrimSpace(os.Getenv(name)))
	return value
}

func envInt(name string, fallback int) int {
	value, err := strconv.Atoi(strings.TrimSpace(os.Getenv(name)))
	if err != nil || value <= 0 {
		return fallback
	}
	return value
}
