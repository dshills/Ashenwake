package main

import (
	"context"
	"log/slog"
	"net"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"ashenwake/services/online/internal/control"
)

func main() {
	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	bind := os.Getenv("ASHENWAKE_HTTP_ADDR")
	if bind == "" {
		bind = "127.0.0.1:8088"
	}
	host, port, err := net.SplitHostPort(bind)
	if err != nil || port == "" || (host != "127.0.0.1" && host != "::1" && !(host == "0.0.0.0" && os.Getenv("ASHENWAKE_CONTAINER_BIND") == "1")) {
		logger.Error("invalid local HTTP bind")
		os.Exit(1)
	}
	serverURL := os.Getenv("ASHENWAKE_GAME_SERVER_URL")
	if serverURL == "" {
		serverURL = "ws://127.0.0.1:5180/v1/matches/{allocationId}/socket"
	}
	parsed, err := url.Parse(serverURL)
	if err != nil || parsed.Scheme != "ws" || parsed.Port() == "" || parsed.User != nil || parsed.RawQuery != "" || parsed.Fragment != "" || (parsed.Hostname() != "127.0.0.1" && parsed.Hostname() != "::1" && parsed.Hostname() != "localhost") {
		logger.Error("invalid local game server URL")
		os.Exit(1)
	}
	dsn := os.Getenv("ASHENWAKE_DATABASE_URL")
	key := os.Getenv("ASHENWAKE_SERVER_KEY")
	if strings.TrimSpace(dsn) == "" || len(key) < 32 {
		logger.Error("database URL and server key are required")
		os.Exit(1)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	store, err := control.Open(ctx, dsn, serverURL)
	cancel()
	if err != nil {
		logger.Error("database startup or migration failed")
		os.Exit(1)
	}
	defer store.Close()
	api, err := control.NewAPI(store, key, logger)
	if err != nil {
		logger.Error("invalid server key")
		os.Exit(1)
	}
	server := &http.Server{Addr: bind, Handler: api, ReadHeaderTimeout: 3 * time.Second, ReadTimeout: 12 * time.Second, WriteTimeout: 15 * time.Second, IdleTimeout: 30 * time.Second, MaxHeaderBytes: 16 << 10}
	stop, stopSignals := signal.NotifyContext(context.Background(), syscall.SIGTERM, syscall.SIGINT)
	defer stopSignals()
	logger.Info("online_service_started", "protocol", control.ProtocolVersion, "bind", bind)
	if err = serveUntilCanceled(stop, server, server.ListenAndServe, 12*time.Second, func() { api.SetDraining(true) }); err != nil {
		logger.Error("HTTP listener or graceful shutdown failed")
		os.Exit(1)
	}
	logger.Info("online_service_stopped")
}
