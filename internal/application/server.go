package application

import (
	"context"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"time"

	"lokal-thesis/internal/auth"
	"lokal-thesis/internal/database"
	"lokal-thesis/internal/handlers"
	"lokal-thesis/internal/hub"
	"lokal-thesis/internal/middleware"
	"lokal-thesis/internal/runtimepaths"
	"lokal-thesis/internal/serverconfig"
	"lokal-thesis/internal/syncoutbox"
	"lokal-thesis/internal/wsrelay"
)

// Run starts the LOKAL HTTP/WebSocket server and blocks until it stops or the
// supplied context is cancelled. It is shared by console and Windows-service
// startup so both paths use exactly the same database and runtime behavior.
func Run(ctx context.Context) error {
	paths, err := runtimepaths.Resolve()
	if err != nil {
		return fmt.Errorf("resolve runtime paths: %w", err)
	}
	if err := paths.Ensure(); err != nil {
		return fmt.Errorf("initialize runtime directories: %w", err)
	}
	if err := auth.ConfigureSigningKey(paths.AuthSecret); err != nil {
		return fmt.Errorf("initialize authentication signing key: %w", err)
	}

	logFile, err := paths.OpenLog()
	if err != nil {
		return fmt.Errorf("initialize application log: %w", err)
	}
	defer logFile.Close()
	log.SetOutput(io.MultiWriter(os.Stdout, logFile))
	log.SetFlags(log.Ldate | log.Ltime | log.Lmicroseconds)

	serverCfg, err := serverconfig.Load(paths.ServerConfig)
	if err != nil {
		return fmt.Errorf("load server configuration: %w", err)
	}
	serverManager := serverconfig.NewManager(paths.ServerConfig, serverCfg)
	databaseConfig := database.ConfigFromEnvironment(paths.Database)

	exePath, _ := os.Executable()
	workingDirectory, _ := os.Getwd()
	migrated, migrationErr := paths.MigrateLegacyForProvider(databaseConfig.Provider == database.DefaultProvider, filepath.Dir(exePath), workingDirectory)
	for _, item := range migrated {
		log.Println("[Storage] Migrated", item)
	}
	if migrationErr != nil {
		log.Println("[Storage] Legacy migration warning:", migrationErr)
	}

	db, err := database.Open(databaseConfig)
	if err != nil {
		return fmt.Errorf("initialize database: %w", err)
	}
	defer db.Close()

	wsHub := hub.NewHub()
	go wsHub.Run()

	h := handlers.New(db, wsHub, paths.UploadsDir)
	h.SetServerConfig(serverManager)
	relayManager := wsrelay.New(db, wsHub, wsrelay.ConfigFromEnvironment())
	h.SetRelay(relayManager)
	if relayManager.Active() {
		wsHub.SetRelayForwarder(relayManager.Forward)
		relayManager.Start(ctx)
		defer relayManager.Stop()
	}
	syncManager := syncoutbox.New(db, syncoutbox.ConfigFromEnvironment())
	h.SetSync(syncManager)
	syncManager.Start(ctx)
	mux := http.NewServeMux()
	h.RegisterRoutes(mux)

	resourceRoot := ResourceRoot()
	webDir := filepath.Join(resourceRoot, "web")
	if info, statErr := os.Stat(webDir); statErr == nil && info.IsDir() {
		teacherFS := http.FileServer(http.Dir(filepath.Join(webDir, "teacher")))
		mux.Handle("GET /teacher/", http.StripPrefix("/teacher/", teacherFS))
		studentFS := http.FileServer(http.Dir(filepath.Join(webDir, "student")))
		mux.Handle("GET /student/", http.StripPrefix("/student/", studentFS))
		mux.Handle("GET /join/", http.StripPrefix("/join/", studentFS))
		sharedFS := http.FileServer(http.Dir(filepath.Join(webDir, "shared")))
		mux.Handle("GET /shared/", http.StripPrefix("/shared/", sharedFS))

		assetsFS := http.FileServer(http.Dir(filepath.Join(resourceRoot, "assets")))
		mux.Handle("GET /assets/", http.StripPrefix("/assets/", assetsFS))
		uploadsFS := http.FileServer(http.Dir(paths.UploadsDir))
		mux.Handle("GET /uploads/", http.StripPrefix("/uploads/", uploadsFS))
		mux.HandleFunc("GET /", func(w http.ResponseWriter, r *http.Request) {
			if r.URL.Path == "/" {
				http.Redirect(w, r, "/teacher/", http.StatusFound)
				return
			}
			http.NotFound(w, r)
		})
	} else {
		log.Printf("[Server] Web resources not found at %s", webDir)
	}

	status := serverManager.Status()
	log.Println("================================================")
	log.Println("                 LOKAL Server")
	log.Println("================================================")
	log.Printf("Operating Mode:    %s", status.ModeLabel)
	log.Printf("Teacher Dashboard: %s", status.TeacherURL)
	log.Printf("Student Join:      %s", status.StudentURL)
	log.Printf("API:               %s", status.APIURL)
	log.Printf("Resource Directory:%s", resourceRoot)
	log.Printf("Data Directory:    %s", paths.Root)
	log.Printf("Server Config:     %s", paths.ServerConfig)
	log.Printf("Log File:          %s", paths.LogFile)
	log.Println("================================================")

	server := &http.Server{
		Addr:              serverManager.ListenAddress(),
		Handler:           middleware.CORS(middleware.Logger(mux)),
		ReadHeaderTimeout: 10 * time.Second,
	}
	shutdownDone := make(chan struct{})
	go func() {
		select {
		case <-ctx.Done():
			shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			if err := server.Shutdown(shutdownCtx); err != nil {
				log.Printf("[Server] Graceful shutdown warning: %v", err)
			}
		case <-shutdownDone:
		}
	}()

	err = server.ListenAndServe()
	close(shutdownDone)
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}

// ResourceRoot returns the installation directory containing web and assets.
// The executable directory wins, while the current directory is retained as a
// development fallback for go run and test workflows.
func ResourceRoot() string {
	if override := os.Getenv("LOKAL_RESOURCE_DIR"); override != "" {
		if absolute, err := filepath.Abs(override); err == nil {
			return absolute
		}
	}
	if executable, err := os.Executable(); err == nil {
		root := filepath.Dir(executable)
		if info, err := os.Stat(filepath.Join(root, "web")); err == nil && info.IsDir() {
			return root
		}
	}
	if current, err := os.Getwd(); err == nil {
		return current
	}
	return "."
}
