//go:build windows

package winservice

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/mgr"
)

const (
	Name        = "LOKALServer"
	DisplayName = "LOKAL Server"
	description = "Runs the LOKAL hybrid classroom server for PowerPoint, teacher, and student clients."
)

type runFunc func(context.Context) error

type handler struct {
	run runFunc
}

func IsServiceProcess() bool {
	service, err := svc.IsWindowsService()
	return err == nil && service
}

func Run(run runFunc) error {
	return svc.Run(Name, &handler{run: run})
}

func (h *handler) Execute(_ []string, requests <-chan svc.ChangeRequest, changes chan<- svc.Status) (bool, uint32) {
	changes <- svc.Status{State: svc.StartPending}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	errCh := make(chan error, 1)
	go func() { errCh <- h.run(ctx) }()

	accepted := svc.AcceptStop | svc.AcceptShutdown
	changes <- svc.Status{State: svc.Running, Accepts: accepted}
	for {
		select {
		case err := <-errCh:
			if err != nil {
				return false, 1
			}
			return false, 0
		case request := <-requests:
			switch request.Cmd {
			case svc.Interrogate:
				changes <- request.CurrentStatus
			case svc.Stop, svc.Shutdown:
				changes <- svc.Status{State: svc.StopPending}
				cancel()
				if err := <-errCh; err != nil {
					return false, 1
				}
				return false, 0
			}
		}
	}
}

func Command(args []string) error {
	if len(args) == 0 {
		return errors.New("usage: lokal.exe service <install|start|stop|restart|status|uninstall>")
	}
	switch strings.ToLower(args[0]) {
	case "install":
		return install()
	case "start":
		return start()
	case "stop":
		return stop()
	case "restart":
		if err := stop(); err != nil && !strings.Contains(strings.ToLower(err.Error()), "already stopped") {
			return err
		}
		return start()
	case "status":
		state, err := status()
		if err != nil {
			return err
		}
		fmt.Printf("%s is %s\n", DisplayName, state)
		return nil
	case "uninstall", "remove":
		return uninstall()
	default:
		return fmt.Errorf("unknown service command %q", args[0])
	}
}

func connect() (*mgr.Mgr, error) {
	manager, err := mgr.Connect()
	if err != nil {
		return nil, fmt.Errorf("connect to Windows Service Control Manager (run as administrator): %w", err)
	}
	return manager, nil
}

func desiredConfig(executable string) mgr.Config {
	return mgr.Config{
		DisplayName:      DisplayName,
		Description:      description,
		StartType:        mgr.StartAutomatic,
		DelayedAutoStart: true,
		ErrorControl:     mgr.ErrorNormal,
		BinaryPathName:   fmt.Sprintf("\"%s\" service run", executable),
	}
}

func install() error {
	executable, err := os.Executable()
	if err != nil {
		return fmt.Errorf("resolve lokal.exe: %w", err)
	}
	executable, err = filepath.Abs(executable)
	if err != nil {
		return fmt.Errorf("resolve absolute executable path: %w", err)
	}
	for _, directory := range []string{"web", "assets"} {
		path := filepath.Join(filepath.Dir(executable), directory)
		if info, statErr := os.Stat(path); statErr != nil || !info.IsDir() {
			return fmt.Errorf("required installation directory is missing: %s", path)
		}
	}
	manager, err := connect()
	if err != nil {
		return err
	}
	defer manager.Disconnect()

	service, openErr := manager.OpenService(Name)
	existing := openErr == nil
	if openErr == nil {
		if err := service.UpdateConfig(desiredConfig(executable)); err != nil {
			service.Close()
			return fmt.Errorf("update existing service: %w", err)
		}
	} else if errors.Is(openErr, windows.ERROR_SERVICE_DOES_NOT_EXIST) {
		service, err = manager.CreateService(Name, executable, desiredConfig(executable), "service", "run")
		if err != nil {
			return fmt.Errorf("create service (run as administrator): %w", err)
		}
	} else {
		return fmt.Errorf("open existing service (run as administrator): %w", openErr)
	}
	defer service.Close()

	actions := []mgr.RecoveryAction{
		{Type: mgr.ServiceRestart, Delay: 5 * time.Second},
		{Type: mgr.ServiceRestart, Delay: 15 * time.Second},
		{Type: mgr.ServiceRestart, Delay: 60 * time.Second},
	}
	if err := service.SetRecoveryActions(actions, 24*60*60); err != nil {
		return fmt.Errorf("configure service recovery: %w", err)
	}
	if err := service.SetRecoveryActionsOnNonCrashFailures(true); err != nil {
		return fmt.Errorf("configure non-crash recovery: %w", err)
	}

	current, err := service.Query()
	if err != nil {
		return fmt.Errorf("query installed service: %w", err)
	}
	if existing && current.State != svc.Stopped {
		if _, err := service.Control(svc.Stop); err != nil {
			return fmt.Errorf("stop existing service for upgrade: %w", err)
		}
		if _, err := waitForState(service, svc.Stopped, 20*time.Second); err != nil {
			return err
		}
		current.State = svc.Stopped
	}
	if current.State == svc.Stopped {
		if err := service.Start(); err != nil {
			return fmt.Errorf("start installed service: %w", err)
		}
		if _, err := waitForState(service, svc.Running, 20*time.Second); err != nil {
			return err
		}
	}
	fmt.Printf("%s installed and running.\n", DisplayName)
	return nil
}

func openInstalled() (*mgr.Mgr, *mgr.Service, error) {
	manager, err := connect()
	if err != nil {
		return nil, nil, err
	}
	service, err := manager.OpenService(Name)
	if err != nil {
		manager.Disconnect()
		return nil, nil, fmt.Errorf("open %s: %w", DisplayName, err)
	}
	return manager, service, nil
}

func start() error {
	manager, service, err := openInstalled()
	if err != nil {
		return err
	}
	defer manager.Disconnect()
	defer service.Close()
	current, err := service.Query()
	if err != nil {
		return err
	}
	if current.State == svc.Running {
		fmt.Printf("%s is already running.\n", DisplayName)
		return nil
	}
	if err := service.Start(); err != nil {
		return fmt.Errorf("start service: %w", err)
	}
	_, err = waitForState(service, svc.Running, 20*time.Second)
	return err
}

func stop() error {
	manager, service, err := openInstalled()
	if err != nil {
		return err
	}
	defer manager.Disconnect()
	defer service.Close()
	current, err := service.Query()
	if err != nil {
		return err
	}
	if current.State == svc.Stopped {
		return errors.New("service is already stopped")
	}
	if _, err := service.Control(svc.Stop); err != nil {
		return fmt.Errorf("stop service: %w", err)
	}
	_, err = waitForState(service, svc.Stopped, 20*time.Second)
	return err
}

func uninstall() error {
	manager, service, err := openInstalled()
	if err != nil {
		return err
	}
	defer manager.Disconnect()
	defer service.Close()
	current, queryErr := service.Query()
	if queryErr == nil && current.State != svc.Stopped {
		_, _ = service.Control(svc.Stop)
		_, _ = waitForState(service, svc.Stopped, 20*time.Second)
	}
	if err := service.Delete(); err != nil {
		return fmt.Errorf("delete service: %w", err)
	}
	fmt.Printf("%s uninstalled. ProgramData classroom data was preserved.\n", DisplayName)
	return nil
}

func status() (string, error) {
	manager, service, err := openInstalled()
	if err != nil {
		return "not installed", err
	}
	defer manager.Disconnect()
	defer service.Close()
	current, err := service.Query()
	if err != nil {
		return "unknown", err
	}
	return stateName(current.State), nil
}

func waitForState(service *mgr.Service, wanted svc.State, timeout time.Duration) (svc.Status, error) {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		current, err := service.Query()
		if err != nil {
			return current, err
		}
		if current.State == wanted {
			return current, nil
		}
		time.Sleep(250 * time.Millisecond)
	}
	current, _ := service.Query()
	return current, fmt.Errorf("timed out waiting for %s to become %s (current: %s)", DisplayName, stateName(wanted), stateName(current.State))
}

func stateName(state svc.State) string {
	switch state {
	case svc.Stopped:
		return "stopped"
	case svc.StartPending:
		return "starting"
	case svc.StopPending:
		return "stopping"
	case svc.Running:
		return "running"
	case svc.PausePending:
		return "pausing"
	case svc.Paused:
		return "paused"
	case svc.ContinuePending:
		return "resuming"
	default:
		return "unknown"
	}
}
