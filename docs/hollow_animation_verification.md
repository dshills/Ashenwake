# Hollow Night animation verification

Local evidence is retained under `artifacts/hollow-animation/` (ignored artifacts).

- Solution build: zero warnings/errors.
- Headless combat-feedback diagnostic: **934 checks passed** (`feedback-headless/combat-feedback-smoke.json`).
- Headless Act V campaign diagnostic: **1,260 checks passed** (`hollow-headless/hollow-review.json`), including all three phases, all seal-channel counts, warning sequences, cleanup branches, the ending, final return and replay of 8,608 commands.
- Initial packaged native combat-feedback diagnostic: **988 checks passed** (`feedback-native/combat-feedback-smoke.json`).
- Final headless combat-feedback diagnostic after the rotation correction: **935 checks passed** (`feedback-final-headless/combat-feedback-smoke.json`).
- Final packaged native combat-feedback diagnostic: **989 checks passed** (`feedback-final-native/combat-feedback-smoke.json`).
- Final packaged native Act V diagnostic: **1,318 checks passed** (`hollow-final-native/hollow-review.json`). It records 8,608 commands and matches the initial headless final state hash: `56079ACDA70F83D860D86694787BF45BB1BB86D84E2C57447DA3995459E80960`.

Changed-file whitespace verification and completed runtime log checks pass. Final macOS package SHA-256: `42cfcb2850e3fcf3cc467675e815617120e7e1c7f9c42e44b39e8240d79bb05c`.

Native captures were inspected for all three creature families' preparation/contact/recovery, contracting terminal poses, the live phase-three boss, containment opening and the settled surviving wound. The final runtime logs pass `tools/check-godot-log.py`.

All three creature families have checks for authored selection, idle/travel poses, immediate contact, new-start versus stale-state priority, recovery, defensive reactions, pause, finite terminal death, actor-root isolation, fixed resources and 30/144 Hz gait consistency. Breach exposure follows the actor's `Shielded` projection, with separate checks for posture, paused transitions and shared-material isolation.

The contracting death effect changes only the cosmetic BodyRoot scale. It does not alter actor collision or shared material opacity. Death remains terminal on that rig; the existing production synchronizer rebuilds a dying rig if its actor becomes alive again. Core's normal boss phase change keeps the actor alive and emits a separate phase effect, rather than invoking death.

The breach monument has 26 detached checks for shield reactions, stable channel slots, immediate warning signals, repeated snapshots, staggered containment, pause, invalid deltas, monotonic fading, restored-defeat equality, reduced-effects settlement without replay, unchanged resources and wall clearance. Its final wound remains visible. The actor's 1.9-second death and the separate scenery's 3.4-second containment have independent owners and lifetimes.

The Act V diagnostic forwards every event batch through the production adapter and checks each family's first attack resolution before capture awaits. Gallery strips provide complementary visual evidence of preparation, contact, recovery and collapse. No Core rules, rewards, save formats, random streams or replay command formats change. No fresh full Core/server suite, co-op network session or target-hardware performance certification is claimed for this presentation pass.

## Prism review

Prism/Gemini reviewed the staged implementation with default secret redaction and `tools/prism-implementation.json`. Initial report `04830ab6d6202d19f39f1216fc68dc9c` contained one high, one medium and two low findings.

The rotation finding identified a valid angle-wrap risk: a shutter authored at -300 degrees can return an equivalent +60-degree Euler angle after a basis round trip. Containment now uses the shortest per-axis hinge path, preserving exact start/end poses. A regression normalizes the shutter rotations and measures the accumulated quaternion arc, requiring the intended 24-degree closure rather than a full turn.

The high finding assumed scene-discovered node counts could differ from the snapshot arrays. This rig constructs its own private readonly arrays of exactly three rings/channels and six shutters, fills them with loops over those same lengths and never discovers an arbitrary scene topology. Its resource-identity and budget checks cover those fixed counts. The reduced-effects finding is addressed by the existing early branch in `Animate`, which settles an in-progress containment even while paused; that behavior is exercised directly. Delta clamping affects cosmetic motion only; it cannot slow Core, change a warning deadline or desynchronize authoritative combat.

Follow-up `8f311963b74b0e72006df978b3bc7424` reviewed the correction and regression, reporting zero high, one medium and one low finding. Its Euler-order concern applies to a hypothetical node using another rotation order; these privately constructed shutters keep Godot's default order, matching `GetEuler()`, and rotate only about Z. The regression additionally verifies the physical pose and traveled arc. The suggested radian conversion is a style preference: `(180 / Mathf.Pi)` correctly converts the vector, while the C# `Mathf.RadToDeg` API is a scalar method rather than a multiplier constant. No unresolved actionable defect remains from the reviews.

## Reproduce

Source `tools/env.sh`, build the solution, export the macOS package and run with fresh output directories:

```sh
Ashenwake -- --combat-feedback-smoke --capture-combat-feedback --output=/absolute/fresh/feedback
Ashenwake -- --hollow-smoke --capture-hollow --output=/absolute/fresh/hollow
```

Require `passed: true` in the generated reports, check runtime logs and inspect native captures. Keep native diagnostic windows focused during input checks. Add `--headless` before `--` for structural checks without rendered evidence.
