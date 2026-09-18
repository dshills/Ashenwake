package control

import (
	"encoding/json"
	"errors"
	"time"
)

const ProtocolVersion = "coop-net.1"
const MaxStateBytes = 256 << 10
const MaxSnapshotBytes = 1 << 20
const MaxBodyBytes = 2 << 20

var ErrUnauthorized = errors.New("unauthorized")
var ErrNotFound = errors.New("not_found")
var ErrConflict = errors.New("conflict")
var ErrInvalid = errors.New("invalid_request")
var ErrDraining = errors.New("draining")

type Session struct {
	AccountID    string    `json:"accountId"`
	SessionToken string    `json:"sessionToken"`
	ExpiresAt    time.Time `json:"expiresAt"`
}
type Character struct {
	ID         string          `json:"id"`
	AccountID  string          `json:"accountId"`
	Name       string          `json:"name"`
	Discipline string          `json:"discipline"`
	Revision   int64           `json:"revision"`
	State      json.RawMessage `json:"state"`
}
type Player struct {
	Slot            int    `json:"slot"`
	CharacterID     string `json:"characterId"`
	AccountID       string `json:"accountId"`
	Ready           bool   `json:"ready"`
	ProtocolVersion string `json:"protocolVersion"`
	ContentHash     string `json:"contentHash"`
}
type Party struct {
	ID              string   `json:"id"`
	LeaderAccountID string   `json:"leaderAccountId"`
	Status          string   `json:"status"`
	Players         []Player `json:"players"`
}
type Invite struct {
	InviteToken string    `json:"inviteToken"`
	ExpiresAt   time.Time `json:"expiresAt"`
}
type Ticket struct {
	Ticket    string    `json:"ticket"`
	ExpiresAt time.Time `json:"expiresAt"`
}
type Allocation struct {
	ID              string          `json:"allocationId"`
	PartyID         string          `json:"partyId"`
	Revision        int64           `json:"revision"`
	ServerURL       string          `json:"serverUrl"`
	ProtocolVersion string          `json:"protocolVersion"`
	ContentHash     string          `json:"contentHash"`
	Players         []Player        `json:"players"`
	Snapshot        json.RawMessage `json:"snapshot"`
	Characters      []Character     `json:"characters,omitempty"`
}
type Join struct {
	AllocationID string          `json:"allocationId"`
	Slot         int             `json:"slot"`
	CharacterID  string          `json:"characterId"`
	AccountID    string          `json:"accountId"`
	Revision     int64           `json:"revision"`
	State        json.RawMessage `json:"state"`
}
type CharacterWrite struct {
	CharacterID      string          `json:"characterId"`
	ExpectedRevision int64           `json:"expectedRevision"`
	State            json.RawMessage `json:"state"`
}
type Reward struct {
	CharacterID string          `json:"characterId"`
	EventID     string          `json:"eventId"`
	Payload     json.RawMessage `json:"payload"`
}
type Checkpoint struct {
	OperationID      string           `json:"operationId"`
	ExpectedRevision int64            `json:"expectedRevision"`
	Snapshot         json.RawMessage  `json:"snapshot"`
	Characters       []CharacterWrite `json:"characters"`
	Rewards          []Reward         `json:"rewards"`
}
type CharacterRevision struct {
	CharacterID string `json:"characterId"`
	Revision    int64  `json:"revision"`
}
type CheckpointResult struct {
	Revision   int64               `json:"revision"`
	Characters []CharacterRevision `json:"characters"`
}
