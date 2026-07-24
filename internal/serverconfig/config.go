package serverconfig

import (
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

type Mode string

const (
	ModeOffline Mode = "offline"
	ModeLAN     Mode = "lan"
	ModeOnline  Mode = "online"
)

type Config struct {
	Mode        Mode   `json:"mode"`
	BindAddress string `json:"bind_address"`
	Port        int    `json:"port"`
	PublicURL   string `json:"public_url,omitempty"`
}

type Status struct {
	Mode             Mode     `json:"mode"`
	ModeLabel        string   `json:"mode_label"`
	Running          bool     `json:"running"`
	ListenAddress    string   `json:"listen_address"`
	TeacherURL       string   `json:"teacher_url"`
	StudentURL       string   `json:"student_url"`
	APIURL           string   `json:"api_url"`
	WebSocketURL     string   `json:"websocket_url"`
	LANURLs          []string `json:"lan_urls"`
	PublicURL        string   `json:"public_url,omitempty"`
	ConfigurationOK  bool     `json:"configuration_ok"`
	ConfigurationMsg string   `json:"configuration_message"`
	RestartRequired  bool     `json:"restart_required"`
}

type Manager struct {
	mu      sync.RWMutex
	path    string
	active  Config
	saved   Config
	restart bool
}

func Default() Config {
	return Config{Mode: ModeLAN, BindAddress: "0.0.0.0", Port: 8080}
}

func Load(path string) (Config, error) {
	cfg := Default()
	data, err := os.ReadFile(path)
	if err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			return Config{}, fmt.Errorf("read server configuration: %w", err)
		}
		if err := Save(path, cfg); err != nil {
			return Config{}, err
		}
	} else if err := json.Unmarshal(data, &cfg); err != nil {
		return Config{}, fmt.Errorf("parse server configuration: %w", err)
	}

	applyEnvironment(&cfg)
	return Normalize(cfg)
}

func Save(path string, cfg Config) error {
	normalized, err := Normalize(cfg)
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		return fmt.Errorf("create server configuration directory: %w", err)
	}
	data, err := json.MarshalIndent(normalized, "", "  ")
	if err != nil {
		return fmt.Errorf("encode server configuration: %w", err)
	}
	temporary := path + ".tmp"
	if err := os.WriteFile(temporary, append(data, '\n'), 0644); err != nil {
		return fmt.Errorf("write server configuration: %w", err)
	}
	if err := os.Rename(temporary, path); err != nil {
		// Windows does not replace an existing destination with os.Rename.
		// Keep the previous file recoverable until the new configuration is in place.
		backup := path + ".bak"
		_ = os.Remove(backup)
		if backupErr := os.Rename(path, backup); backupErr != nil && !errors.Is(backupErr, os.ErrNotExist) {
			_ = os.Remove(temporary)
			return fmt.Errorf("prepare server configuration replacement: %w", backupErr)
		}
		if replaceErr := os.Rename(temporary, path); replaceErr != nil {
			_ = os.Rename(backup, path)
			_ = os.Remove(temporary)
			return fmt.Errorf("replace server configuration: %w", replaceErr)
		}
		_ = os.Remove(backup)
	}
	return nil
}

func Normalize(cfg Config) (Config, error) {
	if cfg.Mode == "" {
		cfg.Mode = ModeLAN
	}
	switch cfg.Mode {
	case ModeOffline:
		cfg.BindAddress = "127.0.0.1"
	case ModeLAN, ModeOnline:
		if strings.TrimSpace(cfg.BindAddress) == "" || cfg.BindAddress == "127.0.0.1" || cfg.BindAddress == "localhost" {
			cfg.BindAddress = "0.0.0.0"
		}
	default:
		return Config{}, fmt.Errorf("mode must be offline, lan, or online")
	}
	if cfg.Port == 0 {
		cfg.Port = 8080
	}
	if cfg.Port < 1 || cfg.Port > 65535 {
		return Config{}, fmt.Errorf("port must be between 1 and 65535")
	}
	cfg.BindAddress = strings.TrimSpace(cfg.BindAddress)
	if ip := net.ParseIP(cfg.BindAddress); ip == nil && cfg.BindAddress != "localhost" {
		return Config{}, fmt.Errorf("bind address must be a valid IP address")
	}
	cfg.PublicURL = strings.TrimRight(strings.TrimSpace(cfg.PublicURL), "/")
	if cfg.PublicURL != "" {
		parsed, err := url.Parse(cfg.PublicURL)
		if err != nil || parsed.Host == "" || (parsed.Scheme != "http" && parsed.Scheme != "https") {
			return Config{}, fmt.Errorf("public URL must be a complete http:// or https:// URL")
		}
	}
	if cfg.Mode == ModeOnline && cfg.PublicURL == "" {
		return Config{}, fmt.Errorf("online mode requires a public URL")
	}
	return cfg, nil
}

func NewManager(path string, active Config) *Manager {
	return &Manager{path: path, active: active, saved: active}
}

func (m *Manager) Active() Config {
	m.mu.RLock()
	defer m.mu.RUnlock()
	return m.active
}

func (m *Manager) Saved() Config {
	m.mu.RLock()
	defer m.mu.RUnlock()
	return m.saved
}

func (m *Manager) Update(cfg Config) (Config, bool, error) {
	normalized, err := Normalize(cfg)
	if err != nil {
		return Config{}, false, err
	}
	if err := Save(m.path, normalized); err != nil {
		return Config{}, false, err
	}
	m.mu.Lock()
	m.saved = normalized
	m.restart = normalized != m.active
	restart := m.restart
	m.mu.Unlock()
	return normalized, restart, nil
}

func (m *Manager) ListenAddress() string {
	cfg := m.Active()
	return net.JoinHostPort(cfg.BindAddress, strconv.Itoa(cfg.Port))
}

func (m *Manager) AdvertisedBaseURL() string {
	cfg := m.Active()
	if cfg.Mode == ModeOnline && cfg.PublicURL != "" {
		return cfg.PublicURL
	}
	if cfg.Mode == ModeOffline {
		return fmt.Sprintf("http://localhost:%d", cfg.Port)
	}
	addresses := LANAddresses(cfg.Port)
	if len(addresses) > 0 {
		return addresses[0]
	}
	return fmt.Sprintf("http://localhost:%d", cfg.Port)
}

func (m *Manager) Status() Status {
	cfg := m.Active()
	base := m.AdvertisedBaseURL()
	lanURLs := []string{}
	if cfg.Mode != ModeOffline {
		lanURLs = LANAddresses(cfg.Port)
	}
	label := map[Mode]string{ModeOffline: "Offline", ModeLAN: "Local Network", ModeOnline: "Online"}[cfg.Mode]
	m.mu.RLock()
	restart := m.restart
	m.mu.RUnlock()
	return Status{
		Mode: cfg.Mode, ModeLabel: label, Running: true,
		ListenAddress: m.ListenAddress(), TeacherURL: base + "/teacher/",
		StudentURL: base + "/student/", APIURL: base + "/api/v1/",
		WebSocketURL: websocketURL(base) + "/ws", LANURLs: lanURLs,
		PublicURL: cfg.PublicURL, ConfigurationOK: true, ConfigurationMsg: "Ready",
		RestartRequired: restart,
	}
}

func LANAddresses(port int) []string {
	interfaces, err := net.Interfaces()
	if err != nil {
		return []string{}
	}
	seen := map[string]bool{}
	ips := []string{}
	for _, iface := range interfaces {
		if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
			continue
		}
		addresses, err := iface.Addrs()
		if err != nil {
			continue
		}
		for _, address := range addresses {
			var ip net.IP
			switch value := address.(type) {
			case *net.IPNet:
				ip = value.IP
			case *net.IPAddr:
				ip = value.IP
			}
			if ip == nil || ip.To4() == nil || ip.IsLoopback() {
				continue
			}
			candidate := ip.String()
			if !seen[candidate] {
				seen[candidate] = true
				ips = append(ips, candidate)
			}
		}
	}
	preferred := preferredOutboundIPv4()
	sort.Slice(ips, func(i, j int) bool {
		if ips[i] == preferred {
			return true
		}
		if ips[j] == preferred {
			return false
		}
		leftRank, rightRank := addressRank(net.ParseIP(ips[i])), addressRank(net.ParseIP(ips[j]))
		if leftRank != rightRank {
			return leftRank < rightRank
		}
		return ips[i] < ips[j]
	})
	urls := make([]string, 0, len(ips))
	for _, ip := range ips {
		urls = append(urls, fmt.Sprintf("http://%s:%d", ip, port))
	}
	return urls
}

func preferredOutboundIPv4() string {
	connection, err := net.DialTimeout("udp", "8.8.8.8:80", 250*time.Millisecond)
	if err != nil {
		return ""
	}
	defer connection.Close()
	address, ok := connection.LocalAddr().(*net.UDPAddr)
	if !ok || address.IP.To4() == nil {
		return ""
	}
	return address.IP.String()
}

func addressRank(ip net.IP) int {
	value := ip.To4()
	if value == nil {
		return 5
	}
	if value[0] == 192 && value[1] == 168 {
		return 0
	}
	if value[0] == 10 {
		return 1
	}
	if value[0] == 172 && value[1] >= 16 && value[1] <= 31 {
		return 2
	}
	if value[0] == 169 && value[1] == 254 {
		return 4
	}
	return 3
}

func websocketURL(base string) string {
	if strings.HasPrefix(base, "https://") {
		return "wss://" + strings.TrimPrefix(base, "https://")
	}
	return "ws://" + strings.TrimPrefix(base, "http://")
}

func applyEnvironment(cfg *Config) {
	if value := strings.TrimSpace(os.Getenv("LOKAL_SERVER_MODE")); value != "" {
		cfg.Mode = Mode(strings.ToLower(value))
	}
	if value := strings.TrimSpace(os.Getenv("LOKAL_BIND_ADDRESS")); value != "" {
		cfg.BindAddress = value
	}
	if value := strings.TrimSpace(os.Getenv("LOKAL_PUBLIC_URL")); value != "" {
		cfg.PublicURL = value
	}
	if value := strings.TrimSpace(os.Getenv("PORT")); value != "" {
		if port, err := strconv.Atoi(value); err == nil {
			cfg.Port = port
		}
	}
}
