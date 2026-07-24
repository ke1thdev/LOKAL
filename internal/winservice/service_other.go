//go:build !windows

package winservice

import (
	"context"
	"errors"
)

type runFunc func(context.Context) error

func IsServiceProcess() bool { return false }
func Run(run runFunc) error  { return run(context.Background()) }
func Command(_ []string) error {
	return errors.New("Windows service management is available only on Windows")
}
