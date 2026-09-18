package control

import (
	"bytes"
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
)

const testKey = "private-local-test-server-key-32-bytes-minimum"

var ctx = context.Background()

func require(t *testing.T, err error) {
	t.Helper()
	if err != nil {
		t.Fatal(err)
	}
}
func assertError(t *testing.T, actual, expected error) {
	t.Helper()
	if !errors.Is(actual, expected) {
		t.Fatalf("want %v, got %v", expected, actual)
	}
}
func account(t *testing.T, s *Store, name string) (Session, Character) {
	t.Helper()
	session, err := s.NewSession(ctx)
	require(t, err)
	character, err := s.CreateCharacter(ctx, session.AccountID, name, "Vanguard")
	require(t, err)
	return session, character
}
func readyAllocation(t *testing.T, s *Store) (Allocation, Session, Session) {
	t.Helper()
	first, c1 := account(t, s, "First")
	second, c2 := account(t, s, "Second")
	party, err := s.CreateParty(ctx, first.AccountID, c1.ID)
	require(t, err)
	invite, err := s.Invite(ctx, first.AccountID, party.ID)
	require(t, err)
	_, err = s.AcceptInvite(ctx, second.AccountID, invite.InviteToken, c2.ID)
	require(t, err)
	for _, owner := range []Session{first, second} {
		_, err = s.Ready(ctx, owner.AccountID, party.ID, true, ProtocolVersion, strings.Repeat("A", 64))
		require(t, err)
	}
	a, err := s.Allocate(ctx, first.AccountID, party.ID, "allocate.once")
	require(t, err)
	return a, first, second
}
func checkpoint(a Allocation) Checkpoint {
	return Checkpoint{OperationID: "checkpoint.1", ExpectedRevision: 0, Snapshot: json.RawMessage(`{"tick":30,"rulesVersion":"coop.1"}`), Characters: []CharacterWrite{{a.Players[0].CharacterID, 0, json.RawMessage(`{"level":1,"materials":15}`)}, {a.Players[1].CharacterID, 0, json.RawMessage(`{"level":1,"materials":15}`)}}, Rewards: []Reward{{a.Players[0].CharacterID, "encounter.1", json.RawMessage(`{"materials":15}`)}, {a.Players[1].CharacterID, "encounter.1", json.RawMessage(`{"materials":15}`)}}}
}
func TestSessionsCharactersAndOwnership(t *testing.T) {
	s, _ := testStore(t)
	one, c := account(t, s, "Ashbearer")
	two, _ := account(t, s, "Other")
	got, err := s.Authenticate(ctx, one.SessionToken)
	require(t, err)
	if got != one.AccountID {
		t.Fatal("wrong session owner")
	}
	_, err = s.CreateCharacter(ctx, one.AccountID, "No import", "Arcanist")
	assertError(t, err, ErrInvalid)
	_, err = s.CreateParty(ctx, two.AccountID, c.ID)
	assertError(t, err, ErrNotFound)
	var persisted string
	require(t, s.DB.QueryRow("SELECT token_hash FROM sessions WHERE account_id=$1", one.AccountID).Scan(&persisted))
	if strings.Contains(persisted, one.SessionToken) || len(persisted) != 64 {
		t.Fatal("session must store only hash")
	}
	require(t, s.Revoke(ctx, one.SessionToken))
	_, err = s.Authenticate(ctx, one.SessionToken)
	assertError(t, err, ErrUnauthorized)
	_, err = s.DB.Exec("UPDATE sessions SET expires_at=now()-interval '1 second' WHERE account_id=$1", two.AccountID)
	require(t, err)
	_, err = s.Authenticate(ctx, two.SessionToken)
	assertError(t, err, ErrUnauthorized)
}
func TestPartyReadyCompatibilityStableSlotsAndOneUseTickets(t *testing.T) {
	s, _ := testStore(t)
	one, c1 := account(t, s, "One")
	two, c2 := account(t, s, "Two")
	outsider, _ := account(t, s, "Outsider")
	p, err := s.CreateParty(ctx, one.AccountID, c1.ID)
	require(t, err)
	_, err = s.Invite(ctx, two.AccountID, p.ID)
	assertError(t, err, ErrNotFound)
	invite, err := s.Invite(ctx, one.AccountID, p.ID)
	require(t, err)
	p, err = s.AcceptInvite(ctx, two.AccountID, invite.InviteToken, c2.ID)
	require(t, err)
	if p.Players[0].CharacterID != c1.ID || p.Players[1].CharacterID != c2.ID || p.Players[1].Slot != 2 {
		t.Fatal("slots not stable")
	}
	_, err = s.AcceptInvite(ctx, two.AccountID, invite.InviteToken, c2.ID)
	assertError(t, err, ErrConflict)
	_, err = s.Allocate(ctx, one.AccountID, p.ID, "not.ready")
	assertError(t, err, ErrConflict)
	_, err = s.Ready(ctx, one.AccountID, p.ID, true, "coop.1", strings.Repeat("A", 64))
	assertError(t, err, ErrInvalid)
	_, err = s.Ready(ctx, one.AccountID, p.ID, true, ProtocolVersion, strings.Repeat("A", 64))
	require(t, err)
	_, err = s.Ready(ctx, two.AccountID, p.ID, true, ProtocolVersion, strings.Repeat("B", 64))
	require(t, err)
	_, err = s.Allocate(ctx, one.AccountID, p.ID, "wrong.hash")
	assertError(t, err, ErrConflict)
	_, err = s.Ready(ctx, two.AccountID, p.ID, true, ProtocolVersion, strings.Repeat("A", 64))
	require(t, err)
	a, err := s.Allocate(ctx, one.AccountID, p.ID, "once")
	require(t, err)
	again, err := s.Allocate(ctx, one.AccountID, p.ID, "once")
	require(t, err)
	if a.ID != again.ID {
		t.Fatal("allocation duplicated")
	}
	_, err = s.JoinTicket(ctx, outsider.AccountID, a.ID)
	assertError(t, err, ErrNotFound)
	ticket, err := s.JoinTicket(ctx, two.AccountID, a.ID)
	require(t, err)
	j, err := s.ConsumeTicket(ctx, a.ID, ticket.Ticket)
	require(t, err)
	if j.Slot != 2 || j.AccountID != two.AccountID || j.AllocationID != a.ID {
		t.Fatal("ticket identity mismatch")
	}
	_, err = s.ConsumeTicket(ctx, a.ID, ticket.Ticket)
	assertError(t, err, ErrNotFound)
	ticket, err = s.JoinTicket(ctx, two.AccountID, a.ID)
	require(t, err)
	_, err = s.DB.Exec("UPDATE join_tickets SET expires_at=now()-interval '1 second' WHERE token_hash=$1", tokenHash(ticket.Ticket))
	require(t, err)
	_, err = s.ConsumeTicket(ctx, a.ID, ticket.Ticket)
	assertError(t, err, ErrNotFound)
}
func TestConcurrentGuestAdmissionAndAccountMembership(t *testing.T) {
	s, _ := testStore(t)
	leader, c := account(t, s, "Leader")
	p, err := s.CreateParty(ctx, leader.AccountID, c.ID)
	require(t, err)
	invite, err := s.Invite(ctx, leader.AccountID, p.ID)
	require(t, err)
	one, c1 := account(t, s, "Guest1")
	two, c2 := account(t, s, "Guest2")
	var wg sync.WaitGroup
	results := make(chan error, 2)
	for _, pair := range []struct{ a, c string }{{one.AccountID, c1.ID}, {two.AccountID, c2.ID}} {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_, err := s.AcceptInvite(ctx, pair.a, invite.InviteToken, pair.c)
			results <- err
		}()
	}
	wg.Wait()
	close(results)
	success := 0
	for err := range results {
		if err == nil {
			success++
		} else if !errors.Is(err, ErrConflict) {
			t.Fatal(err)
		}
	}
	if success != 1 {
		t.Fatal("party admitted more than two players")
	}
	owner, first := account(t, s, "Same account")
	second, err := s.CreateCharacter(ctx, owner.AccountID, "Second character", "Vanguard")
	require(t, err)
	results = make(chan error, 2)
	for _, id := range []string{first.ID, second.ID} {
		wg.Add(1)
		go func() { defer wg.Done(); _, err := s.CreateParty(ctx, owner.AccountID, id); results <- err }()
	}
	wg.Wait()
	close(results)
	success = 0
	for err := range results {
		if err == nil {
			success++
		} else if !errors.Is(err, ErrConflict) {
			t.Fatal(err)
		}
	}
	if success != 1 {
		t.Fatal("account entered concurrent parties")
	}
}
func TestCheckpointAtomicityIdempotencyAndPersonalRewards(t *testing.T) {
	s, _ := testStore(t)
	a, _, _ := readyAllocation(t, s)
	request := checkpoint(a)
	broken := checkpoint(a)
	broken.Characters[1].ExpectedRevision = 9
	_, err := s.Checkpoint(ctx, a.ID, broken)
	assertError(t, err, ErrConflict)
	current, err := s.Allocation(ctx, a.ID)
	require(t, err)
	if current.Revision != 0 || current.Characters[0].Revision != 0 || current.Characters[1].Revision != 0 {
		t.Fatal("partial character write")
	}
	result, err := s.Checkpoint(ctx, a.ID, request)
	require(t, err)
	repeat, err := s.Checkpoint(ctx, a.ID, request)
	require(t, err)
	left, _ := json.Marshal(result)
	right, _ := json.Marshal(repeat)
	if !bytes.Equal(left, right) {
		t.Fatal("idempotent response changed")
	}
	changed := checkpoint(a)
	changed.Snapshot = json.RawMessage(`{"tick":31}`)
	_, err = s.Checkpoint(ctx, a.ID, changed)
	assertError(t, err, ErrConflict)
	duplicateReward := checkpoint(a)
	duplicateReward.OperationID = "checkpoint.2"
	duplicateReward.ExpectedRevision = 1
	for i := range duplicateReward.Characters {
		duplicateReward.Characters[i].ExpectedRevision = 1
	}
	_, err = s.Checkpoint(ctx, a.ID, duplicateReward)
	assertError(t, err, ErrConflict)
	current, err = s.Allocation(ctx, a.ID)
	require(t, err)
	if current.Revision != 1 || current.Characters[0].Revision != 1 || current.Characters[1].Revision != 1 {
		t.Fatal("duplicate reward changed revisions")
	}
	var count int
	require(t, s.DB.QueryRow("SELECT count(*) FROM personal_rewards WHERE allocation_id=$1", a.ID).Scan(&count))
	if count != 2 {
		t.Fatalf("want exactly 2 personal receipts, got %d", count)
	}
	rogue, character := account(t, s, "Unrelated")
	_ = rogue
	foreign := checkpoint(a)
	foreign.OperationID = "foreign"
	foreign.Characters[0].CharacterID = character.ID
	foreign.Rewards = nil
	foreign.ExpectedRevision = 1
	for i := range foreign.Characters {
		foreign.Characters[i].ExpectedRevision = 1
	}
	_, err = s.Checkpoint(ctx, a.ID, foreign)
	assertError(t, err, ErrConflict)
}
func TestConcurrentCheckpointCompareAndSwap(t *testing.T) {
	s, _ := testStore(t)
	a, _, _ := readyAllocation(t, s)
	var wg sync.WaitGroup
	results := make(chan error, 8)
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			c := checkpoint(a)
			c.OperationID = fmt.Sprintf("checkpoint.%d", i)
			_, err := s.Checkpoint(ctx, a.ID, c)
			results <- err
		}()
	}
	wg.Wait()
	close(results)
	success := 0
	for err := range results {
		if err == nil {
			success++
		} else if !errors.Is(err, ErrConflict) {
			t.Fatal(err)
		}
	}
	if success != 1 {
		t.Fatalf("CAS allowed %d commits", success)
	}
}
func TestConcurrentSameCheckpointReturnsSameCommit(t *testing.T) {
	s, _ := testStore(t)
	a, _, _ := readyAllocation(t, s)
	var wg sync.WaitGroup
	results := make(chan error, 8)
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() { defer wg.Done(); _, err := s.Checkpoint(ctx, a.ID, checkpoint(a)); results <- err }()
	}
	wg.Wait()
	close(results)
	for err := range results {
		require(t, err)
	}
	state, err := s.Allocation(ctx, a.ID)
	require(t, err)
	if state.Revision != 1 {
		t.Fatal("identical concurrent retries duplicated commit")
	}
}
func TestRestartAndBackupRestorePreserveCommittedCheckpoint(t *testing.T) {
	s, database := testStore(t)
	a, one, _ := readyAllocation(t, s)
	expected, err := s.Checkpoint(ctx, a.ID, checkpoint(a))
	require(t, err)
	require(t, s.Close())
	require(t, stopPostgres())
	require(t, startPostgres())
	s, err = Open(ctx, postgresDSN(database), "ws://127.0.0.1:8090/ws")
	require(t, err)
	defer s.Close()
	actual, err := s.Checkpoint(ctx, a.ID, checkpoint(a))
	require(t, err)
	if actual.Revision != expected.Revision {
		t.Fatal("restart lost idempotency")
	}
	_, err = s.Authenticate(ctx, one.SessionToken)
	require(t, err)
	dump := filepath.Join(privatePG.root, "checkpoint.dump")
	out, err := pgCommand("pg_dump", "--dbname", postgresDSN(database), "--format=custom", "--file", dump)
	if err != nil {
		t.Fatalf("dump: %v %s", err, out)
	}
	clone := "restore_" + database
	admin, err := sql.Open("pgx", postgresDSN("postgres"))
	require(t, err)
	_, err = admin.Exec(`CREATE DATABASE "` + clone + `"`)
	admin.Close()
	require(t, err)
	out, err = pgCommand("pg_restore", "--dbname", postgresDSN(clone), "--exit-on-error", dump)
	if err != nil {
		t.Fatalf("restore: %v %s", err, out)
	}
	restored, err := Open(ctx, postgresDSN(clone), "ws://127.0.0.1:8090/ws")
	require(t, err)
	defer restored.Close()
	actual, err = restored.Checkpoint(ctx, a.ID, checkpoint(a))
	require(t, err)
	if actual.Revision != 1 {
		t.Fatal("backup lost checkpoint")
	}
	state, err := restored.Allocation(ctx, a.ID)
	require(t, err)
	if state.Characters[0].Revision != 1 || state.Characters[1].Revision != 1 {
		t.Fatal("backup lost character state")
	}
	require(t, os.Remove(dump))
}
func TestMigrationChecksumAndFutureSchemaFailClosed(t *testing.T) {
	s, _ := testStore(t)
	_, err := s.DB.Exec("UPDATE schema_version SET checksum='changed' WHERE version=1")
	require(t, err)
	if s.Migrate(ctx) == nil {
		t.Fatal("migration drift accepted")
	}
	source, err := migrations.ReadFile("migrations/001_initial.sql")
	require(t, err)
	_, err = s.DB.Exec("UPDATE schema_version SET checksum=$1 WHERE version=1", digest(source))
	require(t, err)
	_, err = s.DB.Exec("INSERT INTO schema_version(version,checksum) VALUES(2,'future')")
	require(t, err)
	if s.Migrate(ctx) == nil {
		t.Fatal("future schema accepted")
	}
}
func call(api *API, method, path, token, body string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, strings.NewReader(body))
	r.Header.Set("Content-Type", "application/json")
	r.RemoteAddr = "127.0.0.1:5678"
	if token != "" {
		r.Header.Set("Authorization", "Bearer "+token)
	}
	w := httptest.NewRecorder()
	api.ServeHTTP(w, r)
	return w
}
func TestHTTPBoundaryAuthorizationBoundsDrainingAndRedactedAudit(t *testing.T) {
	s, _ := testStore(t)
	var logs bytes.Buffer
	api, err := NewAPI(s, testKey, slog.New(slog.NewJSONHandler(&logs, nil)))
	require(t, err)
	session, c := account(t, s, "Unlogged name")
	a, _, _ := readyAllocation(t, s)
	for _, item := range []struct {
		method, path, token, body string
		status                    int
	}{{"GET", "/v1/characters", "", "", 401}, {"GET", "/v1/server/allocations/" + a.ID, session.SessionToken, "", 401}, {"GET", "/v1/characters", testKey, "", 401}, {"POST", "/v1/characters", session.SessionToken, `{"name":"Injected","discipline":"Vanguard","state":{"xp":9000}}`, 400}, {"POST", "/v1/characters", session.SessionToken, `{"name":"One","name":"Two","discipline":"Vanguard"}`, 400}, {"POST", "/v1/characters", session.SessionToken, `{"name":"Other","discipline":"Arcanist"}`, 400}, {"POST", "/v1/characters", session.SessionToken, strings.Repeat("x", MaxBodyBytes+1), 400}, {"GET", "/v1/characters?token=secret", session.SessionToken, "", 400}, {"GET", "/v1/server/allocations/" + a.ID, testKey, "", 200}} {
		response := call(api, item.method, item.path, item.token, item.body)
		if response.Code != item.status {
			t.Fatalf("%s: want %d got %d: %s", item.path, item.status, response.Code, response.Body.String())
		}
	}
	require(t, s.Revoke(ctx, session.SessionToken))
	if call(api, "GET", "/v1/characters", session.SessionToken, "").Code != 401 {
		t.Fatal("revoked HTTP session accepted")
	}
	_ = c
	api.SetDraining(true)
	if call(api, "GET", "/readyz", "", "").Code != 503 {
		t.Fatal("drain readiness")
	}
	if call(api, "POST", "/v1/sessions/anonymous", "", `{}`).Code != 503 {
		t.Fatal("drain admits session")
	}
	encoded, _ := json.Marshal(checkpoint(a))
	if call(api, "POST", "/v1/server/allocations/"+a.ID+"/checkpoint", testKey, string(encoded)).Code != 200 {
		t.Fatal("draining dropped existing checkpoint")
	}
	for _, secret := range []string{testKey, session.SessionToken, "Unlogged name", "materials", "xp", a.ID} {
		if strings.Contains(logs.String(), secret) {
			t.Fatalf("audit contains private data: %s", secret)
		}
	}
	if call(api, "GET", "/metrics", "", "").Code != 401 {
		t.Fatal("public metrics")
	}
	if call(api, "GET", "/metrics", testKey, "").Code != 200 {
		t.Fatal("authorized metrics unavailable")
	}
}
func TestHTTPOpaqueSessionRateLimit(t *testing.T) {
	s, _ := testStore(t)
	api, err := NewAPI(s, testKey, nil)
	require(t, err)
	limited := false
	for i := 0; i < 14; i++ {
		r := call(api, "POST", "/v1/sessions/anonymous", "", `{}`)
		if r.Code == 429 {
			limited = true
		} else if r.Code != 200 {
			t.Fatalf("unexpected %d", r.Code)
		}
	}
	if !limited {
		t.Fatal("anonymous session flood unbounded")
	}
}
func TestOpaqueCheckpointShapeBounds(t *testing.T) {
	s, _ := testStore(t)
	a, _, _ := readyAllocation(t, s)
	for _, raw := range []string{`[]`, `null`, `{"x":1,"x":2}`, `{} trailing`, strings.Repeat(`[`, 65) + `0` + strings.Repeat(`]`, 65), `{"blob":"` + strings.Repeat("x", MaxSnapshotBytes) + `"}`} {
		c := checkpoint(a)
		c.Snapshot = json.RawMessage(raw)
		_, err := s.Checkpoint(ctx, a.ID, c)
		assertError(t, err, ErrInvalid)
	}
	c := checkpoint(a)
	c.Characters[1].CharacterID = c.Characters[0].CharacterID
	_, err := s.Checkpoint(ctx, a.ID, c)
	assertError(t, err, ErrInvalid)
}
func TestLocalHTTPServerRoundTrip(t *testing.T) {
	s, _ := testStore(t)
	api, err := NewAPI(s, testKey, nil)
	require(t, err)
	server := httptest.NewServer(api)
	defer server.Close()
	response, err := http.Post(server.URL+"/v1/sessions/anonymous", "application/json", strings.NewReader(`{}`))
	require(t, err)
	defer response.Body.Close()
	if response.StatusCode != 200 {
		t.Fatal(response.Status)
	}
	var session Session
	require(t, json.NewDecoder(response.Body).Decode(&session))
	if len(session.SessionToken) != 43 {
		t.Fatal("opaque session missing")
	}
}

func TestDatabaseFailureAfterFirstCharacterWriteRollsBackWholeCheckpoint(t *testing.T) {
	s, _ := testStore(t)
	a, _, _ := readyAllocation(t, s)
	request := checkpoint(a)
	// Canonical checkpoint ordering is character ID order. Fail the second UPDATE
	// inside PostgreSQL after the first UPDATE has already run in this transaction.
	second := request.Characters[0].CharacterID
	if request.Characters[1].CharacterID > second {
		second = request.Characters[1].CharacterID
	}
	_, err := s.DB.Exec(`CREATE FUNCTION reject_second_character() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.id = '` + second + `' THEN RAISE EXCEPTION 'test checkpoint interruption'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_checkpoint BEFORE UPDATE ON characters FOR EACH ROW EXECUTE FUNCTION reject_second_character()`)
	require(t, err)
	if _, err = s.Checkpoint(ctx, a.ID, request); err == nil {
		t.Fatal("expected database interruption")
	}
	state, err := s.Allocation(ctx, a.ID)
	require(t, err)
	if state.Revision != 0 || state.Characters[0].Revision != 0 || state.Characters[1].Revision != 0 {
		t.Fatal("transaction left partial character writes")
	}
	var count int
	require(t, s.DB.QueryRow("SELECT count(*) FROM personal_rewards WHERE allocation_id=$1", a.ID).Scan(&count))
	if count != 0 {
		t.Fatal("transaction left reward receipts")
	}
	_, err = s.DB.Exec("DROP TRIGGER fail_checkpoint ON characters")
	require(t, err)
	result, err := s.Checkpoint(ctx, a.ID, request)
	require(t, err)
	if result.Revision != 1 {
		t.Fatal("retry after rollback failed")
	}
}

func TestConfiguredAllocationURLAndExpiredReplacedInvites(t *testing.T) {
	s, _ := testStore(t)
	s.ServerURL = "ws://127.0.0.1:5180/v1/matches/{allocationId}/socket"
	a, _, _ := readyAllocation(t, s)
	if a.ServerURL != "ws://127.0.0.1:5180/v1/matches/"+a.ID+"/socket" {
		t.Fatal("configured allocation URL not bound to allocation")
	}
	leader, c1 := account(t, s, "Leader")
	guest, c2 := account(t, s, "Guest")
	p, err := s.CreateParty(ctx, leader.AccountID, c1.ID)
	require(t, err)
	old, err := s.Invite(ctx, leader.AccountID, p.ID)
	require(t, err)
	fresh, err := s.Invite(ctx, leader.AccountID, p.ID)
	require(t, err)
	_, err = s.AcceptInvite(ctx, guest.AccountID, old.InviteToken, c2.ID)
	assertError(t, err, ErrConflict)
	_, err = s.DB.Exec("UPDATE party_invites SET expires_at=now()-interval '1 second' WHERE token_hash=$1", tokenHash(fresh.InviteToken))
	require(t, err)
	_, err = s.AcceptInvite(ctx, guest.AccountID, fresh.InviteToken, c2.ID)
	assertError(t, err, ErrConflict)
}

func TestDotNetJSONContentTypeAndNullBodyRejection(t *testing.T) {
	s, _ := testStore(t)
	api, err := NewAPI(s, testKey, nil)
	require(t, err)
	r := httptest.NewRequest("POST", "/v1/sessions/anonymous", strings.NewReader(`{}`))
	r.Header.Set("Content-Type", "application/json; charset=utf-8")
	w := httptest.NewRecorder()
	api.ServeHTTP(w, r)
	if w.Code != 200 {
		t.Fatalf(".NET JSON content type rejected: %s", w.Body.String())
	}
	if call(api, "POST", "/v1/sessions/anonymous", "", `null`).Code != 400 {
		t.Fatal("null command body accepted")
	}
}
