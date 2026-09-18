package control

import (
	"bytes"
	"context"
	"crypto/rand"
	"crypto/sha256"
	"database/sql"
	"embed"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"regexp"
	"strings"
	"time"
	"unicode"

	_ "github.com/jackc/pgx/v5/stdlib"
)

//go:embed migrations/*.sql
var migrations embed.FS
var safeID = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9_.:-]{0,119}$`)
var contentID = regexp.MustCompile(`^[A-Fa-f0-9]{64}$`)

type Store struct {
	DB        *sql.DB
	ServerURL string
}

func Open(ctx context.Context, dsn, serverURL string) (*Store, error) {
	db, err := sql.Open("pgx", dsn)
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(12)
	db.SetMaxIdleConns(6)
	db.SetConnMaxLifetime(30 * time.Minute)
	s := &Store{DB: db, ServerURL: serverURL}
	if err = db.PingContext(ctx); err != nil {
		db.Close()
		return nil, err
	}
	if err = s.Migrate(ctx); err != nil {
		db.Close()
		return nil, err
	}
	return s, nil
}
func (s *Store) Close() error { return s.DB.Close() }
func (s *Store) Migrate(ctx context.Context) error {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if _, err = tx.ExecContext(ctx, "SELECT pg_advisory_xact_lock(711248032)"); err != nil {
		return err
	}
	var exists bool
	if err = tx.QueryRowContext(ctx, "SELECT to_regclass('public.schema_version') IS NOT NULL").Scan(&exists); err != nil {
		return err
	}
	source, err := migrations.ReadFile("migrations/001_initial.sql")
	if err != nil {
		return err
	}
	hash := digest(source)
	if exists {
		var recorded string
		if err = tx.QueryRowContext(ctx, "SELECT checksum FROM schema_version WHERE version=1").Scan(&recorded); err != nil {
			return err
		}
		if recorded != hash {
			return errors.New("migration checksum mismatch")
		}
		var count int
		if err = tx.QueryRowContext(ctx, "SELECT count(*) FROM schema_version").Scan(&count); err != nil {
			return err
		}
		if count != 1 {
			return errors.New("unsupported database schema")
		}
	} else {
		if _, err = tx.ExecContext(ctx, string(source)); err != nil {
			return err
		}
		if _, err = tx.ExecContext(ctx, "INSERT INTO schema_version(version,checksum) VALUES(1,$1)", hash); err != nil {
			return err
		}
	}
	return tx.Commit()
}
func randomToken(n int) (string, error) {
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		return "", err
	}
	return base64.RawURLEncoding.EncodeToString(b), nil
}
func randomID() (string, error) {
	b := make([]byte, 16)
	if _, err := rand.Read(b); err != nil {
		return "", err
	}
	return hex.EncodeToString(b), nil
}
func digest(b []byte) string        { h := sha256.Sum256(b); return hex.EncodeToString(h[:]) }
func tokenHash(token string) string { return digest([]byte(token)) }
func canonicalObject(raw json.RawMessage, limit int) (json.RawMessage, error) {
	if len(raw) == 0 || len(raw) > limit || validateJSON(raw) != nil {
		return nil, ErrInvalid
	}
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	var value map[string]any
	if err := decoder.Decode(&value); err != nil || value == nil {
		return nil, ErrInvalid
	}
	var extra any
	if decoder.Decode(&extra) != io.EOF {
		return nil, ErrInvalid
	}
	encoded, err := json.Marshal(value)
	if err != nil {
		return nil, ErrInvalid
	}
	return encoded, nil
}
func validName(name string) bool {
	if len([]rune(name)) < 1 || len([]rune(name)) > 32 || strings.TrimSpace(name) != name {
		return false
	}
	for _, r := range name {
		if unicode.IsControl(r) {
			return false
		}
	}
	return true
}
func rollback(tx *sql.Tx) { _ = tx.Rollback() }
func notFound(err error) error {
	if errors.Is(err, sql.ErrNoRows) {
		return ErrNotFound
	}
	return err
}

func (s *Store) NewSession(ctx context.Context) (Session, error) {
	account, err := randomID()
	if err != nil {
		return Session{}, err
	}
	token, err := randomToken(32)
	if err != nil {
		return Session{}, err
	}
	expiry := time.Now().UTC().Add(24 * time.Hour)
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Session{}, err
	}
	defer rollback(tx)
	if _, err = tx.ExecContext(ctx, "INSERT INTO accounts(id) VALUES($1)", account); err != nil {
		return Session{}, err
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO sessions(token_hash,account_id,expires_at) VALUES($1,$2,$3)", tokenHash(token), account, expiry); err != nil {
		return Session{}, err
	}
	if err = tx.Commit(); err != nil {
		return Session{}, err
	}
	return Session{account, token, expiry}, nil
}
func (s *Store) Authenticate(ctx context.Context, token string) (string, error) {
	if len(token) != 43 {
		return "", ErrUnauthorized
	}
	var account string
	err := s.DB.QueryRowContext(ctx, "SELECT account_id FROM sessions WHERE token_hash=$1 AND expires_at>now() AND NOT revoked", tokenHash(token)).Scan(&account)
	if errors.Is(err, sql.ErrNoRows) {
		return "", ErrUnauthorized
	}
	return account, err
}
func (s *Store) Revoke(ctx context.Context, token string) error {
	_, err := s.DB.ExecContext(ctx, "UPDATE sessions SET revoked=true WHERE token_hash=$1", tokenHash(token))
	return err
}
func (s *Store) CreateCharacter(ctx context.Context, account, name, discipline string) (Character, error) {
	if !validName(name) || discipline != "Vanguard" {
		return Character{}, ErrInvalid
	}
	id, err := randomID()
	if err != nil {
		return Character{}, err
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Character{}, err
	}
	defer rollback(tx)
	var locked string
	if err = tx.QueryRowContext(ctx, "SELECT id FROM accounts WHERE id=$1 FOR UPDATE", account).Scan(&locked); err != nil {
		return Character{}, notFound(err)
	}
	var count int
	if err = tx.QueryRowContext(ctx, "SELECT count(*) FROM characters WHERE account_id=$1", account).Scan(&count); err != nil {
		return Character{}, err
	}
	if count >= 8 {
		return Character{}, ErrConflict
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO characters(id,account_id,name,discipline) VALUES($1,$2,$3,$4)", id, account, name, discipline); err != nil {
		return Character{}, err
	}
	if err = tx.Commit(); err != nil {
		return Character{}, err
	}
	return Character{id, account, name, discipline, 0, json.RawMessage(`{}`)}, nil
}
func (s *Store) Characters(ctx context.Context, account string) ([]Character, error) {
	rows, err := s.DB.QueryContext(ctx, "SELECT id,account_id,name,discipline,revision,state FROM characters WHERE account_id=$1 ORDER BY created_at,id", account)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := []Character{}
	for rows.Next() {
		var c Character
		if err = rows.Scan(&c.ID, &c.AccountID, &c.Name, &c.Discipline, &c.Revision, &c.State); err != nil {
			return nil, err
		}
		result = append(result, c)
	}
	return result, rows.Err()
}
func (s *Store) owned(ctx context.Context, tx *sql.Tx, account, character string) error {
	var id string
	if err := tx.QueryRowContext(ctx, "SELECT id FROM accounts WHERE id=$1 FOR UPDATE", account).Scan(&id); err != nil {
		return notFound(err)
	}
	return notFound(tx.QueryRowContext(ctx, "SELECT id FROM characters WHERE id=$1 AND account_id=$2 FOR UPDATE", character, account).Scan(&id))
}
func (s *Store) member(ctx context.Context, tx *sql.Tx, account, party string, leader bool) (Party, error) {
	var p Party
	err := tx.QueryRowContext(ctx, "SELECT id,leader_account_id,status FROM parties WHERE id=$1 FOR UPDATE", party).Scan(&p.ID, &p.LeaderAccountID, &p.Status)
	if err != nil {
		return p, notFound(err)
	}
	if leader && p.LeaderAccountID != account {
		return p, ErrNotFound
	}
	var found bool
	if err = tx.QueryRowContext(ctx, "SELECT EXISTS(SELECT 1 FROM party_members WHERE party_id=$1 AND account_id=$2)", party, account).Scan(&found); err != nil {
		return p, err
	}
	if !found {
		return p, ErrNotFound
	}
	return p, nil
}
func players(ctx context.Context, tx *sql.Tx, party string) ([]Player, error) {
	rows, err := tx.QueryContext(ctx, "SELECT slot,character_id,account_id,ready,protocol_version,content_hash FROM party_members WHERE party_id=$1 ORDER BY slot", party)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := []Player{}
	for rows.Next() {
		var p Player
		if err = rows.Scan(&p.Slot, &p.CharacterID, &p.AccountID, &p.Ready, &p.ProtocolVersion, &p.ContentHash); err != nil {
			return nil, err
		}
		result = append(result, p)
	}
	return result, rows.Err()
}
func (s *Store) CreateParty(ctx context.Context, account, character string) (Party, error) {
	id, err := randomID()
	if err != nil {
		return Party{}, err
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Party{}, err
	}
	defer rollback(tx)
	if err = s.owned(ctx, tx, account, character); err != nil {
		return Party{}, err
	}
	var exists bool
	if err = tx.QueryRowContext(ctx, "SELECT EXISTS(SELECT 1 FROM party_members m JOIN parties p ON p.id=m.party_id WHERE m.account_id=$1 AND p.status!='closed')", account).Scan(&exists); err != nil {
		return Party{}, err
	}
	if exists {
		return Party{}, ErrConflict
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO parties(id,leader_account_id) VALUES($1,$2)", id, account); err != nil {
		return Party{}, err
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO party_members(party_id,account_id,character_id,slot) VALUES($1,$2,$3,1)", id, account, character); err != nil {
		return Party{}, err
	}
	if err = tx.Commit(); err != nil {
		return Party{}, err
	}
	return Party{id, account, "open", []Player{{Slot: 1, CharacterID: character, AccountID: account}}}, nil
}
func (s *Store) GetParty(ctx context.Context, account, id string) (Party, error) {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Party{}, err
	}
	defer rollback(tx)
	p, err := s.member(ctx, tx, account, id, false)
	if err != nil {
		return p, err
	}
	p.Players, err = players(ctx, tx, id)
	if err != nil {
		return p, err
	}
	return p, tx.Commit()
}
func (s *Store) Invite(ctx context.Context, account, party string) (Invite, error) {
	token, err := randomToken(32)
	if err != nil {
		return Invite{}, err
	}
	expiry := time.Now().UTC().Add(10 * time.Minute)
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Invite{}, err
	}
	defer rollback(tx)
	p, err := s.member(ctx, tx, account, party, true)
	if err != nil {
		return Invite{}, err
	}
	if p.Status != "open" {
		return Invite{}, ErrConflict
	}
	// One live invitation per party; replacing it invalidates the previous opaque token.
	if _, err = tx.ExecContext(ctx, "UPDATE party_invites SET consumed=true WHERE party_id=$1", party); err != nil {
		return Invite{}, err
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO party_invites(token_hash,party_id,expires_at) VALUES($1,$2,$3)", tokenHash(token), party, expiry); err != nil {
		return Invite{}, err
	}
	if err = tx.Commit(); err != nil {
		return Invite{}, err
	}
	return Invite{token, expiry}, nil
}
func (s *Store) AcceptInvite(ctx context.Context, account, token, character string) (Party, error) {
	if len(token) != 43 {
		return Party{}, ErrInvalid
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Party{}, err
	}
	defer rollback(tx)
	// Read the party, then lock party before invite everywhere to keep lock ordering stable.
	var party string
	if err = tx.QueryRowContext(ctx, "SELECT party_id FROM party_invites WHERE token_hash=$1", tokenHash(token)).Scan(&party); err != nil {
		return Party{}, notFound(err)
	}
	var p Party
	if err = tx.QueryRowContext(ctx, "SELECT id,leader_account_id,status FROM parties WHERE id=$1 FOR UPDATE", party).Scan(&p.ID, &p.LeaderAccountID, &p.Status); err != nil {
		return p, notFound(err)
	}
	if p.Status != "open" {
		return p, ErrConflict
	}
	var valid bool
	if err = tx.QueryRowContext(ctx, "SELECT NOT consumed AND expires_at>now() FROM party_invites WHERE token_hash=$1 FOR UPDATE", tokenHash(token)).Scan(&valid); err != nil {
		return p, notFound(err)
	}
	if !valid {
		return p, ErrConflict
	}
	if err = s.owned(ctx, tx, account, character); err != nil {
		return p, err
	}
	var exists bool
	if err = tx.QueryRowContext(ctx, "SELECT EXISTS(SELECT 1 FROM party_members m JOIN parties p ON p.id=m.party_id WHERE m.account_id=$1 AND p.status!='closed')", account).Scan(&exists); err != nil {
		return p, err
	}
	if exists {
		return p, ErrConflict
	}
	current, err := players(ctx, tx, party)
	if err != nil {
		return p, err
	}
	if len(current) != 1 || current[0].Slot != 1 {
		return p, ErrConflict
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO party_members(party_id,account_id,character_id,slot) VALUES($1,$2,$3,2)", party, account, character); err != nil {
		return p, err
	}
	if _, err = tx.ExecContext(ctx, "UPDATE party_invites SET consumed=true WHERE token_hash=$1", tokenHash(token)); err != nil {
		return p, err
	}
	p.Players, err = players(ctx, tx, party)
	if err != nil {
		return p, err
	}
	return p, tx.Commit()
}
func (s *Store) Ready(ctx context.Context, account, party string, ready bool, protocol, content string) (Party, error) {
	if ready && (protocol != ProtocolVersion || !contentID.MatchString(content)) {
		return Party{}, ErrInvalid
	}
	if !ready {
		protocol = ""
		content = ""
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Party{}, err
	}
	defer rollback(tx)
	p, err := s.member(ctx, tx, account, party, false)
	if err != nil {
		return p, err
	}
	if p.Status != "open" {
		return p, ErrConflict
	}
	if _, err = tx.ExecContext(ctx, "UPDATE party_members SET ready=$1,protocol_version=$2,content_hash=$3 WHERE party_id=$4 AND account_id=$5", ready, protocol, strings.ToUpper(content), party, account); err != nil {
		return p, err
	}
	p.Players, err = players(ctx, tx, party)
	if err != nil {
		return p, err
	}
	return p, tx.Commit()
}
func (s *Store) CloseParty(ctx context.Context, account, party string) error {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer rollback(tx)
	p, err := s.member(ctx, tx, account, party, true)
	if err != nil {
		return err
	}
	if p.Status == "allocated" {
		return ErrConflict
	}
	if _, err = tx.ExecContext(ctx, "UPDATE parties SET status='closed' WHERE id=$1", party); err != nil {
		return err
	}
	return tx.Commit()
}

func storeResponse(value any) ([]byte, error) {
	b, err := json.Marshal(value)
	if err != nil {
		return nil, fmt.Errorf("encode response: %w", err)
	}
	return b, nil
}
