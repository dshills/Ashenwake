# Dedicated-server lifecycle regression tests

Run `dotnet test game/Ashenwake.Server.Tests` from the repository root. The project uses the same xUnit packages and versions as the existing Core test project.

The tests run the real `MatchHost` loop against a loopback HTTP control-plane fixture that models revision checks, atomic checkpoint snapshots and operation-ID replay. A controllable `WebSocket` fixture exercises connection replacement and a blocked outbound sender while inbound messages continue. These tests do not replace the Go/PostgreSQL transactional suite or the separate real WebSocket/Godot network probe.

Covered failures include unsupported/future character state with zero writes, outbound backpressure disconnect, concurrent socket replacement and shutdown, retrying an unchanged checkpoint after a failed response (including a write that already committed), deferred disconnect replay continuity, idle eviction only after durability, retained personal receipts after slot changes, and post-completion input/reconnect hash consistency. A real replay-path write failure on an earned reward tick also verifies that peer disconnect cannot publish the uncommitted receipts. The combat fixtures are earned by two ordinary `CoopSmoke` input streams through the real shared simulation.

Server operational changes under test:

- Idle allocations become evictable only after all peer lifetimes end and their final snapshot commits. The process checks them once per second; `ASHENWAKE_IDLE_EVICT_SECONDS` defaults to 60 and is bounded to 1–600 seconds. Failed checkpoints remain resident for retry.
- Readiness returns 503 at four resident allocations, during draining, or while persistence is paused.
- Logical disconnections wait behind an immutable pending checkpoint; they become the first recorded operations of the next segment after the checkpoint commits.
- Waiting/completed-room journals flush periodically even when combat ticks stop. Completed matches reject further gameplay intent without mutating Core state.
- Shutdown attempts durability before cancellation, aborts sockets, and waits for every tracked peer cleanup. A persistently unavailable control service is reported as `CoopDrainUncommitted`; idle eviction never discards that pending state.

Artifact paths are isolated temporary directories and are removed after each test. Existing running game servers and control services are not stopped or reconfigured.
