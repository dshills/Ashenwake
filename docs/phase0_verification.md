# Phase 0 implementation and verification

The architecture spike is implemented. The larger combat sandbox is Phase 1 and is not included in this milestone.

## Implemented gate evidence

| Requirement | Implementation and evidence |
|---|---|
| Core without Godot | Independent `net8.0` library; assembly-reference regression test |
| Fixed ticks and event bridge | 30 Hz accumulator, bounded catch-up, pause/single step, interpolation; rendered Godot smoke |
| Shared authoritative geometry | Integer positions, static obstacle sweep/slide, hit range/line of sight, stable overlap ordering; collision tests |
| Structured content | Typed required schemas, semantic validation, stable IDs/localization, hashed runtime bundle; invalid-content tests |
| Skill/enemy/item/fragment interaction | Ember Strike → Ember of Vael → Burning → Ash Ghoul death → seeded Ash Iron roll → pickup |
| Seeded independent RNG | Separate persisted combat/loot/AI/encounter streams; reproducibility and AI/loot isolation tests |
| Replay | Initial snapshot, tick-indexed commands, per-tick state/event hashes, first divergence; Core/editor/export scenario agreement |
| Logical save/load | Rolled items, equipped identity, fragment, pending actions/statuses, progression, RNG; round-trip/resume tests |
| Atomic save/backup recovery | Flushed same-directory replacement; last valid backup recovery; corrupt/newer-save regression tests |
| Toolchain/automation | Pinned project-local tools, package locks/archive checksums, Go `aw`, verification/export scripts, GitHub Actions |
| Actual package | macOS universal development ZIP exported, ad-hoc signed, launched on Apple Silicon, and replay-verified |

## Recorded local checks

- C# solution build: successful with zero warnings/errors.
- xUnit: 13 tests passing; Go: two CLI tests, vet, and build passing.
- Content compile/validation: content version `0.1.0`, hash `F8AAE568EAA1D73EA9CB8EEAF2DD897548B526815D8205BFE728E544364F3B0D`.
- CLI, Godot editor smoke, rendered client smoke, and standalone package: the 90-tick encounter ends at state hash `A4D35D968D04E8C16B7D72F34B09A6E81B260C8402E7B96CBAC26A97EFFB8B10`.
- Rendered frame inspected at 1280×800: room/obstacle layout, character, health/state feedback, event log, controls, and diagnostic HUD visible without clipping.
- Interactive packaged-app check: Space triggered the fragment/Burning kill; P paused; period advanced exactly one tick; F5/F9 saved/restored; F6 verified a 1,021-tick recording. Automated scenarios cover movement and pickup.
- Local macOS export executed successfully. Linux CI is configured; a Linux execution result is not claimed from the macOS host.

Run `bash tools/verify.sh` and `bash tools/export.sh` to regenerate evidence under ignored `artifacts/`. They fail if the engine logs an error or the expected smoke-success record is absent, even when Godot itself returns exit code zero. Keep the exported Linux runtime directory with its executable.

## Initial performance baseline

Reference: Apple M4 Pro, 14 logical processors, macOS/Darwin 25.6, .NET 8.0.31, Godot 4.6.2 Mono Compatibility renderer. This scene contains two actors, one possible status, and one possible item drop; it is not a production enemy-population benchmark.

The initial Core run measures 9,000 ticks from 100 fresh encounters, after 10 warmup encounters. Recorded values: p50 0.0019 ms, p95 0.0034 ms, p99 0.0048 ms, maximum 0.6597 ms, and approximately 3,146 allocated bytes/tick. This is intentionally an allocation-heavy correctness baseline, not the final packed simulation architecture.

The first rendered client run recorded p95 `_Process` CPU work of 0.5943 ms and p95 tick-plus-replay work of 0.4080 ms. A subsequent instrumented rendered run measured frame-interval p95 of 12.20 ms, 76 draw calls, approximately 4.30 MB managed memory, and 44.78 MB Godot static memory. These are short diagnostic samples, not GPU timings or hardware certification. Current reports also capture entity/effect counts and catch-up loss. Performance certification across hardware and large populations remains later work.

## Boundaries and next work

- One stationary enemy, one direct attack, one Arms fragment, one Burning effect, one item/loot table; no player-damage AI, dodge, potion, resource system, full discipline, or campaign.
- Flat room geometry; no navigation mesh or engine-physics dependency. Room obstacles are the shared authored geometry, not an external DCC export pipeline.
- One save schema, strict matching content/rules, and no legacy migration yet. The test fixtures establish the place for later migrations; incompatible saves are preserved and rejected.
- Replay is scoped to matching pinned configurations and recent 3,600-tick client segments; no cross-platform determinism guarantee.
- Input is keyboard/mouse and HUD text is English. Production animation/audio, accessibility, controller support, and localization remain later milestones.

Phase 1 should start with movement/attack/dodge feel, the full combat ordering document, active enemy telegraphs, Vanguard Momentum and six abilities, then the wider fire/spirit/poison interaction. Use the tiny baseline to compare correctness and cost as those mechanics are introduced.

## Review

Prism CLI 0.5.0 reviewed the staged implementation using its configured `gemini-3-flash-preview` provider. Initial run `60c99e7108b8107297b4001469ee34f1` returned two medium performance findings. Follow-up run `3553492b87cb6c4665aa276167bea8f9` used `tools/prism-phase0.json` and returned one medium and two low findings. This is a completed review with explicit dispositions, not a claim that Prism returned zero findings.

| Finding | Disposition |
|---|---|
| Repeated linear loot searches and snapshot allocation in `Main.Present` | Fixed: take one loot/entity view per presentation update and use a set of live drop IDs for cleanup. |
| Quadratic actor separation checks | Deferred to population scaling: the world is currently restricted to exactly two actors, with p99 baseline simulation time under 0.005 ms. A spatial index before a measured need contradicts the architecture's optimization rule. |
| Attack selects the configured `StartingAbility`; commands lack a skill ID | Intentional Phase 0 scope: the milestone requires one ability, and the client exposes one attack action. Explicit loadout/skill selection is Phase 1 work; no multi-skill support is claimed. |
| `Single()` content lookup might encounter a missing or duplicate starting ability | Already covered: both constructors validate a private copy of the entire bundle, including duplicate IDs and starting references, before any tick. Invalid-content tests exercise this boundary; the client cannot mutate the internal bundle. |

Prism supplied abbreviated/nonexistent paths for several follow-up findings; each was mapped to the actual implementation before disposition. No severity override or finding suppression was used. Review excluded the earlier design plan and generated checksum/lockfile noise; source content, simulation, client, tests, scripts, and docs were included.

After the client fix and additional missing-coordinate/unknown-field regression coverage, `tools/verify.sh` and `tools/export.sh` both passed. The repository also passed staged whitespace checks. Raw review JSON remains in ignored `artifacts/review/` for local inspection.
