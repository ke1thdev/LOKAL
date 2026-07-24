package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"os/signal"
	"strings"
	"syscall"

	"lokal-thesis/internal/application"
	"lokal-thesis/internal/winservice"
)

func main() {
	if winservice.IsServiceProcess() {
		if err := winservice.Run(application.Run); err != nil {
			log.Fatal(err)
		}
		return
	}

	if len(os.Args) >= 2 && strings.EqualFold(os.Args[1], "service") {
		if err := winservice.Command(os.Args[2:]); err != nil {
			fmt.Fprintln(os.Stderr, "LOKAL service:", err)
			os.Exit(1)
		}
		return
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if err := application.Run(ctx); err != nil {
		log.Fatal(err)
	}
}
