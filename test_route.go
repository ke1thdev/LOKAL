//go:build routetest
// +build routetest

package main

import (
	"fmt"
	"net/http"
)

func main() {
	m := http.NewServeMux()
	m.HandleFunc("POST /api/v1/activities/{id}/slide", func(w http.ResponseWriter, r *http.Request) {
		fmt.Fprint(w, "OK")
	})
	m.HandleFunc("GET /", func(w http.ResponseWriter, r *http.Request) {})
	go http.ListenAndServe(":8081", m)
	select {}
}
