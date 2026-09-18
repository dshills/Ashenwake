package control

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"sort"
	"strings"
	"time"
)

func (s *Store) Allocate(ctx context.Context, account, party, operation string) (Allocation, error) {
	if !safeID.MatchString(operation) {
		return Allocation{}, ErrInvalid
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Allocation{}, err
	}
	defer rollback(tx)
	p, err := s.member(ctx, tx, account, party, true)
	if err != nil {
		return Allocation{}, err
	}
	payload := digest([]byte(party + "\n" + account + "\n" + operation))
	var previousHash string
	var previous []byte
	err = tx.QueryRowContext(ctx, "SELECT payload_hash,response FROM allocate_operations WHERE party_id=$1 AND operation_id=$2", party, operation).Scan(&previousHash, &previous)
	if err == nil {
		if previousHash != payload {
			return Allocation{}, ErrConflict
		}
		var a Allocation
		if err = json.Unmarshal(previous, &a); err != nil {
			return a, err
		}
		return a, tx.Commit()
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return Allocation{}, err
	}
	if p.Status != "open" {
		return Allocation{}, ErrConflict
	}
	members, err := players(ctx, tx, party)
	if err != nil {
		return Allocation{}, err
	}
	if len(members) != 2 || !members[0].Ready || !members[1].Ready || members[0].ProtocolVersion != ProtocolVersion || members[1].ProtocolVersion != ProtocolVersion || members[0].ContentHash != members[1].ContentHash {
		return Allocation{}, ErrConflict
	}
	id, err := randomID()
	if err != nil {
		return Allocation{}, err
	}
	a := Allocation{ID: id, PartyID: party, ServerURL: strings.ReplaceAll(s.ServerURL, "{allocationId}", id), ProtocolVersion: ProtocolVersion, ContentHash: members[0].ContentHash, Players: members, Snapshot: json.RawMessage(`{}`)}
	if _, err = tx.ExecContext(ctx, "INSERT INTO allocations(id,party_id,protocol_version,content_hash,server_url) VALUES($1,$2,$3,$4,$5)", id, party, a.ProtocolVersion, a.ContentHash, a.ServerURL); err != nil {
		return a, err
	}
	if _, err = tx.ExecContext(ctx, "UPDATE parties SET status='allocated' WHERE id=$1", party); err != nil {
		return a, err
	}
	encoded, err := storeResponse(a)
	if err != nil {
		return a, err
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO allocate_operations(party_id,operation_id,payload_hash,response) VALUES($1,$2,$3,$4)", party, operation, payload, encoded); err != nil {
		return a, err
	}
	return a, tx.Commit()
}
func allocation(ctx context.Context, tx *sql.Tx, id string) (Allocation, error) {
	var a Allocation
	err := tx.QueryRowContext(ctx, "SELECT id,party_id,revision,server_url,protocol_version,content_hash,snapshot FROM allocations WHERE id=$1 FOR UPDATE", id).Scan(&a.ID, &a.PartyID, &a.Revision, &a.ServerURL, &a.ProtocolVersion, &a.ContentHash, &a.Snapshot)
	if err != nil {
		return a, notFound(err)
	}
	a.Players, err = players(ctx, tx, a.PartyID)
	return a, err
}
func (s *Store) Allocation(ctx context.Context, id string) (Allocation, error) {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Allocation{}, err
	}
	defer rollback(tx)
	a, err := allocation(ctx, tx, id)
	if err != nil {
		return a, err
	}
	for _, p := range a.Players {
		var c Character
		err = tx.QueryRowContext(ctx, "SELECT id,account_id,name,discipline,revision,state FROM characters WHERE id=$1", p.CharacterID).Scan(&c.ID, &c.AccountID, &c.Name, &c.Discipline, &c.Revision, &c.State)
		if err != nil {
			return a, err
		}
		a.Characters = append(a.Characters, c)
	}
	return a, tx.Commit()
}
func (s *Store) JoinTicket(ctx context.Context, account, id string) (Ticket, error) {
	token, err := randomToken(32)
	if err != nil {
		return Ticket{}, err
	}
	expiry := time.Now().UTC().Add(30 * time.Second)
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Ticket{}, err
	}
	defer rollback(tx)
	a, err := allocation(ctx, tx, id)
	if err != nil {
		return Ticket{}, err
	}
	found := false
	for _, p := range a.Players {
		found = found || p.AccountID == account
	}
	if !found {
		return Ticket{}, ErrNotFound
	}
	// Reconnect gets a fresh ticket; only the most recently issued ticket can be used.
	if _, err = tx.ExecContext(ctx, "UPDATE join_tickets SET consumed=true WHERE allocation_id=$1 AND account_id=$2", id, account); err != nil {
		return Ticket{}, err
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO join_tickets(token_hash,allocation_id,account_id,expires_at) VALUES($1,$2,$3,$4)", tokenHash(token), id, account, expiry); err != nil {
		return Ticket{}, err
	}
	return Ticket{token, expiry}, tx.Commit()
}
func (s *Store) ConsumeTicket(ctx context.Context, id, token string) (Join, error) {
	if len(token) != 43 {
		return Join{}, ErrInvalid
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Join{}, err
	}
	defer rollback(tx)
	a, err := allocation(ctx, tx, id)
	if err != nil {
		return Join{}, err
	}
	var account string
	err = tx.QueryRowContext(ctx, "SELECT account_id FROM join_tickets WHERE token_hash=$1 AND allocation_id=$2 AND expires_at>now() AND NOT consumed FOR UPDATE", tokenHash(token), id).Scan(&account)
	if err != nil {
		return Join{}, notFound(err)
	}
	var j Join
	for _, p := range a.Players {
		if p.AccountID == account {
			j = Join{AllocationID: id, Slot: p.Slot, CharacterID: p.CharacterID, AccountID: account}
			break
		}
	}
	if j.Slot == 0 {
		return j, ErrConflict
	}
	if err = tx.QueryRowContext(ctx, "SELECT revision,state FROM characters WHERE id=$1 AND account_id=$2", j.CharacterID, account).Scan(&j.Revision, &j.State); err != nil {
		return j, notFound(err)
	}
	if _, err = tx.ExecContext(ctx, "UPDATE join_tickets SET consumed=true WHERE token_hash=$1", tokenHash(token)); err != nil {
		return j, err
	}
	return j, tx.Commit()
}
func validateCheckpoint(c Checkpoint) (Checkpoint, string, error) {
	c.Characters = append([]CharacterWrite(nil), c.Characters...)
	c.Rewards = append([]Reward(nil), c.Rewards...)
	if !safeID.MatchString(c.OperationID) || c.ExpectedRevision < 0 || c.ExpectedRevision >= 1<<53 || len(c.Characters) != 2 || len(c.Rewards) > 32 {
		return c, "", ErrInvalid
	}
	var err error
	c.Snapshot, err = canonicalObject(c.Snapshot, MaxSnapshotBytes)
	if err != nil {
		return c, "", err
	}
	characters := map[string]bool{}
	for i := range c.Characters {
		v := &c.Characters[i]
		if !safeID.MatchString(v.CharacterID) || characters[v.CharacterID] || v.ExpectedRevision < 0 || v.ExpectedRevision >= 1<<53 {
			return c, "", ErrInvalid
		}
		characters[v.CharacterID] = true
		v.State, err = canonicalObject(v.State, MaxStateBytes)
		if err != nil {
			return c, "", err
		}
	}
	sort.Slice(c.Characters, func(i, j int) bool { return c.Characters[i].CharacterID < c.Characters[j].CharacterID })
	rewards := map[string]bool{}
	for i := range c.Rewards {
		v := &c.Rewards[i]
		key := v.CharacterID + "/" + v.EventID
		if !characters[v.CharacterID] || !safeID.MatchString(v.EventID) || rewards[key] {
			return c, "", ErrInvalid
		}
		rewards[key] = true
		v.Payload, err = canonicalObject(v.Payload, 16<<10)
		if err != nil {
			return c, "", err
		}
	}
	sort.Slice(c.Rewards, func(i, j int) bool {
		a, b := c.Rewards[i], c.Rewards[j]
		if a.CharacterID == b.CharacterID {
			return a.EventID < b.EventID
		}
		return a.CharacterID < b.CharacterID
	})
	encoded, err := json.Marshal(c)
	if err != nil {
		return c, "", err
	}
	return c, digest(encoded), nil
}
func (s *Store) Checkpoint(ctx context.Context, id string, input Checkpoint) (CheckpointResult, error) {
	c, payload, err := validateCheckpoint(input)
	if err != nil {
		return CheckpointResult{}, err
	}
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return CheckpointResult{}, err
	}
	defer rollback(tx)
	a, err := allocation(ctx, tx, id)
	if err != nil {
		return CheckpointResult{}, err
	}
	var previousHash string
	var previous []byte
	err = tx.QueryRowContext(ctx, "SELECT payload_hash,response FROM checkpoints WHERE allocation_id=$1 AND operation_id=$2", id, c.OperationID).Scan(&previousHash, &previous)
	if err == nil {
		if previousHash != payload {
			return CheckpointResult{}, ErrConflict
		}
		var r CheckpointResult
		if err = json.Unmarshal(previous, &r); err != nil {
			return r, err
		}
		return r, tx.Commit()
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return CheckpointResult{}, err
	}
	if a.Revision != c.ExpectedRevision {
		return CheckpointResult{}, ErrConflict
	}
	allowed := map[string]string{}
	for _, p := range a.Players {
		allowed[p.CharacterID] = p.AccountID
	}
	if len(allowed) != 2 {
		return CheckpointResult{}, ErrConflict
	}
	result := CheckpointResult{Revision: a.Revision + 1, Characters: []CharacterRevision{}}
	// Validate both personal revisions and ownership before any write. Row locks keep
	// concurrent checkpoint/retry decisions in one order for the whole allocation.
	for _, v := range c.Characters {
		account, ok := allowed[v.CharacterID]
		if !ok {
			return result, ErrConflict
		}
		var revision int64
		if err = tx.QueryRowContext(ctx, "SELECT revision FROM characters WHERE id=$1 AND account_id=$2 FOR UPDATE", v.CharacterID, account).Scan(&revision); err != nil {
			return result, notFound(err)
		}
		if revision != v.ExpectedRevision {
			return result, ErrConflict
		}
	}
	for _, r := range c.Rewards {
		var exists bool
		if err = tx.QueryRowContext(ctx, "SELECT EXISTS(SELECT 1 FROM personal_rewards WHERE allocation_id=$1 AND character_id=$2 AND event_id=$3)", id, r.CharacterID, r.EventID).Scan(&exists); err != nil {
			return result, err
		}
		if exists {
			return result, ErrConflict
		}
	}
	for _, v := range c.Characters {
		if _, err = tx.ExecContext(ctx, "UPDATE characters SET revision=revision+1,state=$1 WHERE id=$2", []byte(v.State), v.CharacterID); err != nil {
			return result, err
		}
		result.Characters = append(result.Characters, CharacterRevision{v.CharacterID, v.ExpectedRevision + 1})
	}
	for _, r := range c.Rewards {
		if _, err = tx.ExecContext(ctx, "INSERT INTO personal_rewards(allocation_id,character_id,event_id,payload_hash,payload,operation_id) VALUES($1,$2,$3,$4,$5,$6)", id, r.CharacterID, r.EventID, digest(r.Payload), []byte(r.Payload), c.OperationID); err != nil {
			return result, err
		}
	}
	if _, err = tx.ExecContext(ctx, "UPDATE allocations SET revision=revision+1,snapshot=$1 WHERE id=$2", []byte(c.Snapshot), id); err != nil {
		return result, err
	}
	encoded, err := storeResponse(result)
	if err != nil {
		return result, err
	}
	if _, err = tx.ExecContext(ctx, "INSERT INTO checkpoints(allocation_id,operation_id,payload_hash,response) VALUES($1,$2,$3,$4)", id, c.OperationID, payload, encoded); err != nil {
		return result, err
	}
	return result, tx.Commit()
}
