# Cinder Reach verification

Validated on 2026-09-18 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `b2a6eb9b513cc6a9ee17ac938bf478658c4ad6b2`.

## Scope and checks

Act III now has four distinct industrial environments, regional floors and obstacle coverings, a state-driven Furnace Spindle backdrop, storm atmosphere and four synthesized ambience loops. The forge observes the existing boss's defense timer and live vent warnings; victory, pause, reduced effects, restore and travel have explicit presentation behavior. Cinder-region endgame arenas reuse the city environment.

- Solution build passes with zero warnings or errors. Formatting verification and `git diff --check` pass.
- Core regression suite: **325 passed**, including a new defense-projection test at the exact guard-expiry boundary and during the initial windup. The read-only projection does not change logical state or combat rules.
- Final rendered source diagnostic: **187 checks**, **14 screenshots** and **4,286 real campaign commands**. Both guarded states and both vent directions are observed from actual combat. The full route completes Act III, visits Greyhaven, revisits completed Act III and verifies replay.
- A separate restored campaign branch survives the full **900-tick** storm using normal movement, dodge and potion commands. It verifies expiry without death or victory, immediate regional atmosphere/audio restoration, removed scoped hazards and matching replay. The main route clears the storm after 155 combat ticks.
- Geometry checks use transformed mesh vertices. Tall architecture remains outside the authoritative arena; floors top out between **-10 mm and -6 mm**, below gameplay warnings. Both obstacle coverings stay inside their Core footprints. Static architecture uses **10–14 palette batches** and ground uses **8–10**. No art creates collision or navigation nodes.
- Mouse checks send real viewport clicks in each environment, verify picking accuracy and the mint ring's visibility above the floor, and stop via X. These viewport checks keep the campaign stationary. Separate planner checks traverse around both authoritative obstacles in each room and reject blocked destinations; the existing mouse diagnostic covers actual solo/co-op input execution.
- Furnace checks verify 11 articulated parts, at most 12 pooled embers, no live embers after settling/restore/reduced effects, finite shutdown, pause, repeated refresh, and no replayed victory. Audio checks verify fixed cache size, distinct non-silent/unclipped samples, same-process regeneration, Master routing, pause/resume and context cleanup. Five additional rendered checks cover mote motion, pause, perimeter placement and immediate room resizing.
- Four context captures, four mouse-ring captures, guarded/exposed core, horizontal/vertical vents and both shutdown states were inspected. The initial lava emission was too bright; the final material uses a restrained orange/red surface with cooled crust. Actual floor warnings remain dominant and the destination ring remains visible.
- Final focused route hash: `D709FF2B3C55B3307EDCE527CF492941A02A85D6C544F6F15B81E80E8E52E477`.

## Package verification

The exported Mac app passed **976 headless checks**: release 33, mouse 83, interactions 17, opening journey 42, Verdant 136, Cinder 182, services 19, character visuals 170, combat feedback 168 and appearance 126. Its Cinder diagnostic also passed **187 rendered checks** and wrote 14 captures. Geometry fingerprints, audio fingerprints, route hash and all rendered assertion results match the final source run. This totals **1,163 packaged checks**.

The full packaged campaign/endgame route completed **32,190 commands**, verified save/replay and produced `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`, matching the previous milestone. Borrowed Memory completed **2,516 commands**, verified save/replay and produced `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`, also matching the previous milestone. All export and diagnostic Godot log checks passed.

- Mac build: `artifacts/cinder-reach/Ashenwake.zip`.
- SHA-256: `cad7b68531d476c630576e3756ae69559b850e2d2eeedf7778e43a266cbb7c26`.
- Full headless evidence: `artifacts/package/22f02d6da01b.PmV49n/`.
- Consolidated results: `artifacts/cinder-reach/verification.json`.

## Prism review

The user explicitly approved transmitting Ashenwake source diffs to Prism's Gemini provider and confirmed standing approval. Initial review `7a3fe3ce257b471d51e2823cecbe3ab9` reported two low findings and no medium/high findings. The unused `CorePose` property was removed; `ActiveTransientCount` now supports meaningful bounded-allocation and cleanup assertions. The cross-platform PCM warning does not apply to the diagnostic: it compares two generations in the same process, with no cross-platform golden checksum, and audio never enters logical state or replay.

Follow-up review `fa18572f8b238e1c9825fdce891ae8cb` reported one medium and three low notes, with no high findings. The medium async warning is a false positive: the entire awaited diagnostic already has the suggested try/catch and failure exit. The audio note repeats the same-process misunderstanding. Recursive traversal is confined to bounded diagnostic observations, not per-frame gameplay. The axle rotates only on its local Z axis under a fixed parent and uses angle-aware interpolation, so the suggested parent/Euler failure has no execution path. No actionable findings remain. Full dispositions are in `artifacts/cinder-reach/prism-disposition.json`.

## Artifacts and limits

Evidence lives under `artifacts/cinder-reach/`: build/format/Core-test logs, Prism reports and dispositions, the exported app, package regression reports and rendered captures. Exact-package captures are in `package-rendered.M1paQ0/`, and final source captures are in `final-rendered.Aqgw3R/`; the earlier `rendered.xGQOeX/` captures show the pre-refinement lava. Initial focused headless evidence in `headless/` predates the added mouse-ring/ember assertions.

Persistent state, replay commands, gameplay RNG and combat rules are unchanged. `CombatActorView.Guarded` is a derived presentation field, not a save-schema change. This is procedural art and automated software evidence; it does not establish performance on other hardware, final textured assets or independent player acceptance. Cooperative environment art and Acts IV–V remain outside this pass. Reproduction steps are in [The Cinder Reach](cinder_reach.md).
