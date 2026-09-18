# Local online control plane

`services/online` is a Go modular monolith backed by PostgreSQL. It supplies the local two-player co-op prototype with anonymous accounts, short-lived credentials, server-owned Vanguard characters, parties, allocation, and durable checkpoints. The authoritative C# server runs Core combat. The Go service never computes damage, accepts client loot claims, or imports an offline character.

This is a loopback prototype, with one configured C# server and exactly two fixed Vanguard loadouts. It does not provide public deployment, password login, account recovery, external identity integration, paid entitlements, cross-region matchmaking, host failover, or production anti-cheat. An anonymous session is a randomly generated bearer credential; possession is its account provenance. Losing it loses access to that prototype account. The separate server key grants trusted checkpoint access and must remain in server processes.

## Run and verify

Requirements: Go 1.27.1 and PostgreSQL command-line binaries. The repository pins `github.com/jackc/pgx/v5` v5.11.0 and its transitive dependency checksums in `go.mod`/`go.sum`. That driver exposes PostgreSQL through Go's `database/sql`; see the [upstream release](https://github.com/jackc/pgx/releases/tag/v5.11.0) and [stdlib documentation](https://pkg.go.dev/github.com/jackc/pgx/v5/stdlib).

```sh
bash tools/online-services-verify.sh
```

The script uses dedicated Go caches under `/private/tmp`, checks dependency hashes, runs `go vet`, then runs race-enabled integration tests. Test setup **always creates a new private PostgreSQL cluster** under `/private/tmp/awpg-*`, listening only on its own Unix socket. Tests never read an existing `ASHENWAKE_DATABASE_URL` or redirect to an existing database. A unique database isolates each test. The suite exercises actual SQL transactions, simultaneous admissions/checkpoints, a database error after the first character update, server restart, and `pg_dump`/`pg_restore` into a second private database. Cleanup stops only the cluster it created and removes its directory. `ASHENWAKE_TEST_PG_BIN` may select the PostgreSQL binary directory; it cannot select a data directory.

To leave a private cluster and API alive while running the C# server and clients:

```sh
export ASHENWAKE_SERVER_KEY="$(openssl rand -hex 32)"
export ASHENWAKE_GAME_SERVER_URL='ws://127.0.0.1:5180/v1/matches/{allocationId}/socket'
bash tools/online-services-verify.sh serve
```

Pass the same server key to the C# server through its environment. The runner starts HTTP on `127.0.0.1:8088`, overrides any inherited database URL with its own private cluster, and cleans up on interruption. It prints structured request outcomes but never prints the key. Changing `ASHENWAKE_HTTP_ADDR` can select another loopback address/port. This temporary runner deliberately discards its database when stopped; the restart/backup tests verify durable behavior within a retained private cluster.

For an explicitly managed local database, build/run `services/online/cmd/online` with `ASHENWAKE_DATABASE_URL`, `ASHENWAKE_SERVER_KEY` (at least 32 characters generated from random bytes), `ASHENWAKE_HTTP_ADDR`, and `ASHENWAKE_GAME_SERVER_URL`. The application creates/verifies the single embedded migration using a transaction and advisory lock. Unknown schema versions or changed migration checksums fail startup. Use a dedicated database. No source migration should be edited after release; add a separately versioned migration when the schema changes.

`services/online/Dockerfile` is a build recipe with a non-root runtime user. No image build or deployment is performed by verification. A container requires explicit database/key environment configuration and a host-loopback port publication such as `127.0.0.1:8088:8088`; its `0.0.0.0` listener is enabled only with the container opt-in. PostgreSQL should disable statement/parameter logging for save payloads (`log_statement=none`, `log_min_error_statement=panic`, `log_parameter_max_length_on_error=0`), and backups require the same access protection as the database. The private runner uses these logging settings.

## HTTP contracts

All successful responses are plain JSON with HTTP 200. Mutating request bodies use `Content-Type: application/json`; unknown outer fields, duplicate JSON keys, extra trailing JSON, excessive nesting, and oversized bodies fail with 400. Empty bodies are treated as `{}` for no-argument actions. Errors are `{ "error": "invalid_request|unauthorized|not_found|conflict|draining|busy|timeout|internal_error" }`; rate limiting uses 429 and `Retry-After`. No database errors, credentials, or raw snapshots are returned in errors.

Client authentication is `Authorization: Bearer <sessionToken>`. Server authentication uses the same header format with the separately configured `ASHENWAKE_SERVER_KEY`. Neither credential type authorizes the other's routes. Protocol identity is `coop-net.1`; the C# Core rules identity remains `coop.1` inside its opaque state. Content identity is 64 hex characters, stored uppercase. Allocation URL comes only from service configuration; `{allocationId}` is replaced with the generated ID.

| Method / path | Request | Response / authority |
|---|---|---|
| `POST /v1/sessions/anonymous` | `{}` | `{accountId,sessionToken,expiresAt}`; no authentication |
| `DELETE /v1/session` | none | `{revoked:true}`; revokes current session |
| `POST /v1/characters` | `{name,discipline:"Vanguard"}` | `{id,accountId,name,discipline,revision:0,state:{}}`; no client state/import accepted |
| `GET /v1/characters` | none | array of owned character objects |
| `POST /v1/parties` | `{characterId}` | `{id,leaderAccountId,status,players}`; owned character enters slot 1 |
| `GET /v1/parties/{id}` | none | same party view; members only |
| `DELETE /v1/parties/{id}` | none | `{closed:true}`; leader may close an unallocated party |
| `POST /v1/parties/{id}/invites` | `{}` | `{inviteToken,expiresAt}`; leader only; replaces prior live invite |
| `POST /v1/party-invites/accept` | `{inviteToken,characterId}` | party view; owned guest character enters slot 2 |
| `POST /v1/parties/{id}/ready` | `{ready,protocolVersion,contentHash}` | party view; ready requires supported protocol and valid hash |
| `POST /v1/parties/{id}/allocate` | `{operationId}` | allocation below; leader, exactly two compatible ready members |
| `POST /v1/allocations/{id}/join-ticket` | `{}` | `{ticket,expiresAt}`; members only |
| `POST /v1/server/allocations/{id}/consume-ticket` | `{ticket}` | `{allocationId,slot,characterId,accountId,revision,state}`; trusted server only |
| `GET /v1/server/allocations/{id}` | none | allocation plus both character objects; trusted server only |
| `POST /v1/server/allocations/{id}/checkpoint` | checkpoint below | `{revision,characters:[{characterId,revision}]}`; trusted server only |
| `POST /v1/server/drain` | `{draining:true}` | `{draining:true}`; trusted server only; false resumes admission |
| `GET /healthz` | none | process health |
| `GET /readyz` | none | DB connectivity and admission state; 503 during drain |
| `GET /metrics` | none | bounded counters; trusted server only |

Allocation shape:

```json
{
  "allocationId": "generated-id",
  "partyId": "generated-party-id",
  "revision": 0,
  "serverUrl": "ws://127.0.0.1:5180/v1/matches/generated-id/socket",
  "protocolVersion": "coop-net.1",
  "contentHash": "64_HEX_CHARACTERS",
  "players": [
    {"slot":1,"characterId":"first-id","accountId":"first-account","ready":true,"protocolVersion":"coop-net.1","contentHash":"64_HEX_CHARACTERS"},
    {"slot":2,"characterId":"second-id","accountId":"second-account","ready":true,"protocolVersion":"coop-net.1","contentHash":"64_HEX_CHARACTERS"}
  ],
  "snapshot": {}
}
```

The trusted GET also returns `characters`, ordered by slot. Stable slots follow party creation/admission order. Each account can join one open/allocated party, and each party contains two distinct accounts/characters. Issuing a new ticket invalidates its predecessor; successful consumption is atomic and one-use. Reconnection requests a fresh ticket, then the server reads the latest committed allocation state. Tickets are authorization to a specific allocation and slot, not authority to supply a character state.

Checkpoint shape:

```json
{
  "operationId":"checkpoint.30",
  "expectedRevision":0,
  "snapshot":{"rulesVersion":"coop.1","tick":30},
  "characters":[
    {"characterId":"first-id","expectedRevision":0,"state":{"rulesVersion":"coop.1","experience":15,"ash":10}},
    {"characterId":"second-id","expectedRevision":0,"state":{"rulesVersion":"coop.1","experience":15,"ash":10}}
  ],
  "rewards":[
    {"characterId":"first-id","eventId":"encounter.1","payload":{"experience":15,"ash":10}},
    {"characterId":"second-id","eventId":"encounter.1","payload":{"experience":15,"ash":10}}
  ]
}
```

The opaque shared/personal objects above illustrate transport shape; actual Core snapshots contain its complete validated state. The C# server must restore/validate content and rules, reject client authority claims, derive both resulting personal states and reward payloads, and submit all of them together. The service does not interpret or independently award experience/ash.

## Transaction and recovery invariants

A checkpoint locks its allocation and both character rows, confirms membership/ownership and all three expected revisions, rejects previously persisted personal reward events, updates both personal states and the shared snapshot, inserts reward receipts, and stores the operation's response in **one PostgreSQL transaction**. An exception anywhere rolls back all writes. The same allocation/operation ID with the same canonical payload returns the original committed response even after revisions advance. Reusing that ID with changed input returns 409. Reusing a personal reward event under a different operation also returns 409, without partial state changes. Reward identity is `(allocationId, characterId, eventId)`. Changed expected revisions are changed payload, not an idempotent retry.

The idempotency digest binds canonical JSON key order, both character IDs/states/revisions, every reward payload, and shared state. Characters and rewards are sorted before hashing; numeric lexical forms remain significant. A caller must retain the exact logical checkpoint input until acknowledged. After an uncertain response, resend that operation unchanged or fetch authoritative state before constructing a new operation. Do not invent a new operation for the same valuable outcome.

Database restart retains snapshots, sessions, reward receipts, and stored responses. The automated backup exercise uses `pg_dump --format=custom` and `pg_restore --exit-on-error` into a newly created private database, then repeats the original checkpoint and confirms both character revisions. The application has no direct restore endpoint and never overwrites an online character with offline JSON. Database maintenance, backup encryption/retention, and any future public deployment remain operational work outside this local prototype.

## Bounds and observability

- Anonymous credentials, invite tokens, and tickets use 32 random bytes; only their SHA-256 hashes are stored. Sessions expire after 24 hours, invites after 10 minutes, tickets after 30 seconds. Expired/revoked credentials fail authentication. Each account has at most eight characters; names contain 1–32 non-control Unicode characters.
- Checkpoints require exactly two distinct member character writes and at most 32 distinct personal reward receipts. Operation/event IDs use `[A-Za-z0-9][A-Za-z0-9_.:-]{0,119}`. Revisions remain below `2^53`. Shared state is at most 1 MiB, each personal state 256 KiB, each reward payload 16 KiB, request body 2 MiB. JSON depth is capped at 64 with at most 50,000 values.
- The HTTP server bounds headers (16 KiB), active requests (64), header/read/write/idle timeouts (3/12/15/30 seconds), database operations (10-second request contexts), and the SQL pool (12 connections). Token-bucket limits per direct peer address allow 12 anonymous sessions/minute, 240 ordinary requests/minute, and 1,200 trusted-route requests/minute, each with its full minute's burst. Forwarded client-address headers are ignored. The limiter retains at most 4,096 peer/class entries and expires idle entries when full.
- Structured audit events record route template, HTTP status, and duration. They omit account/character IDs, query strings, credentials, names, and save payloads. Metrics report request/failure/active counts and drain state without per-user labels. Readiness checks PostgreSQL and drain state; liveness does not depend on the database.
- Draining rejects new sessions and allocations while allowing existing trusted checkpoints and reconnect tickets. SIGINT/SIGTERM drains admission and gives in-flight HTTP requests a bounded shutdown window. This prototype retains completed allocations for replay/reconnect inspection; allocated party lifecycle cleanup, persistent metric storage, session retention jobs, and multiple game-server leasing are future extensions.
