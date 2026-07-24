package serverconfig

import (
	"path/filepath"
	"testing"
)

func TestDefaultPreservesLANBehavior(t *testing.T) {
	cfg := Default()
	if cfg.Mode != ModeLAN || cfg.BindAddress != "0.0.0.0" || cfg.Port != 8080 {
		t.Fatalf("unexpected default: %#v", cfg)
	}
}

func TestOfflineForcesLoopback(t *testing.T) {
	cfg, err := Normalize(Config{Mode: ModeOffline, BindAddress: "0.0.0.0", Port: 8080})
	if err != nil {
		t.Fatal(err)
	}
	if cfg.BindAddress != "127.0.0.1" {
		t.Fatalf("offline mode bound to %q", cfg.BindAddress)
	}
	status := NewManager("", cfg).Status()
	if len(status.LANURLs) != 0 {
		t.Fatalf("offline mode must not advertise LAN URLs: %v", status.LANURLs)
	}
}

func TestOnlineRequiresPublicURL(t *testing.T) {
	if _, err := Normalize(Config{Mode: ModeOnline, Port: 8080}); err == nil {
		t.Fatal("expected online mode without a public URL to fail")
	}
}

func TestLoadCreatesConfiguration(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config", "server.json")
	cfg, err := Load(path)
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Mode != ModeLAN {
		t.Fatalf("expected LAN mode, got %q", cfg.Mode)
	}
}

func TestSaveReplacesExistingConfiguration(t *testing.T) {
	path := filepath.Join(t.TempDir(), "server.json")
	if err := Save(path, Config{Mode: ModeLAN, Port: 8080}); err != nil {
		t.Fatal(err)
	}
	if err := Save(path, Config{Mode: ModeOffline, Port: 9090}); err != nil {
		t.Fatal(err)
	}
	loaded, err := Load(path)
	if err != nil {
		t.Fatal(err)
	}
	if loaded.Mode != ModeOffline || loaded.Port != 9090 || loaded.BindAddress != "127.0.0.1" {
		t.Fatalf("unexpected replacement configuration: %+v", loaded)
	}
}
