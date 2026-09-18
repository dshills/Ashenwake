package main

import (
	"context"
	"errors"
	"io"
	"net"
	"net/http"
	"sync/atomic"
	"testing"
	"time"
)

func TestShutdownWaitsForActiveRequestBeforeReturning(t *testing.T) {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	started, release := make(chan struct{}), make(chan struct{})
	defer close(release)
	var persisted atomic.Bool
	server := &http.Server{Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		close(started)
		<-release
		persisted.Store(true)
		_, _ = io.WriteString(w, "committed")
	})}
	defer server.Close()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	draining, finished := make(chan struct{}), make(chan error, 1)
	go func() {
		finished <- serveUntilCanceled(ctx, server, func() error { return server.Serve(listener) }, time.Second, func() { close(draining) })
	}()
	response := make(chan error, 1)
	go func() {
		client := &http.Client{Timeout: 2 * time.Second}
		r, err := client.Get("http://" + listener.Addr().String())
		if err == nil {
			defer r.Body.Close()
			var body []byte
			body, err = io.ReadAll(r.Body)
			if err == nil && string(body) != "committed" {
				err = errors.New("missing committed response")
			}
		}
		response <- err
	}()
	select {
	case <-started:
	case <-time.After(time.Second):
		t.Fatal("request did not start")
	}
	cancel()
	select {
	case <-draining:
	case <-time.After(time.Second):
		t.Fatal("drain did not start")
	}
	select {
	case err := <-finished:
		t.Fatalf("returned while request still active: %v", err)
	case <-time.After(30 * time.Millisecond):
	}
	release <- struct{}{}
	select {
	case err := <-response:
		if err != nil {
			t.Fatal(err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("request did not finish")
	}
	select {
	case err := <-finished:
		if err != nil {
			t.Fatal(err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("server did not finish")
	}
	if !persisted.Load() {
		t.Fatal("returned before persistence completed")
	}
}

func TestShutdownDeadlineClosesActiveConnectionAndReportsFailure(t *testing.T) {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	started, canceled := make(chan struct{}), make(chan struct{})
	server := &http.Server{Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { close(started); <-r.Context().Done(); close(canceled) })}
	defer server.Close()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	finished := make(chan error, 1)
	go func() {
		finished <- serveUntilCanceled(ctx, server, func() error { return server.Serve(listener) }, 20*time.Millisecond, func() {})
	}()
	response := make(chan error, 1)
	go func() {
		client := &http.Client{Timeout: 2 * time.Second}
		r, err := client.Get("http://" + listener.Addr().String())
		if r != nil {
			r.Body.Close()
		}
		response <- err
	}()
	select {
	case <-started:
	case <-time.After(time.Second):
		t.Fatal("request did not start")
	}
	cancel()
	select {
	case err := <-finished:
		if !errors.Is(err, context.DeadlineExceeded) {
			t.Fatalf("expected failed drain, got %v", err)
		}
	case <-time.After(time.Second):
		t.Fatal("deadline did not bound shutdown")
	}
	select {
	case <-canceled:
	case <-time.After(time.Second):
		t.Fatal("active connection was not closed")
	}
	select {
	case err := <-response:
		if err == nil {
			t.Fatal("unfinished request unexpectedly succeeded")
		}
	case <-time.After(time.Second):
		t.Fatal("client was not released")
	}
}

func TestListenerFailureDoesNotLeaveShutdownWorkerWaiting(t *testing.T) {
	want := errors.New("listener failed")
	var draining atomic.Bool
	if err := serveUntilCanceled(context.Background(), &http.Server{}, func() error { return want }, time.Second, func() { draining.Store(true) }); !errors.Is(err, want) {
		t.Fatalf("expected listener error, got %v", err)
	}
	if !draining.Load() {
		t.Fatal("listener failure did not close admission")
	}
}
