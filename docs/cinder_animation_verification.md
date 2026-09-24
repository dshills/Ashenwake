# Cinder animation verification

Local reports and captures are retained under `artifacts/cinder-animation/` (ignored artifacts).

- Solution build succeeds with zero warnings/errors.
- Headless combat-feedback diagnostic: **705 checks passed**.
- Headless Act III diagnostic: **748 checks passed**, including real creature events, all five regional contexts, both vent directions, guard/exposure changes, victory and deterministic replay of 5,229 commands (`cinder-headless-verified/cinder-review.json`).
- Initial packaged native combat-feedback diagnostic: **750 checks passed**. Native pose strips were inspected for distinct anticipation, weapon contact, recovery and forward collapse.
- Final packaged native Act III diagnostic: **761 checks passed** (`cinder-native/cinder-review.json`); final native combat-feedback diagnostic: **750 checks passed** (`feedback-final-native/combat-feedback-smoke.json`). Final captures confirm the emberling detonation/collapse sequence and the arena engine's exposed, shutdown-opening and settled states. Runtime logs and changed-file whitespace verification pass.

Final macOS package SHA-256: `06a43b03d783c6844a848f3010c7d9f4d094cf8fa910c2fb477cc72ac603a950`.

The first campaign diagnostic incorrectly required a living emberling at attack resolution. Core's existing Rusher detonation kills its source in the same tick. The corrected diagnostic gives the first emberling an opportunity to act using ordinary recorded idle inputs, then verifies that death correctly takes priority over attack. Other Cinder families must receive their attack clips. The emberling gallery shows idle, anticipation, detonation and collapse to reflect this behavior.

Creature fixtures cover pause, new-start versus stale-state priority, actor-root isolation, finite terminal poses, 30/144 Hz gait consistency, fixed resources and material isolation. Furnace machinery checks cover immediate authoritative armor/vent poses, finite secondary recoil, staggered mechanical release, monotonic cooling, restored defeat equality and reduced-effects settlement without replay. Existing geometry checks retain arena clearance and bounded lighting.

No Core rules, content definitions, save formats or replay commands changed. No fresh full Core/server test suite, cooperative session or target-hardware performance certification is claimed.

## Prism review

Prism/Gemini used default secret redaction and `tools/prism-implementation.json`.

Initial review `e91d0a67fc9f73e79c2948aac26e4676` reported one medium and one low finding. The combat actor's 1.65-second collapse and the separate arena engine's 3.2-second shutdown have independent owners and lifetimes; completing a CharacterVisual clip never removes the FurnaceSpindleVisual scenery. The latter remains through victory and is exercised to completion by the campaign and detached checks. Synchronizing the durations would not fix an actual truncation. The diagnostic's recursive actor lookup happens only for the first observed resolution of each family, with a maximum of four successful lookups; it is not part of the shipping frame loop. The review also cited an unrelated test file for that lookup.

Follow-up `f11c3fba73799721b89dbb333717b1ed` reviewed the route adjustment and reported high/medium/low findings. Its nondeterminism concern assumed `_creatureAttacks` came from visual callbacks. It is populated synchronously from Core's `Execute` result before capture awaits; no signal or animation-completion callback feeds it. All input commands are recorded and independently replayed. The single observation budget intentionally covers only the first emberling; completed revisits must not restart it. The exact authored fixture IDs and bounded 180-tick allowance are deliberate diagnostic inputs, not production tuning. These findings do not identify an actionable defect in the executed path.

Final complete review `34976ce09d5d21cb6ca15e43f35aa74b` reported **zero high, zero medium and five low findings**. The gait precision concern is already prevented by `AnimateLocomotion`, which wraps `_gaitPhase` modulo one on every update before this code reads it. The Spindle duration fallback is restricted by the four-family `CinderCreature` guard and intentionally yields .8 seconds. The remaining suggestions concern extracting pose coefficients, diagnostic cue strings and the identical .1-second cosmetic delta limits. These follow the existing presentation conventions; no gameplay timing depends on them. No unresolved actionable correctness issue remains from these reviews.

## Reproduce

Source `tools/env.sh`, build the solution, export the macOS package and run with fresh output directories:

```sh
Ashenwake -- --combat-feedback-smoke --capture-combat-feedback --output=/absolute/fresh/feedback
Ashenwake -- --cinder-smoke --capture-cinder --output=/absolute/fresh/cinder
```

Require the generated reports to contain `passed: true`, check the Godot logs and inspect native captures. Keep native diagnostic windows focused during input checks. Add `--headless` before `--` for structural runs without rendered evidence.
