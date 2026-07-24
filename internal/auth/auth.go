package auth

import (
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"golang.org/x/crypto/bcrypt"
)

var (
	secretMu  sync.RWMutex
	jwtSecret = randomBytes(32)
)

const (
	TeacherSessionTTL = 30 * 24 * time.Hour
	StudentSessionTTL = 30 * 24 * time.Hour
)

type Claims struct {
	TeacherID int64  `json:"teacher_id"`
	Username  string `json:"username"`
	jwt.RegisteredClaims
}

func HashPassword(password string) (string, error) {
	bytes, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	return string(bytes), err
}

func CheckPassword(password, hash string) bool {
	return bcrypt.CompareHashAndPassword([]byte(hash), []byte(password)) == nil
}

// ConfigureSigningKey loads or creates the persistent presenter-token key.
func ConfigureSigningKey(path string) error {
	if configured := strings.TrimSpace(os.Getenv("LOKAL_JWT_SECRET")); configured != "" {
		if len(configured) < 32 {
			return errors.New("LOKAL_JWT_SECRET must contain at least 32 characters")
		}
		setSigningKey([]byte(configured))
		return nil
	}
	if path == "" {
		return errors.New("auth signing key path is required")
	}
	if data, err := os.ReadFile(path); err == nil {
		if len(data) < 32 {
			return errors.New("auth signing key is invalid")
		}
		setSigningKey(data)
		return nil
	} else if !os.IsNotExist(err) {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
		return err
	}
	key := randomBytes(48)
	f, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		if os.IsExist(err) {
			data, readErr := os.ReadFile(path)
			if readErr != nil {
				return readErr
			}
			setSigningKey(data)
			return nil
		}
		return err
	}
	if _, err = f.Write(key); err != nil {
		_ = f.Close()
		return err
	}
	if err = f.Close(); err != nil {
		return err
	}
	setSigningKey(key)
	return nil
}

func setSigningKey(key []byte) {
	secretMu.Lock()
	jwtSecret = append([]byte(nil), key...)
	secretMu.Unlock()
}

func signingKey() []byte {
	secretMu.RLock()
	defer secretMu.RUnlock()
	return append([]byte(nil), jwtSecret...)
}

func GenerateToken(teacherID int64, username string) (string, error) {
	claims := &Claims{
		TeacherID: teacherID,
		Username:  username,
		RegisteredClaims: jwt.RegisteredClaims{
			Issuer:    "lokal",
			Audience:  jwt.ClaimStrings{"lokal-presenter"},
			ExpiresAt: jwt.NewNumericDate(time.Now().Add(72 * time.Hour)),
			IssuedAt:  jwt.NewNumericDate(time.Now()),
		},
	}
	return jwt.NewWithClaims(jwt.SigningMethodHS256, claims).SignedString(signingKey())
}

func ValidateToken(tokenString string) (*Claims, error) {
	token, err := jwt.ParseWithClaims(tokenString, &Claims{}, func(token *jwt.Token) (interface{}, error) {
		if token.Method != jwt.SigningMethodHS256 {
			return nil, fmt.Errorf("unexpected signing method: %v", token.Header["alg"])
		}
		return signingKey(), nil
	}, jwt.WithIssuer("lokal"), jwt.WithAudience("lokal-presenter"))
	if err != nil {
		return nil, err
	}
	if claims, ok := token.Claims.(*Claims); ok && token.Valid {
		return claims, nil
	}
	return nil, errors.New("invalid token")
}

func GenerateOpaqueToken(prefix string) (raw string, hash string) {
	raw = prefix + base64.RawURLEncoding.EncodeToString(randomBytes(32))
	return raw, HashToken(raw)
}

func HashToken(raw string) string {
	sum := sha256.Sum256([]byte(raw))
	return base64.RawURLEncoding.EncodeToString(sum[:])
}

func randomBytes(size int) []byte {
	value := make([]byte, size)
	if _, err := rand.Read(value); err != nil {
		panic("secure random source unavailable: " + err.Error())
	}
	return value
}
