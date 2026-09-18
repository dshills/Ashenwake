# Phase 7 — optional cooperative prototype verification

The local prototype completes a shared Ossuary → Cloister → three-phase Bell Saint slice with two fixed, server-owned Vanguard loadouts. One C# authority owns movement, combat, resources, enemies and personal rewards. Godot sends intent and renders snapshots; Go/PostgreSQL owns account/party admission and atomic durable checkpoints. Offline campaign/endgame saves remain independent and cannot be imported to claim online power.

This note records measured prototype evidence and a bounded independent source audit. It does not itself certify a public service or replace the phase's Prism review. The actual exported client has also passed the final local acceptance route described below.

## Verified evidence

| Check | Observed result | Evidence |
|---|---|---|
| Isolated baseline | Build succeeds; 282 Core tests, 11 server tests and 15 Go control-plane tests pass | `/private/tmp/ashenwake-phase7-verify.log` (final successful run; the log also retains the earlier corrected whitespace failure) |
| Server review regressions | All 12 server tests pass after the replay-write publication fix | `/private/tmp/ashenwake-phase7-server-disk-verify.log` |
| Go review regressions | Dependency verification, vet and race-enabled tests pass: 15 PostgreSQL/control tests plus 3 lifecycle tests | `/private/tmp/ashenwake-phase7-go-drain-verify.log` |
| Actual network route | Completes at tick 993; ten distinct durable personal receipts; one reconnect and one abrupt dedicated-server restart | `artifacts/coop-network/run.pQtaWa/probe/report.json` in the isolated Phase 7 checkout |
| Durable replay continuity | 39 nonempty, contiguous revision segments; each initial hash matches the previous final hash; the last replay matches both completed clients | Same network report and the probe's enforced replay checks |
| Rendered Godot client | Tick 992; 992 matching same-tick snapshots, zero mismatches; ten receipts | Workspace `artifacts/coop-agent/readable-final/coop-client-report.json`, encounter captures and `coop-complete.png` |

The network run's content hash is `FC1FC621E8F8C87687F688EA49AC23F882F897991C238ED1E4071699E2BD4DFC`; final state hash is `DF1B25FCEB737B194082E2F6AD82ECB98633E945DC99DB22C571559B2E810002`. The rendered-client run uses a separate allocation and therefore has its own final hash.

The impairment driver omitted 116 application intents, duplicated 82, sent 65 stale sequences and delayed 266 by 12–24 ms. WebSocket/TCP bytes remained reliably ordered. This tests software intent impairment and recovery, not real internet packet loss, WAN latency or congestion.

After restart, the server diagnostics measured 531 full loop iterations: p99 14.0452 ms, one iteration above the 33.34 ms budget, zero persistence failures and 19,509,192 managed bytes at capture. Timing includes synchronous checkpoint/replay work, but excludes client rendering and control-service CPU. These figures describe one local match, not four-match density or a long-duration memory guarantee. Separate Core seeds 42–44 complete without wipes; measured peaks are seven actors, two projectiles, three warnings and two queued inputs, with replay and restore checks passing.

## Audit disposition

The independent audit checked authentication/ownership, transactional reward writes, reconnect replacement, replay origins, backpressure, eviction and shutdown. It found two additional concrete failures, both fixed with regressions:

- Go previously returned from `ListenAndServe` before its shutdown goroutine finished draining handlers, allowing process/database teardown during an active checkpoint. The caller now waits for the bounded drain, force-closes remaining connections on timeout, and reports timeout as failure. Tests exercise a real active response, deadline cancellation and listener startup failure under the race detector.
- A replay-path write failure could stop C# combat before preparing an HTTP checkpoint, then publish newly earned but uncommitted receipts during another peer's disconnect. Draining disconnect cleanup now suppresses that broadcast. A real filesystem-path failure on a reward tick earned through ordinary inputs verifies no reward-bearing frame escapes and no checkpoint was submitted.

The broader suites cover immutable uncertain-write retries, deferred disconnect replay continuity, future character state preservation, atomic rollback after a partial SQL write, duplicate reward rejection, PostgreSQL restart and backup/restore. Failed persistence freezes combat; idle eviction requires a durable checkpoint and no remaining peer lifetimes. These tests do not simulate every storage or infrastructure failure.

## Delivery boundaries

Both nonroot container recipes were rebuilt from the final isolated Phase 7 source, including the shutdown and reward-publication corrections. Neither image was run or pushed. The local `ashenwake-server:phase7-local` image is `sha256:11cc93ac2843b81abd14065627bcc2ac8e51abda22c9f71d10d4d32c3d83a3fc`; `ashenwake-online:phase7-local` is `sha256:8ce0908121dd978d1ca36a0bf394e84b2103ea7a15ae4b3ef3f9586a3d922b8b`. Build logs, image-ID files and `images.json` are retained under the isolated checkout's `artifacts/phase7-containers/`. These are successful image-build checks, not container runtime or deployment tests.

Scope remains a greybox, two-player, fixed-Vanguard slice with anonymous bearer sessions and one configured authority. The current party API has no completed-allocation recycling workflow. Additional disciplines, repeatable online progression, public identity/recovery, TLS deployment, authority leases/failover, production load/cost measurements, retention policy, public anti-cheat and human co-op feel remain outside this prototype. No cloud account, paid infrastructure or public endpoint was provisioned. The four-resident-match cap is a resource guard, not a capacity certification. See `coop_combat.md`, `coop_network.md` and `online_services.md` for operational contracts.

## Packaged client and Prism review

The macOS ZIP `284D49BAEE5D45C86034E4026E7FBF6E05B95CEF6B8C846E1CBAEEF6DD4D4994` passed offline startup, all 17 release UI checks, and the default Launch → `--coop-smoke` route against the final corrected server. The final two authenticated peers matched 1,035 snapshots with zero mismatches and ten durable personal receipts. Final hash: `F32D6CCCB25D9EF04D32082B10421FFC31FDDC3D3F242290ADE546B9718D7C87`. Evidence is `artifacts/coop-package/run.j83w7y/report.json`; the unchanged package is retained under `run.j9w3yJ`. The server assembly SHA-256 is `F3CD956313EF1381DD456C8A30969762FAF56A8A385B2A469E0FB871C3F8CF64`. The script's optional reuse mode records both package and rebuilt server identity and obtains fresh short-lived tickets only after readiness. Exported templates route via Launch because they disable command-line scene overrides.

Prism reviewed the phase diff with Gemini. Its two high findings report invalid `[REDACTED]` tokens inserted by Prism's credential-name redaction; those tokens are absent from source and the complete source/package builds pass. The two medium observations concern repeated canonical state hashing and fixed encounter indices. Canonical hashing is retained as a measured correctness check for this bounded prototype (full-loop p99 14.0452 ms in the impaired/restart route); fixed encounter indices belong to the immutable five-room slice and are covered by complete encounter tests. Neither observation establishes a current correctness failure. Future content expansion or density targets would require revisiting those choices. The separate audit fixes above were tested after this review.
