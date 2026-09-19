# Hollow Night verification

Validated on 2026-09-18 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `5c35f02780883e3873a01e958dd46215e70c8d31`.

## Scope

Act V has three distinct procedural environments: Repeating Rooms, Identity Memory and the Breach Heart chamber. Regional floors, obstacle coverings, lighting and three synthesized ambience loops accompany the scenery. The articulated Breach Heart backdrop follows actual phases, seal actors and boss-owned warnings. Causal echoes and the first/returning boss echoes display separate countdowns. Final defeat starts finite containment, and the existing ending screen remains reachable.

The Core exposes its existing damage-protection result as a read-only `CombatActorView.Shielded` field. The rig and actor label consume this projection. No combat rules, saved state, replay commands, progression or gameplay random streams change.

## Verified source results

- Solution build: zero warnings or errors. Formatting and `git diff --check` pass.
- Core regression suite: **326 passed**. The added test exercises actual damage against the shielded boss and an unprotected Mirrorborn copy, breaks one of three channels through real damage, then verifies exposed boss damage. It also checks view-read purity and deterministic restore.
- Final rendered diagnostic: **229 checks**, **17 captures**, **6,944 campaign commands** and matching replay.
- Source route hash: `7F3E9E6AE0A39225E3A3C183FB468467769958BAAF28983F4E0DB83927343D73`.
- All three boss phases, all living channel counts from zero to three, both shield states and all four echo warning types were observed.
- Independent defensive branches complete actual boss warning cycles in phases one, two and three after **309**, **115** and **117** ticks respectively. Phase one also observes Mirrorborn creation and expiry while the original boss remains alive.
- The independent Identity Memory death branch reaches real death after **791 movement ticks**. Death and departure both clean up stale warnings and ambience and reproduce by replay.
- Static architecture uses **7, 6 and 6 palette batches** respectively; each ground uses **7**. Floor tops are **9 mm below Y=0**. The Breach Heart has **13 articulated parts** and a fixed pool of **16 transient fragments**; its mesh count stays below the diagnostic's limit of 100.
- Final source evidence: `artifacts/hollow-night/final-rendered.f8atjn/`.

## Verification method

The diagnostic advances through real campaign commands, completes Act V and the ending, returns to Greyhaven and revisits the completed region. Independent snapshots test each boss phase without attacks, using ordinary movement, dodge and potion inputs so the actual AI completes its first echo, returning echo and final-phase sweep. Separate Memory snapshots test departure and ordinary incoming damage until death. Every branch verifies replay and leaves the main campaign state unchanged. No combat state is fabricated to force presentation results in this diagnostic.

Geometry checks inspect transformed mesh vertices: tall architecture remains outside combat bounds, ground remains below warnings and obstacle skins stay inside their authoritative footprints. The shipping mouse planner traverses around each obstacle. Real viewport clicks verify floor picking, the mint destination ring and X cancellation; these presentation checks keep combat stationary. Existing packaged mouse diagnostics cover actual solo/cooperative input execution.

Echo labels match Core hazard identity, geometry, remaining time and lifetime, including the zero-countdown tick before resolution. Boss checks cover stable channel identities, authoritative shielding, canonical-boss selection despite same-definition Mirrorborn copies, exact boss-owned cues, bounded resources, pause, reduced effects, finite containment, repeat refresh and restored victory. Reduced effects preserve gameplay signals. Ending checks open and close the actual Story UI and confirm the enabled return to Greyhaven.

Audio checks establish a bounded cue cache, distinct non-silent/unclipped loops, same-process regeneration, Master routing and pause/context cleanup. Rendered checks also exercise peripheral mote motion, pause and room resizing. Visual inspection led to larger echo countdowns and a distinct **BREAK SEAL** instruction below channel names.

## Prism review

Prism's initial Gemini review `8c76d2f4ba44d02b6322ea6bfa1b7bfb` reported two high findings: the rig and actor label independently repeated the Core's shield rule. Both were addressed by the authoritative `Shielded` projection and its damage/restore regression test.

Follow-up review `209ab42db4f7e5955812609bbd5a5ba5` completed on 2026-09-19 with secret redaction enabled. It reported one medium and four low notes, with no actionable defect. The cache note assumes a runtime resource-reload path that the client does not support; the bounded three-cue cache deliberately retains and reuses its live streams across travel. The mesh note assumes simultaneous Breach Heart instances in endgame arenas, but this rig exists only in its campaign encounter and requires independent animated materials. The remaining notes concern the deliberate inclusion of opt-in exact-package diagnostics, diagnostic-only pose comparisons and System.IO reports written to explicit host filesystem paths. These do not identify a current gameplay, portability or lifecycle failure. Reports and finding-by-finding dispositions remain under `artifacts/hollow-night/`.

## Package verification

The exported Mac app passed **1,411 headless checks**: release 33, mouse 83, interactions 17, opening journey 42, Verdant 136, Cinder 182, Spine 211, Hollow 224, services 19, character visuals 170, combat feedback 168 and appearance 126. Its rendered Hollow run passed another **229 checks** and produced **17 captures**, for **1,640 packaged checks**. All rendered assertions, geometry/audio fingerprints, branch counts and the route hash match the final source run. Five rendered-only checks cover mote motion, pause and room resizing.

The full packaged campaign/endgame route completed **32,190 commands**, verified save/replay and produced `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`, matching the previous milestone. Borrowed Memory completed **2,516 commands**, verified save/replay and produced `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`, also unchanged. Export and diagnostic Godot log checks passed.

- Mac build: `artifacts/hollow-night/Ashenwake.zip`.
- SHA-256: `2b95e7a39e17cde0b771bf29602d04d39f425b4a4458544bd4ea419fd8612e2a`.
- Full headless evidence: `artifacts/package/22f02d6da01b.GZCjXk/`.
- Exact-package rendered evidence: `artifacts/hollow-night/package-rendered.1X5QwK/`.
- Consolidated results: `artifacts/hollow-night/verification.json`.

## Evidence and limits

Build, formatting, Core tests, Prism reports, diagnostic JSON, screenshots and the Mac package are stored under `artifacts/hollow-night/`. Reproduction steps are in [The Hollow Night](hollow_night.md).

Regional procedural art now covers Greyhaven and all five campaign acts. These automated software checks do not establish performance on other hardware, final textured assets or independent player acceptance. Cooperative environments retain their earlier presentation.
