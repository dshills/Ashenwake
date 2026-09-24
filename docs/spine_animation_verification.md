# Shattered Spine animation verification

Local evidence is retained under `artifacts/spine-animation/` (ignored artifacts).

- Solution build: zero warnings/errors.
- Headless combat-feedback diagnostic: **832 checks passed** (`feedback-headless/combat-feedback-smoke.json`).
- Headless Act IV campaign diagnostic: **963 checks passed** (`spine-headless/spine-review.json`), including real attack resolutions for all four families, both authoritative fault lanes, oath marks, Divine Memory branches, victory and replay.
- Packaged native combat-feedback diagnostic: **882 checks passed** (`feedback-native/combat-feedback-smoke.json`).
- Packaged native Act IV diagnostic: **976 checks passed** (`spine-native/spine-review.json`). Both campaign runs record 6,797 commands and finish with state hash `C4AF765603C8ECA8231436B4F6DDF9A81C796D7D6A776378D24785009173AB1B`.

Native images were inspected for all four families' preparation, contact, recovery and collapse; the keeper's book/staff gesture; and the Warden's guarded, exposed, release-opening and settled monument. Runtime log checks and changed-file whitespace verification pass. Package SHA-256: `29b6da7cda71ce9a0d438e8089a60e262b76b27beea09d602a0798348689e98a`.

The four new creature fixtures cover exact authored selection, idle and travel poses, immediate contact, new-start versus stale-state priority, recovery, defensive reactions, pause, finite terminal death, actor-root isolation, fixed node/mesh identities and 30/144 Hz gait consistency. Warden exposure is checked for distinct body poses, paused transitions and shared-material isolation. The same exposure checks still cover Furnace Spindle.

The Warden monument has 25 additional detached checks for immediate guard/oath/fault signals, staggered shield recoil, snapshot idempotence, invalid delta handling, seal-before-shield-before-tablet release, pause, monotonic fading, restored-defeat equality and reduced-effects settlement without replay. Transformed vertices remain behind the court wall; existing lighting and resource budgets are retained.

The combat actor's 1.7-second collapse and the separate monument's 3.2-second release have independent owners. Finishing an actor clip does not remove the monument. Creature pose clips only update existing transforms; room effects update their owned materials. No Core rules, content definitions, save formats, random streams or replay command formats change. No fresh full Core/server suite, co-op network session or target-hardware performance certification is claimed for this presentation pass.

The Act IV diagnostic forwards every combat event batch through the production adapter and checks the first actual attack resolution of each family before waiting for captures. Support wards and room hazards retain the adapter's existing separate effects. Gallery strips provide complementary rendered evidence of anticipation, contact, recovery and collapse.

## Prism review

Prism/Gemini reviewed the staged implementation with default secret redaction and `tools/prism-implementation.json`. Report `c9e0abdcf2caacd3be203a56b1d7259b` contained zero high, one medium and one low finding.

The medium finding asked whether observing `ActiveCue` immediately after dispatch could race with the next animation frame. `CharacterVisual.React` assigns `_cue = incoming` synchronously, and `ActiveCue` reads that field directly. `PresentCombatEvents` calls `React` before returning. The diagnostic therefore intentionally checks before its first capture await; waiting for a frame would add unnecessary timing dependence. All four real campaign resolutions pass this check. The review's cited `CombatPresenter.cs` location is not part of this change.

The low finding suggested centralizing the monument's light-range values with the tests. Existing live values remain 4.2/5 meters and the settled value remains 2.8 meters; the new checks enforce those bounds alongside existing depth checks. Independent expected bounds in the diagnostic are intentional. No actionable defect remains from the review.

## Reproduce

Source `tools/env.sh`, build the solution, export the macOS package and run with fresh output directories:

```sh
Ashenwake -- --combat-feedback-smoke --capture-combat-feedback --output=/absolute/fresh/feedback
Ashenwake -- --spine-smoke --capture-spine --output=/absolute/fresh/spine
```

Require `passed: true` in the generated reports, check runtime logs and inspect native captures. Keep native diagnostic windows focused during input checks. Add `--headless` before `--` for structural runs without rendered evidence.
