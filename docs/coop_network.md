# Local cooperative network prototype

The optional prototype connects two Godot peers to one dedicated C# authority and a Go/PostgreSQL control service. It is separate from the offline campaign and uses two server-owned Vanguard characters. An offline save cannot be uploaded to claim online inventory or progress. See `coop_combat.md` for combat/design rules and `online_services.md` for the control API and database transactions.

## Protocol and transport

The independently versioned wire protocol is `coop-net.1`; logical simulation rules are `coop.1`. The initial implementation uses reliable ordered WebSockets, because the measured local slice fits bounded JSON snapshots at30Hz and does not require a UDP reliability layer. This is a prototype transport decision. Internet congestion, packet retransmission delays and geographically remote latency remain unmeasured.

Connect to the control service's allocated `ws://127.0.0.1:5180/v1/matches/{allocationId}/socket`. The first text message is `{protocolVersion,contentHash,ticket}`. Protocol/content mismatch fails before consuming a ticket. A short-lived one-use ticket binds an authenticated account, owned character and stable player slot through the trusted Go endpoint. No client-supplied player ID is accepted. Subsequent messages are strict `CoopInput` JSON intent. Message size, handshake deadline, input rate and idle timeout are bounded; unknown fields and invalid enum values are rejected.

Server frames contain `{protocolVersion,kind,playerId,revision,stateHash,view,inputResult}`. `joined`, `ack` and `snapshot` include a complete authoritative `CoopView`. Accepted/processed sequences permit reconciliation after a lost acknowledgment. The client interpolates remote actors and applies bounded cosmetic local movement prediction; it never advances combat, assigns damage or mints a reward. Full snapshots are used instead of deltas for this small slice. A bounded latest-frame queue prevents an unbounded slow-client backlog. Reconnect needs a new ticket; the old connection cannot control or disconnect its replacement.

The match begins when both players have joined. It continues when one disconnects, leaving that body damageable, and pauses simulation while no peers are present. Both must explicitly ready at a cleared room or party wipe. Online pause panels affect local input; they cannot pause another player's combat. Original encounter warnings and action timelines are retained across reconnection. Restoring a server checkpoint clears stale connections and held movement before admitting new peers.

## Persistence and recovery

One serialized match loop owns all inputs, connection changes, fixed ticks and snapshots. It checkpoints every30ticks, at context changes and before publishing new rewards. Each checkpoint atomically writes the shared snapshot, both personal character states and **only newly earned** immutable personal reward receipts. Snapshot/character state retain the complete receipt ledger. The Go transaction checks the allocation and both character revisions, and exact retries reuse the same operation ID and immutable payload.

A failed checkpoint freezes simulation and reward publication; bounded backoff retries it. It cannot continue awarding play on an uncommitted fork. Character state retains earlier matches' receipts and validates its logical version before update. A restart reloads the last committed world. Up to one checkpoint interval of ordinary combat can roll back after an abrupt failure, while already acknowledged durable rewards remain exactly once. Clients resynchronize to the restored accepted-sequence values.

The host writes bounded `CoopRecorder` segments under `artifacts/online-server/<allocation>/`. Each records admissions, connection changes and tick result/state hashes. Segments are independently replayed before checkpoint submission. These are local diagnostic gameplay records, with no session tickets or API keys. They are not a public analytics stream; retention/sharing policy is a separate production decision.

## Local verification

Use a random private server key shared only by the two local services. `tools/online-services-verify.sh serve` starts a new private PostgreSQL cluster and Go API, ignoring any supplied database URL. The cluster is isolated from user databases and is stopped/removed when that runner exits. The C# server reads `ASHENWAKE_SERVER_KEY`, `ASHENWAKE_CONTROL_URL` (default `http://127.0.0.1:8088/`) and `ASHENWAKE_LISTEN` (default `http://127.0.0.1:5180`). Never put a key in a checked-in file.

```bash
source tools/env.sh
dotnet build game/Ashenwake.Server
dotnet build game/Ashenwake.NetworkProbe
bash tools/online-services-verify.sh verify
# In separate shells with the same private ASHENWAKE_SERVER_KEY:
bash tools/online-services-verify.sh serve
dotnet game/Ashenwake.NetworkProbe/bin/Debug/net8.0/Ashenwake.NetworkProbe.dll run artifacts/online-probe/new-run
```

`run` owns and terminates its dedicated server process. It creates two anonymous accounts/characters, forms a party, readies compatible builds and allocates a match. Two real WebSocket clients complete the dungeon using ordinary inputs. The impairment profile omits every17th application intent, duplicates every23rd, sends a stale sequence every29th, and adds12–24ms jitter every7th; it also reconnects one player and abruptly restarts its own server. TCP still supplies reliable ordered bytes; these are application-intent impairments, not evidence of an internet packet-loss test. Completion requires identical final client hashes, ten distinct durable personal receipts and replayed server segments.

`setup <new-output-directory>` writes a private `connection.json` with two short-lived one-use tickets for Godot. Launch the client immediately; tickets expire after30seconds. It does not print credentials. The Godot `Coop.tscn` scene accepts the endpoint, allocation and masked ticket, or command-line values for an explicitly isolated test. `--coop-smoke --peer-ticket=... --output=...` drives both real connections and compares same-tick snapshots. Never copy these temporary ticket files into release artifacts.

## Operations and limits

Health/readiness and aggregate metrics are exposed on the local services. C# diagnostics report tick cost, overruns, persistence failures, reconnects, rejected input and snapshots; Go reports HTTP/database/allocation/checkpoint counters without raw saves or credentials. These local metrics are suitable for scraping. No externally configured OpenTelemetry exporter or hosted observability backend is installed by the prototype.

Container recipes exist for both services. Build the C# recipe with repository-root context and the Go recipe with `services/online` context. Bind their exposed ports to loopback for local testing, use a private network for control traffic and mount replay storage with ownership for the nonroot runtime user. The recipes use explicit runtime versions; immutable image digests and tested cloud deployment are separate acceptance work. PostgreSQL uses versioned transactional migrations. Its tests exercise an actual database restart and `pg_dump`/`pg_restore` round trip, including character revisions and reward receipts.

The C# process caps resident matches at4; this is a resource guard, not a measured production-density guarantee. No AWS account, paid resource, public endpoint, Redis, Kubernetes, trade economy or external identity provider is provisioned. Before remote deployment: select TLS/session identity, retention and privacy policy, active-match ownership/lease strategy across multiple game servers, tested density and backup recovery objectives. The current control allocation points to one dedicated process; CAS prevents two authorities from both committing, but does not provide automatic failover orchestration.

Operating cost is intentionally not quoted from an unprovisioned cloud stack. Measure server CPU/memory at the accepted number of active matches and bytes transferred, then apply the chosen host/database/egress prices. The local network probe is a baseline for that sizing, not a production cost forecast. Human co-op feel, additional classes/builds, content-wide progression and operational staffing require a separate expansion decision.
