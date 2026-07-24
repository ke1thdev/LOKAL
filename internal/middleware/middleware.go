package middleware

import (
	"context"
	"log"
	"net/http"
	"strings"
	"sync"
	"time"

	"lokal-thesis/internal/auth"
)

type contextKey string

const TeacherIDKey contextKey = "teacher_id"
const UsernameKey contextKey = "username"

type SessionIdentity struct {
	TeacherID int64
	Username  string
}

type SessionValidator func(rawToken string) (*SessionIdentity, error)

var (
	validatorMu      sync.RWMutex
	sessionValidator SessionValidator
)

func SetSessionValidator(validator SessionValidator) {
	validatorMu.Lock()
	sessionValidator = validator
	validatorMu.Unlock()
}

func validateTeacherToken(tokenString string) (*SessionIdentity, error) {
	validatorMu.RLock()
	validator := sessionValidator
	validatorMu.RUnlock()
	if validator != nil && strings.HasPrefix(tokenString, "lkt_") {
		return validator(tokenString)
	}
	claims, err := auth.ValidateToken(tokenString)
	if err != nil {
		return nil, err
	}
	return &SessionIdentity{TeacherID: claims.TeacherID, Username: claims.Username}, nil
}

// CORS adds CORS headers
func CORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", "*")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization")

		if r.Method == "OPTIONS" {
			w.WriteHeader(http.StatusOK)
			return
		}
		next.ServeHTTP(w, r)
	})
}

// Logger logs HTTP requests
func Logger(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		start := time.Now()
		next.ServeHTTP(w, r)
		log.Printf("[%s] %s %s (%v)", r.Method, r.URL.Path, r.RemoteAddr, time.Since(start))
	})
}

// Auth validates JWT token and injects teacher ID into context
func Auth(next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		authHeader := r.Header.Get("Authorization")
		if authHeader == "" {
			http.Error(w, `{"success":false,"error":"unauthorized"}`, http.StatusUnauthorized)
			return
		}

		if !strings.HasPrefix(authHeader, "Bearer ") {
			http.Error(w, `{"success":false,"error":"invalid authorization scheme"}`, http.StatusUnauthorized)
			return
		}
		tokenString := strings.TrimPrefix(authHeader, "Bearer ")
		identity, err := validateTeacherToken(tokenString)
		if err != nil {
			http.Error(w, `{"success":false,"error":"invalid token"}`, http.StatusUnauthorized)
			return
		}

		ctx := context.WithValue(r.Context(), TeacherIDKey, identity.TeacherID)
		ctx = context.WithValue(ctx, UsernameKey, identity.Username)
		next.ServeHTTP(w, r.WithContext(ctx))
	}
}

// GetTeacherID extracts teacher ID from request context
func GetTeacherID(r *http.Request) int64 {
	id, _ := r.Context().Value(TeacherIDKey).(int64)
	return id
}
