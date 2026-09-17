# Phase 1 — Combat sandbox verification

The engineering milestone adds the playable Vanguard arena to the Phase 0 foundation. The campaign is developed separately. The implementation plan's external playtest gate remains open: automated encounter completion does not establish combat enjoyment or controller usability.

## Implemented behavior

- Six command-driven abilities; Momentum generation/spending/decay; target range and line-of-sight checks; windup/recovery, input buffering, dodge cancellation/invulnerability, stagger, and limited potions.
- Active melee, ranged/repositioning, and armored AI, plus an elite area attack. Projectiles and persistent areas live in Core and survive snapshots.
- Ordered integer damage rules, bounded effect queue and causal IDs, Burning/Poisoned/Staggered/Vulnerable, exactly-once death/loot, item instance identity and seeded rolls.
- Six anatomy slots with three authored fragments. Critical ignition, damage-over-time death spirits, and spirit poison use authored trigger/effect pairs. Removal cleans up dependent summons/statuses.
- Avalanche and No Ground Given alter Shield Breaker behavior; the latter gives barriers and retaliation. Equipment comparison and anatomy/mutation selection are available in the client.
- Rebindable keyboard actions, initial controller combat bindings, targeting/zoom, generated impact/tell audio, visual telegraphs, reduced effects/shake, and a development profiler.
- Standard, dense, projectile, summon, and chain presets; save checksum/atomic replacement/backup recovery; mid-encounter command replay with first-divergence reporting.

## Reproduction

Run `bash tools/verify.sh` followed by `bash tools/export.sh`. The former retains all Phase 0 regression checks and adds combat rules, persistence tests, deterministic demo/replay, five workload benchmarks, and the sandbox client smoke. Export launches the packaged client and verifies its recorded combat replay in the headless tooling process.

The CLI demo uses 900 authoritative ticks, clears six enemies, and collects their six drops. The client smoke uses the same Core through its input/presentation adapter and records its own deterministic action policy; it is checked by replay rather than expected to share the CLI's final hash.

## Performance scope

On this macOS arm64 host (.NET 8.0.31, 14 logical processors), the initial five-preset run measured a highest simulation p99 of 0.32 ms (dense, 74 peak actors) against the provisional 25 ms budget. Each preset measures 4,500 ticks after one 900-tick warmup, with population/effect counters and allocation data in `artifacts/combat/benchmark.json`.

This measures `CombatSession.Step`, excluding view copies, command policy, replay hashing, Godot drawing, GPU time, and other hardware. Dead/finished encounters remain part of each fixed-duration sample. Rendered client measurements are reported separately; no broad hardware or external player claim is inferred from these numbers.

The final rendered client smoke completed in 425 ticks with six enemy deaths, nine inventory items, eight fragment triggers, no rejected effects, 2.32 ms p95 client processing, and 11.63 ms p95 sampled frame intervals on Apple M4 Pro. Its state hash `D3E095A9E082E78D2BE97B9B8302226B3FBFA90CF52E25AD4468F70711D124AD` matches the headless client and exported macOS package. A Godot ObjectDB resource warning appears on rendered process shutdown; it is a tracked cleanup issue, not claimed resolved by the clean headless run.

## Acceptance limitations

The arena deliberately uses procedural prototype geometry/audio. Rigged production characters, finished environment kits, full controller menu accessibility, and independent player observations belong to later production/acceptance work. Five stress presets are diagnostic scenarios rather than evidence that every build clears every dense workload. Phase 2 is allowed to proceed as development while these qualitative gates remain explicitly unaccepted.

## Prism review

Prism reviewed the staged Phase 1 diff using the configured Gemini provider (`artifacts/review/prism-phase1.json`, run `a63748a926f855afec524e2a8b5123a3`). It returned one medium and one low finding, with no high findings.

- **Medium: a fragment could occupy two slots.** Not reproducible under the implemented contracts: content IDs are unique; every fragment has exactly one anatomy slot; equip derives its key from that definition; restore validates each key/value pair. Added a regression proving duplicate-ID authoring and wrong-slot restore both fail and repeated equip retains one slot. Clearing/reapplying a correctly equipped fragment would unnecessarily destroy its effects, so the suggested cleanup was not applied.
- **Low: projectile/area removal uses linear searches inside bounded loops.** Confirmed, but not a measured bottleneck at caps of 128 projectiles/64 areas; the measured simulation workloads remain far below budget. Retained deterministic insertion-order processing. A future measured optimization should compact in stable order and retain replay tests; blindly reversing iteration would change effect ordering.

The review disposition records the actual findings rather than treating the provider's nonzero severity exit as a clean report.
