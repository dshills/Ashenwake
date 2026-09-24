# World encounters and Resonance Storms verification

Evidence is retained under `artifacts/world-encounters/`. The milestone adds three optional Grey March encounters and five regional storm incursions using one Core-owned arena lifecycle. Published combat/progression catalogs are unchanged.

## Automated checks

- The solution builds with zero warnings and errors; formatting verification passes.
- All 46 new Core cases pass: 26 combat-mechanic cases, 15 runtime/save/reward cases, and five discipline routes covering 40 battles. Each discipline earns its campaign builds through public commands, then completes all eight fights on the first attempt, including the caravan's harder escort route. These fixtures inject no gear or health bonuses.
- The 26 combat cases passed again after widening damage arithmetic. All 15 runtime cases passed again after making the completion dictionary update explicitly copy its predecessor and asserting that prior captured snapshots remain unchanged.
- Adjacent combat, secret, regional, and replay coverage passed 169 cases. A separate existing optional-encounter/endgame selection passed 76 cases; these selections overlap and should not be added as a unique test count.
- The initial headless client diagnostic passed 445 checks. The final native diagnostic passed **499 checks**, using **59 actual viewport clicks**, **43 rendered captures**, and 10,451 public commands. It earns the campaign route, enters all eight encounters, tests the caravan puzzle and a separate combat branch, completes all rewards, and verifies save/replay and exact source-room return.
- Native checks cover actual death and recovery, retreat before victory, completed but unclaimed treasure, rejected duplicate claims, cancellation, pauses, reduced effects, shipping tutorial suppression, and layouts at 1280 × 800 and 780 × 720. The final native engine log is clean. Screenshots were inspected for the lantern traveler, caravan, shrine, regional storm scenery, combat readability, and compact journal.

The final exported macOS application passed **456 headless encounter checks** and **33 release/pause/recovery checks**, with clean accepted engine logs. `tools/export.sh` now includes the world-encounter diagnostic in future package verification. Shell syntax, staged diff checks, and the export log check pass. The refreshed ZIP SHA-256 is `6db265a98a096f15503abb424b6fdeafdd2c094faab19db1901b01f841c5b457`.

A source marker initially fell inside an Extraction Floor obstacle. The Cinder storm entrance was moved to a reachable position and passed every discipline's route. Rendered inspection caught repeated description text and an opening tutorial hint covering encounter combat; both were corrected and verified in the final native run.

## Ownership and compatibility

World encounters keep a detached combat arena while the source campaign room remains preserved. Completing a fight removes its lingering hazards, projectiles, pending attacks, and statuses. Caravan puzzle completion does not invoke combat-only victory logic. Equipment and material grants share the command rollback boundary and reconcile against permanent completion and reward receipts.

New state is nullable and omitted until explicit encounter entry. Reading nearby encounters does not mutate the character, and historical commands do not automatically create new discoveries. The tests cover saved puzzle steps, wrong and out-of-order choices, capacity denial, restoration at each stage, forged or missing receipts, altered seed/context, and exact replay.

Storms are authored optional incursions, not timed or random changes across a whole campaign region. Their one-time rewards use existing named equipment. The tests establish scripted correctness and bounded playability; they do not replace a human balance or hardware playtest.

## Prism review

Prism/Gemini reviewed the staged implementation with `tools/prism-implementation.json` and default secret redaction. Review reports and per-finding dispositions are retained alongside the diagnostic evidence.

The first two reviews prompted defensive widening of damage multiplier arithmetic and an explicit copied completion dictionary. Current arithmetic was already bounded below Int32 overflow, and snapshots were already deep-copied. The remaining ownership-style observation concerns a production method that mutates a detached `progression.Capture()` candidate before adopting it; it does not mutate historical snapshots or shared network state. Save, replay, ownership, and captured-snapshot tests verify those boundaries.

Full staged reviews: `10ade684a970dd0899890bfc00d31baf` and `15f324890f54690fa724a35df0ae929b`. A later full follow-up returned malformed provider output; a focused final review completed as `9230b733d3c68a01551fd65a6e4eff51`. Its two findings were checked against the surrounding types: `EndgameRuntimeSession.Combat` already routes to `worldArena` during an encounter, and `WorldEncounterState.SchemaVersion` already initializes to 1. Both behaviors are exercised by the earned interaction and save tests. No actionable findings remain.
