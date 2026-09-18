package control

import (
	"bytes"
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"mime"
	"net"
	"net/http"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type API struct {
	Store     *Store
	Logger    *slog.Logger
	serverKey [32]byte
	draining  atomic.Bool
	requests  atomic.Uint64
	failures  atomic.Uint64
	active    atomic.Int64
	limiter   rateLimiter
	slots     chan struct{}
	mux       *http.ServeMux
}
type rateEntry struct {
	tokens  float64
	updated time.Time
}
type rateLimiter struct {
	sync.Mutex
	entries map[string]rateEntry
}

func (r *rateLimiter) allow(key string, perMinute float64) bool {
	r.Lock()
	defer r.Unlock()
	now := time.Now()
	if r.entries == nil {
		r.entries = map[string]rateEntry{}
	}
	if len(r.entries) >= 4096 {
		for k, v := range r.entries {
			if now.Sub(v.updated) > time.Minute {
				delete(r.entries, k)
			}
		}
		if len(r.entries) >= 4096 {
			return false
		}
	}
	v, ok := r.entries[key]
	if !ok {
		v = rateEntry{tokens: perMinute, updated: now}
	}
	v.tokens += now.Sub(v.updated).Seconds() * perMinute / 60
	if v.tokens > perMinute {
		v.tokens = perMinute
	}
	v.updated = now
	allowed := v.tokens >= 1
	if allowed {
		v.tokens--
	}
	r.entries[key] = v
	return allowed
}
func NewAPI(store *Store, key string, logger *slog.Logger) (*API, error) {
	if len(key) < 32 || len(key) > 256 || strings.ContainsAny(key, "\r\n") {
		return nil, ErrInvalid
	}
	if logger == nil {
		logger = slog.New(slog.NewJSONHandler(io.Discard, nil))
	}
	a := &API{Store: store, Logger: logger, serverKey: sha256.Sum256([]byte(key)), slots: make(chan struct{}, 64), mux: http.NewServeMux()}
	a.routes()
	return a, nil
}
func (a *API) SetDraining(value bool) { a.draining.Store(value) }

type responseWriter struct {
	http.ResponseWriter
	status int
}

func (w *responseWriter) WriteHeader(code int) {
	if w.status == 0 {
		w.status = code
		w.ResponseWriter.WriteHeader(code)
	}
}
func (w *responseWriter) Write(b []byte) (int, error) {
	if w.status == 0 {
		w.WriteHeader(200)
	}
	return w.ResponseWriter.Write(b)
}
func (a *API) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	started := time.Now()
	rw := &responseWriter{ResponseWriter: w}
	a.requests.Add(1)
	a.active.Add(1)
	defer a.active.Add(-1)
	rw.Header().Set("Cache-Control", "no-store")
	rw.Header().Set("X-Content-Type-Options", "nosniff")
	defer func() {
		if recover() != nil {
			a.failures.Add(1)
			writeError(rw, errors.New("internal"))
		}
		status := rw.status
		if status == 0 {
			status = 200
		}
		if status >= 400 {
			a.failures.Add(1)
		}
		pattern := r.Pattern
		if pattern == "" {
			pattern = "unmatched"
		}
		a.Logger.Info("http_request", "route", pattern, "status", status, "duration_ms", time.Since(started).Milliseconds())
	}()
	if len(r.URL.RawQuery) > 0 {
		writeError(rw, ErrInvalid)
		return
	}
	select {
	case a.slots <- struct{}{}:
		defer func() { <-a.slots }()
	default:
		writeJSON(rw, 503, map[string]string{"error": "busy"})
		return
	}
	ip, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		ip = "unknown"
	}
	class, limit := "client", float64(240)
	if strings.HasPrefix(r.URL.Path, "/v1/server/") || r.URL.Path == "/metrics" {
		class, limit = "server", 1200
	} else if r.URL.Path == "/v1/sessions/anonymous" {
		class, limit = "anonymous", 12
	}
	if !a.limiter.allow(ip+"/"+class, limit) {
		rw.Header().Set("Retry-After", "5")
		writeJSON(rw, 429, map[string]string{"error": "rate_limited"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 10*time.Second)
	defer cancel()
	r = r.WithContext(ctx)
	r.Body = http.MaxBytesReader(rw, r.Body, MaxBodyBytes)
	a.mux.ServeHTTP(rw, r)
}
func writeJSON(w http.ResponseWriter, status int, value any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(value)
}
func writeError(w http.ResponseWriter, err error) {
	status, code := 500, "internal_error"
	switch {
	case errors.Is(err, ErrUnauthorized):
		status, code = 401, "unauthorized"
	case errors.Is(err, ErrNotFound):
		status, code = 404, "not_found"
	case errors.Is(err, ErrConflict):
		status, code = 409, "conflict"
	case errors.Is(err, ErrInvalid):
		status, code = 400, "invalid_request"
	case errors.Is(err, ErrDraining):
		status, code = 503, "draining"
	case errors.Is(err, context.DeadlineExceeded):
		status, code = 503, "timeout"
	}
	writeJSON(w, status, map[string]string{"error": code})
}
func result(w http.ResponseWriter, value any, err error) {
	if err != nil {
		writeError(w, err)
		return
	}
	writeJSON(w, 200, value)
}
func bearer(r *http.Request) string {
	header := r.Header.Get("Authorization")
	if !strings.HasPrefix(header, "Bearer ") {
		return ""
	}
	token := strings.TrimPrefix(header, "Bearer ")
	if strings.ContainsAny(token, " \t\r\n") {
		return ""
	}
	return token
}
func (a *API) user(w http.ResponseWriter, r *http.Request) (string, bool) {
	account, err := a.Store.Authenticate(r.Context(), bearer(r))
	if err != nil {
		writeError(w, err)
		return "", false
	}
	return account, true
}
func (a *API) server(w http.ResponseWriter, r *http.Request) bool {
	token := bearer(r)
	hash := sha256.Sum256([]byte(token))
	if token == "" || subtle.ConstantTimeCompare(hash[:], a.serverKey[:]) != 1 {
		writeError(w, ErrUnauthorized)
		return false
	}
	return true
}
func decode(r *http.Request, target any) error {
	mediaType, parameters, mediaErr := mime.ParseMediaType(r.Header.Get("Content-Type"))
	if mediaErr != nil || mediaType != "application/json" || (parameters["charset"] != "" && !strings.EqualFold(parameters["charset"], "utf-8")) {
		return ErrInvalid
	}
	data, err := io.ReadAll(r.Body)
	if err != nil {
		return ErrInvalid
	}
	if len(bytes.TrimSpace(data)) == 0 {
		data = []byte(`{}`)
	}
	if trimmed := bytes.TrimSpace(data); len(trimmed) == 0 || trimmed[0] != '{' {
		return ErrInvalid
	}
	if err = validateJSON(data); err != nil {
		return ErrInvalid
	}
	d := json.NewDecoder(bytes.NewReader(data))
	d.DisallowUnknownFields()
	if err = d.Decode(target); err != nil {
		return ErrInvalid
	}
	return nil
}

// Reject duplicate keys, excessive depth and large object/array counts before
// accepting opaque Core state. This validates shape, never combat semantics.
func validateJSON(data []byte) error {
	d := json.NewDecoder(bytes.NewReader(data))
	d.UseNumber()
	count := 0
	var walk func(int) error
	walk = func(depth int) error {
		count++
		if depth > 64 || count > 50000 {
			return ErrInvalid
		}
		token, err := d.Token()
		if err != nil {
			return err
		}
		delim, ok := token.(json.Delim)
		if !ok {
			return nil
		}
		switch delim {
		case '{':
			seen := map[string]bool{}
			for d.More() {
				key, err := d.Token()
				if err != nil {
					return err
				}
				name, ok := key.(string)
				if !ok || seen[name] {
					return ErrInvalid
				}
				seen[name] = true
				if err = walk(depth + 1); err != nil {
					return err
				}
			}
		case '[':
			for d.More() {
				if err = walk(depth + 1); err != nil {
					return err
				}
			}
		default:
			return ErrInvalid
		}
		_, err = d.Token()
		return err
	}
	if err := walk(0); err != nil {
		return err
	}
	if _, err := d.Token(); err != io.EOF {
		return ErrInvalid
	}
	return nil
}
func (a *API) routes() {
	a.mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) { writeJSON(w, 200, map[string]string{"status": "ok"}) })
	a.mux.HandleFunc("GET /readyz", func(w http.ResponseWriter, r *http.Request) {
		if a.draining.Load() {
			writeError(w, ErrDraining)
			return
		}
		if err := a.Store.DB.PingContext(r.Context()); err != nil {
			writeJSON(w, 503, map[string]string{"error": "database_unavailable"})
			return
		}
		writeJSON(w, 200, map[string]string{"status": "ready", "protocolVersion": ProtocolVersion})
	})
	a.mux.HandleFunc("GET /metrics", func(w http.ResponseWriter, r *http.Request) {
		if !a.server(w, r) {
			return
		}
		writeJSON(w, 200, map[string]any{"requests": a.requests.Load(), "failures": a.failures.Load(), "activeRequests": a.active.Load(), "draining": a.draining.Load()})
	})
	a.mux.HandleFunc("POST /v1/server/drain", func(w http.ResponseWriter, r *http.Request) {
		if !a.server(w, r) {
			return
		}
		var body struct {
			Draining bool `json:"draining"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		a.SetDraining(body.Draining)
		writeJSON(w, 200, map[string]bool{"draining": body.Draining})
	})
	a.mux.HandleFunc("POST /v1/sessions/anonymous", func(w http.ResponseWriter, r *http.Request) {
		if a.draining.Load() {
			writeError(w, ErrDraining)
			return
		}
		var body struct{}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.NewSession(r.Context())
		result(w, value, err)
	})
	a.mux.HandleFunc("DELETE /v1/session", func(w http.ResponseWriter, r *http.Request) {
		if _, ok := a.user(w, r); !ok {
			return
		}
		result(w, map[string]bool{"revoked": true}, a.Store.Revoke(r.Context(), bearer(r)))
	})
	a.mux.HandleFunc("GET /v1/characters", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		value, err := a.Store.Characters(r.Context(), account)
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/characters", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		var body struct {
			Name       string `json:"name"`
			Discipline string `json:"discipline"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.CreateCharacter(r.Context(), account, body.Name, body.Discipline)
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/parties", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		var body struct {
			CharacterID string `json:"characterId"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.CreateParty(r.Context(), account, body.CharacterID)
		result(w, value, err)
	})
	a.mux.HandleFunc("GET /v1/parties/{party}", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		value, err := a.Store.GetParty(r.Context(), account, r.PathValue("party"))
		result(w, value, err)
	})
	a.mux.HandleFunc("DELETE /v1/parties/{party}", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		result(w, map[string]bool{"closed": true}, a.Store.CloseParty(r.Context(), account, r.PathValue("party")))
	})
	a.mux.HandleFunc("POST /v1/parties/{party}/invites", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		var body struct{}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.Invite(r.Context(), account, r.PathValue("party"))
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/party-invites/accept", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		var body struct {
			InviteToken string `json:"inviteToken"`
			CharacterID string `json:"characterId"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.AcceptInvite(r.Context(), account, body.InviteToken, body.CharacterID)
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/parties/{party}/ready", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		var body struct {
			Ready           bool   `json:"ready"`
			ProtocolVersion string `json:"protocolVersion"`
			ContentHash     string `json:"contentHash"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.Ready(r.Context(), account, r.PathValue("party"), body.Ready, body.ProtocolVersion, body.ContentHash)
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/parties/{party}/allocate", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		if a.draining.Load() {
			writeError(w, ErrDraining)
			return
		}
		var body struct {
			OperationID string `json:"operationId"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.Allocate(r.Context(), account, r.PathValue("party"), body.OperationID)
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/allocations/{allocation}/join-ticket", func(w http.ResponseWriter, r *http.Request) {
		account, ok := a.user(w, r)
		if !ok {
			return
		}
		var body struct{}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.JoinTicket(r.Context(), account, r.PathValue("allocation"))
		result(w, value, err)
	})
	a.mux.HandleFunc("GET /v1/server/allocations/{allocation}", func(w http.ResponseWriter, r *http.Request) {
		if !a.server(w, r) {
			return
		}
		value, err := a.Store.Allocation(r.Context(), r.PathValue("allocation"))
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/server/allocations/{allocation}/consume-ticket", func(w http.ResponseWriter, r *http.Request) {
		if !a.server(w, r) {
			return
		}
		var body struct {
			Ticket string `json:"ticket"`
		}
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.ConsumeTicket(r.Context(), r.PathValue("allocation"), body.Ticket)
		result(w, value, err)
	})
	a.mux.HandleFunc("POST /v1/server/allocations/{allocation}/checkpoint", func(w http.ResponseWriter, r *http.Request) {
		if !a.server(w, r) {
			return
		}
		var body Checkpoint
		if err := decode(r, &body); err != nil {
			writeError(w, err)
			return
		}
		value, err := a.Store.Checkpoint(r.Context(), r.PathValue("allocation"), body)
		result(w, value, err)
	})
}
