package main

import (
	"context"
	"errors"
	"net/http"
	"time"
)

// Shutdown closes listeners before it finishes draining handlers. Keep the caller
// (and its database) alive until that drain completes or the grace period expires.
func serveUntilCanceled(ctx context.Context, server *http.Server, serve func() error, grace time.Duration, draining func()) error {
	stop, cancel := context.WithCancel(ctx)
	defer cancel()
	drained := make(chan error, 1)
	go func() {
		<-stop.Done()
		draining()
		deadline, release := context.WithTimeout(context.Background(), grace)
		defer release()
		err := server.Shutdown(deadline)
		if err != nil {
			// Stop remaining connections after the bounded grace period. Returning
			// the error prevents a timed-out drain from being reported as success.
			_ = server.Close()
		}
		drained <- err
	}()
	err := serve()
	cancel() // Also release the shutdown worker if the listener failed to start.
	drainErr := <-drained
	if err != nil && !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return drainErr
}
