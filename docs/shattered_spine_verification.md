# Shattered Spine verification

Validated on 2026-09-18 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `3638d51dd9c3a7bdd12e888af8030dd13bf79bbc`.

## Scope

Act IV now has four distinct bone and law environments, regional floors and obstacle coverings, a state-driven Covenant Warden backdrop, Divine Memory atmosphere and four synthesized ambience loops. The Warden observes the existing defense window and boss-owned oath/fault announcements. The causeway and Memory render sequence numbers from actual fault IDs, including the Memory's reversed positions. Shattered Spine endgame arenas reuse the causeway art, floor and ambience.

## Verified source results

- Solution build: zero warnings or errors. Formatting and `git diff --check` pass.
- Core regression suite: **325 passed**.
- Final rendered diagnostic: **216 checks**, **16 captures**, **5,508 campaign commands** and matching replay.
- The main route completes the Memory after **349 combat ticks**. The independent death branch reaches real death after **640 movement ticks**. Departure and death both remove scoped rules, numbered warnings and Memory ambience, restore the recorded regional context and reproduce by replay.
- The independent Warden branch observes both actual fault lanes after **247 ordinary stationary/potion ticks**, with no deaths and no attacks. It verifies exact presentation signals and replay, then restores the main route unchanged.
- Static architecture uses **5–10 palette batches** per setting; each ground uses **7**. Floor tops are **9 mm below Y=0**. The Warden uses 14 articulated nodes, at most 100 mesh objects and a pool of 16 transient glyphs.
- Source route hash: `A9F80F66612BCA280F92875D2D14DFEF0655C6FAF24C645A8A1DF7A2ADE72B79`.
- Final source evidence: `artifacts/shattered-spine/final-rendered.u4X9IZ/`. Earlier failing diagnostics remain available as development evidence.

## Verification method

The focused diagnostic progresses through actual campaign commands, completes Act IV, returns to Greyhaven and revisits the completed region. Independently restored branches test Memory departure, ordinary incoming damage until death, and both Warden fault announcements. Every branch checks replay and preserves the main session's state. No combat state is fabricated to force a presentation result.

Geometry checks inspect transformed mesh vertices: tall architecture stays outside the arena, floors stay below gameplay warnings, and obstacle skins remain inside their Core footprints. The shipping click-move planner traverses around each room's obstacles. Real viewport clicks test floor picking, the mint destination ring and X cancellation in every context; those viewport checks keep combat stationary. Existing packaged mouse diagnostics cover actual solo/co-op input execution.

Warden checks cover authoritative defense, boss-owned warning direction, bounded articulation and transient glyphs, finite victory, pause, reduced effects, repeated refresh and restored victory. The sequence-label checks match IDs, lane positions, numeric order and warning lifetime, including the zero-countdown tick before resolution. Reduced effects preserve all gameplay signals. Audio checks establish bounded cache size, distinct non-silent/unclipped loops, same-process regeneration, Master routing and pause/context cleanup. Rendered checks additionally exercise dust motion, pause, perimeter placement and room resizing.

Rendered inspection prompted two refinements: the Memory floor was darkened to separate yellow fault warnings from the warmer sanctuary, and the Warden's forty-two inscription objects were merged into two meshes. The inscriptions retain their exact geometry and shared animated material.

## Prism review

The user explicitly approved Prism and unredacted source-diff transmission to its Gemini provider. Initial review `94090c7c11a63f41ce60fe21403a0cf0` reported two medium notes and no high findings. Both concern a hypothetical center/origin-crossing Warden fault. The Core mechanic only creates the Warden's own `campaign.covenant_fault` at Z=-2500 or Z=2500, with both endpoints at the same Z. The rig filters by content ID and boss source; the sign of the endpoint sum is exactly the sign of the midpoint. The three-lane causeway/Memory faults are a different mechanic and retain their numbered floor warnings. Neither note identifies an execution path in the current game. Follow-up review `1077a93d4adddbe2e9bca7cebda44b7c` reported one high and one medium note. The high note conditionally warns about uncached label allocation, but the existing implementation already retrieves each warning line from `_effects` by hazard ID, retrieves the label from that cached line and creates either only when absent. `EndEffects` removes expired lines and their child labels; room cleanup also frees them. Runtime checks establish exact live-label counts and departure/death cleanup. The medium note repeats the lane concern: the room center is the origin, so the midpoint sign already uses the correct frame. No actionable findings remain. Full reports and dispositions are recorded under `artifacts/shattered-spine/`.

## Package verification

The exported Mac app passed **1,187 headless checks**: release 33, mouse 83, interactions 17, opening journey 42, Verdant 136, Cinder 182, Spine 211, services 19, character visuals 170, combat feedback 168 and appearance 126. Its rendered Spine run passed another **216 checks** and produced **16 captures**, for **1,403 packaged checks**. All assertion results, geometry/audio fingerprints, branch counts and the route hash match the final source run. Five rendered-only checks cover dust motion, pause and room-bound resizing.

The full packaged campaign/endgame route completed **32,190 commands**, verified save/replay and produced `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`, matching the previous milestone. Borrowed Memory completed **2,516 commands**, verified save/replay and produced `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`, also unchanged. Export and diagnostic Godot log checks passed.

- Mac build: `artifacts/shattered-spine/Ashenwake.zip`.
- SHA-256: `5742e899a95bf2048daa17f3e38ca26a72a2942c9cdd5effe1366c826d0abc50`.
- Full headless evidence: `artifacts/package/22f02d6da01b.ykvuuh/`.
- Exact-package rendered evidence: `artifacts/shattered-spine/package-rendered.cfqUEg/`.
- Consolidated results: `artifacts/shattered-spine/verification.json`.

## Evidence and limits

Build, formatting, Core tests, Prism reports, diagnostic JSON, screenshots and the Mac package are stored under `artifacts/shattered-spine/`.

This milestone changes presentation and diagnostics. Core rules, persistent state, replay commands and gameplay random streams are unchanged. These are procedural assets and automated software checks; they do not establish performance on other hardware, final textured assets or independent player acceptance. Cooperative environments and Act V remain outside this pass. Reproduction steps are in [The Shattered Spine](shattered_spine.md).
