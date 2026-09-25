# Release hardening and acceptance record

The current local candidate is **1.0.0-rc.1**. Its scope and playtest criteria are in [the RC1 plan](release_candidate_1.md), with measured results in [the RC1 verification record](release_candidate_1_verification.md).

The repository has automated integrity, recovery-fixture, diagnostic, and short-soak checks. **It is not certified as a production single-player release.** These tools report their measured scope and leave unperformed OS/GPU/input, art/content, human playtest, accessibility, localization, signing, and distribution acceptance gates open.

## Automated commands

Invoke from the repository root with the pinned local tools:

```bash
bash tools/release-audit.sh
AW_SOAK_SESSIONS=100 AW_SOAK_TICKS=3600 bash tools/release-audit.sh
bash tools/release-audit.sh /absolute/path/to/distribution net8.0.31 osx-arm64
aw release endgame-soak artifacts/release/persistent-run 3
bash tools/release-package.sh "$(git rev-parse HEAD)"
```

The script builds the tooling, checks maintained save fixtures and asset source inventory, and runs a combat soak. With an unpacked distribution path, it creates and verifies a manifest tied to the current Git commit. It does not sign, upload, publish, tag, or alter distribution files.

The CLI helper also exposes:

```text
release manifest <distribution-root> <build-id> <rules> <runtime> <platform> [output]
release verify <distribution-root> <manifest>
release fixtures [repo-root]
release soak [sessions] [ticks] [output]
release endgame-soak <new-output-directory> [extra-fractures]
release audit [repo-root]
```

Audit exit status distinguishes automated technical failures from unperformed release decisions. `artifacts/release/readiness.json` always preserves the explicit outstanding gates; passing its automated checks does not declare a public release ready.

## Immutable package identity

`ReleaseManifests.Create` records relative path, category, byte length, and SHA-256 for every selected file, plus build/rules/runtime/platform identity, separate aggregate content and asset hashes, and a hash of the complete canonical manifest. Input order does not change the manifest. Verification detects missing files, altered bytes, changed metadata, and changed content/asset lists.

Portable paths reject absolute locations, traversal, case-colliding names, control characters, and symbolic links inside the selected package. The caller-selected root is the trust boundary; operating-system aliases in its ancestors are allowed. The CLI scans only the explicitly supplied unpacked distribution root. Keep the manifest outside that root so it does not include an earlier version of itself. Verification rejects undeclared package files as well as changed declared files. It is not a digital signature and cannot establish publisher authenticity. Compare the accepted manifest hash through a trusted release record when distributing a package.

The CLI requires explicit rules, runtime, and platform labels. `tools/release-package.sh` exports and runs the actual package, collects the pinned engine/runtime notices, records code/content/asset identities, and verifies a manifest outside the package root. Its source identity must be the reviewed commit or tree that produced the candidate. A source hash and an integrity manifest do not provide publisher authentication or prove other platforms.

## Maintained fixtures and recoverable upgrades

`fixtures/save-fixtures.json` lists original fixture files, their explicit type, and immutable expected byte hashes. `fixtures/adventure-v1-hub.json` is the maintained legacy hub fixture. The audit validates its original bytes, runs the supported migration, and round-trips the upgraded state without rewriting the fixture. Changed fixture bytes or unsupported content/schema produce a failure with the originals preserved.

When changing saves or content, add a migration and a new fixture entry deliberately. Never regenerate old fixtures during validation to make a failing upgrade pass. The inventory also freezes actual Phase 2, Phase 3, and Phase 4 application saves and their original catalogs. The audit executes Phase 2→3→4→5, Phase 3→4→5, and Phase 4→5 upgrades, preserves owned inventory, XP, materials, story and choices, then round-trips the current archive. It also restores the actual exported Phase 5 completion without changing its exact state hash. Future unsupported versions fail without altering original bytes.

The application save implementation retains the prior valid generation, rejects incompatible newer saves without overwriting or falling back, and commits authoritative domain snapshots together. Patch validation must test the last accepted distribution's save files against the new distribution. Executable rollback does not make a newer save safe for an older build; retain original backups and declare the supported direction of migration.

The RC1 inventory also freezes an actual pre-pet client save and an actual pet-owning client save from the preceding build. Both are loaded through the current endgame archive and compared across a round trip. Frozen JSON/replay documents receive byte-integrity and syntax checks only; the audit labels these separately from executable restores.

## Local diagnostics and privacy boundaries

`DiagnosticBuffer` retains the latest 256 semantic combat events, counting dropped older events. It stores bounded identifiers and numeric values. Unknown/free-text event kinds and content identifiers are redacted; exception messages, stack traces, profile names, absolute paths, environment variables, and arbitrary metadata are not included. Only the exception type and a known failure category are retained.

Replay data is excluded by default. An explicit `includeReplay: true` request may include a recorded segment of at most 300 frames. A replay contains gameplay state and commands and must be treated as optional user data. The complete bundle is capped at 2 MiB. `WriteLocal` writes atomically to the caller's local destination. There is no telemetry upload, background network client, crash collection service, or implicit sharing.

## Client recovery checks

`ReleaseSmoke.tscn` exercises focus loss, controller disconnect/reconnect signals, pause/resume, cleared held inputs, keyboard focus, persisted settings and backup recovery, and local diagnostic privacy in the actual engine. Test settings and exports use the supplied output directory. Reconnect leaves the game paused until explicit resume. Malformed and oversized character-selection pointers recover without deleting character archives. These software checks do not certify a physical controller or another OS/GPU.

## Soak evidence and interpretation

The default soak runs ten deterministic combat sessions of 900 ticks, alternating standard and fragment-chain presets. It restores a checksum-validated checkpoint every 120 ticks, continues the restored simulation alongside the original, verifies state and semantic event hashes, and replays each completed recording. It tracks population caps, runtime/platform identity, simulation-only p50/p95/p99 times, per-tick allocation, retained managed-byte delta, and final deterministic hashes.

Commands, checkpoint/replay serialization, view construction, and replay verification are outside the measured simulation-only step samples. Retained managed-byte delta includes test-runner/report/JIT effects and is diagnostic evidence, not proof of a leak. The bounded suite accepts at most 100 sessions × 3,600 ticks. It exercises repeated encounter lifecycles, not a single multi-hour persistent campaign session, GPU rendering, loading stalls, controller reconnect, or the complete OS matrix. Record those separately before accepting stability/performance gates.

The persistent endgame soak imports the maintained completed campaign, progresses through all ten tiers and five hunts, then repeats tier-ten expeditions. Every 600 public commands it verifies a continuous replay segment and writes/loads the joined character/profile archive. It reports full command cost, allocation, retained memory, receipt/inventory growth, and final state hash. Each invocation requires a new output directory. Checkpoint serialization and replay validation are outside command timings. Managed-memory deltas remain diagnostic, not a proof of a leak or a multi-hour hardware certification.

## Patch and recovery procedure

1. Preserve the last accepted package manifest and the player's original character/profile files before upgrade. Copy fixtures; never regenerate historical bytes to pass a check.
2. Run the full verification, maintained upgrade audit, actual packaged-app playthrough, release UI smoke, and bounded persistent soak on the reviewed source tree.
3. Verify the candidate manifest, exact notices, source/content/assets identity, and declared platform. Compare saves and receipt counts before and after update. Record any schema/rules/content changes.
4. Diagnose a failure from the build ID (Core assembly MVID), content hash, failure category and optional user-exported diagnostics. Reproduce with an explicitly provided replay; diagnostics remain local until the user chooses to share.
5. Restore a validated backup into a new destination when corruption recovery is necessary. A newer unsupported save must remain untouched. Rolling back an executable does not authorize downgrading its saves.
6. Record hardware, input, accessibility/localization, rights, signing and independent playtest acceptance separately. Tag and distribute only the accepted candidate; automated checks alone do not grant release acceptance.

## Asset and distribution inventory

`assets/credits.json` inventories the project-authored procedural combat/adventure geometry and synthesized cues. It references source files so missing assets are detected. No downloaded art/audio is declared by this inventory. The project currently lacks an owner-approved distribution license/credit declaration, so `redistributionApproved` remains false rather than inventing rights.

`tools/collect-notices.sh` extracts the pinned Godot 4.6.2 engine license database and copies the .NET 8.0.31 runtime-pack license and third-party notices. The candidate includes these actual texts and a provenance/hash record. `docs/third_party_notices.md` describes the process. Final distribution still requires approved project credits, component audit and applicable signing/notarization.

## Outstanding release acceptance

| Gate | Required evidence |
| --- | --- |
| Playable scope | Accepted five-discipline campaign, endgame, crafting, anatomy, and Godwrought behavior in the exported product. |
| Player acceptance | Independent playtests of telegraphs, build diversity, narrative comprehension, replay interest, and accessibility. |
| Hardware/control matrix | Exported-build checks on declared OS/GPU/input combinations, window modes, focus loss, pause/resume, reconnects, and settings persistence. |
| Performance/stability | Hardware-specific rendering and simulation budgets, long persistent-session runs, loading/memory behavior, and reproducible failure triage. |
| Save/update matrix | Maintained prior-release fixtures, interruptions/full-disk/backup tests, complete migration chains, and actual package update tests. |
| Distribution | Exact runtime notices, owner-approved rights/credits, final immutable package manifest, required signing/notarization, install/update acceptance. |
| Release decision | A documented candidate playthrough, reviewed remaining issues, and an explicitly accepted build before tagging or publishing. |

Isolated snapshots without `.git` set `ASHENWAKE_SOURCE_REPOSITORY` to the reviewed repository. The package gate verifies all tracked source bytes/modes before and after export and rejects undeclared source files. Each exported smoke uses a fresh directory.
