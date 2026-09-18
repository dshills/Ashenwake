CREATE TABLE schema_version (version integer PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE accounts (id text PRIMARY KEY, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE sessions (token_hash text PRIMARY KEY, account_id text NOT NULL REFERENCES accounts(id), expires_at timestamptz NOT NULL, revoked boolean NOT NULL DEFAULT false);
CREATE INDEX sessions_account ON sessions(account_id);
CREATE TABLE characters (
 id text PRIMARY KEY, account_id text NOT NULL REFERENCES accounts(id), name text NOT NULL CHECK (length(name) BETWEEN 1 AND 32),
 discipline text NOT NULL CHECK (discipline = 'Vanguard'), revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
 state jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(state) = 'object'), created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX characters_account ON characters(account_id);
CREATE TABLE parties (id text PRIMARY KEY, leader_account_id text NOT NULL REFERENCES accounts(id), status text NOT NULL DEFAULT 'open' CHECK (status IN ('open','allocated','closed')), created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE party_members (
 party_id text NOT NULL REFERENCES parties(id), account_id text NOT NULL REFERENCES accounts(id), character_id text NOT NULL REFERENCES characters(id),
 slot integer NOT NULL CHECK (slot IN (1,2)), ready boolean NOT NULL DEFAULT false, protocol_version text NOT NULL DEFAULT '', content_hash text NOT NULL DEFAULT '',
 PRIMARY KEY (party_id,account_id), UNIQUE (party_id,slot), UNIQUE (party_id,character_id));
CREATE TABLE party_invites (token_hash text PRIMARY KEY, party_id text NOT NULL REFERENCES parties(id), expires_at timestamptz NOT NULL, consumed boolean NOT NULL DEFAULT false);
CREATE TABLE allocations (
 id text PRIMARY KEY, party_id text NOT NULL UNIQUE REFERENCES parties(id), revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
 protocol_version text NOT NULL, content_hash text NOT NULL, server_url text NOT NULL,
 snapshot jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(snapshot) = 'object'), created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE allocate_operations (party_id text NOT NULL REFERENCES parties(id), operation_id text NOT NULL, payload_hash text NOT NULL, response jsonb NOT NULL, PRIMARY KEY(party_id,operation_id));
CREATE TABLE join_tickets (token_hash text PRIMARY KEY, allocation_id text NOT NULL REFERENCES allocations(id), account_id text NOT NULL REFERENCES accounts(id), expires_at timestamptz NOT NULL, consumed boolean NOT NULL DEFAULT false);
CREATE TABLE checkpoints (allocation_id text NOT NULL REFERENCES allocations(id), operation_id text NOT NULL, payload_hash text NOT NULL, response jsonb NOT NULL, PRIMARY KEY(allocation_id,operation_id));
CREATE TABLE personal_rewards (
 allocation_id text NOT NULL REFERENCES allocations(id), character_id text NOT NULL REFERENCES characters(id), event_id text NOT NULL, payload_hash text NOT NULL, payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
 operation_id text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(allocation_id,character_id,event_id));
