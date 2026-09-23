# Weapon animation verification

Evidence lives under `artifacts/weapon-animation/` (local, ignored). This is a presentation-only change; no Core rules, content definitions, saves or replay formats changed.

## Automated and rendered checks

- Final headless combat-feedback suite: 412 checks passed. Final packaged native combat-feedback suite: 449 checks passed (`feedback-release-native/combat-feedback-smoke.json`).
- Final packaged native Journey: 464 checks passed (`journey-release-native/journey-review.json`), including the corrected resolved attack path, actual combat, save restoration and replay agreement.
- Solution build: zero warnings/errors. Whitespace verification and Git diff checks pass.
- Equipment/appearance headless suite: 743 checks passed, including equipment replacement and preview isolation.
- Final rendered weapon gallery: checked actual weapon contact, downward heavy follow-through, settling and idle poses. The first gallery exposed upright heavy contact poses; the final version articulates the existing equipment module around the hand grip and includes that transform in its effect anchor.
- Headless Journey: 455 checks passed, including real opening combat, event feedback, navigation, save restoration and replay agreement. The first native Journey attempt exited without its final report and is not counted as a pass.
- Animation checks cover seven equipped weapon families, four common Act I enemy rigs, spells with a nonstaff weapon, equipped/absent shields, immediate resolved contact, return to idle grip, cue priority, pause, death, root isolation, and stable geometry counts.
- Locomotion checks compare 30 Hz and 144 Hz rendering, planted idle feet, shortest-path turning, bounded large displacements and unchanged actor transforms.
- Attached-effect checks cover nested/transformed actor and effect parents, moving contacts, pause, dodge interruption, reduced effects, removed actors, bounded recycling and reuse for ordinary target impacts.

No fresh full Core/server suite, cooperative network session or target-hardware performance certification is claimed for this cosmetic pass. Co-op and roaming sightings convert their rendered displacement to the rig's documented per-Core-tick movement units.

## Prism review

Prism/Gemini ran with default secret redaction and the project's implementation rules.

The initial review (`45ddd6347fc4adb3baaaa7b49396ea23`) reported one medium and one low finding. The redundant `MaxBy` search was removed: `EmitBurst` returns the configured burst directly. The empty-pool recycling hypothesis does not follow the execution path: an empty pool first creates and assigns a burst under the `Count < Maximum` branch; `MinBy` is reached only when all 40 existing bursts are active. Cold-start and saturation diagnostics exercise both paths.

The complete follow-up (`d6c31eee2fa46531c0725a5ae0f8a1aa`) reported two medium findings. Grip resolution runs before the anticipation method's windup early return, and that method is called on every unpaused living frame, including instant attacks. The instant-contact fixtures run without any preceding windup and verify the articulated contact. Moving this lookup into configuration would be incorrect because equipment modules are built after animation configuration.

Attack durations describe cosmetic follow-through after the authoritative resolved event, not a second attack clock. They never schedule damage or change cooldowns. New attacks restart the cue, dodge/death supersede it, and `BeginAttackWindup`, dispatched from authoritative start/warning events, cancels any lingering cosmetic attack or recoil. This prevents follow-through from hiding the next tell while retaining weapon-specific recovery appearance.

The safeguard review (`2f790141322adf460ad79c52571da13e`) reported one medium and one low finding, with no high findings. Its cancellation concern proved real in the native Journey: Core clears the player's pending attack after `MovePlayer` has already retained its Windup display state for that tick. A blanket `windup` check erased the just-resolved cue. The final fix uses explicit AbilityStarted, EliteAbilityStarted, BossPatternStarted and CampaignHazardWarned events to interrupt cosmetic follow-through, while preserving resolved contact despite an older Windup view. Regression checks cover both paths. The proposed abstraction for hypothetical future cue types is deferred: the complete enum currently contains None, Hit, Attack, Dodge and Death, and the explicit two-cue condition preserves dodge/death priority.

The final event-priority review (`35e03fedf82fbe9b48bdadd221d6884f`) reported one medium and one low finding, with no high findings. Suppressing cosmetic flinch during a live tell is an existing readability contract: damage still produces target sparks, combat labels and health changes, while the attack warning remains readable. Removing that guard would suggest an interruption that Core did not grant. The session-boundary event filter is also existing behavior; this change adds EliteAbilityStarted to both its stale-event filter and start dispatcher. Converting that bounded pattern to a new metadata system is outside this presentation change.

Package SHA-256: `bebea9df6fe1bd27c1af8f799c0ce3e455bb7a4d81da54784108b8a81a3cf3f2`.

## Reproduction

After building with the pinned environment from `tools/env.sh`, run the packaged app with fresh output directories:

```sh
Ashenwake -- --combat-feedback-smoke --capture-combat-feedback --output=/absolute/fresh/feedback
Ashenwake --headless -- --appearance-smoke --output=/absolute/fresh/appearance
Ashenwake --headless -- --journey-smoke --output=/absolute/fresh/journey
```

Require each diagnostic's JSON report to have `passed: true`; a zero process exit alone is insufficient. Inspect captured images only after a native input diagnostic finishes, and keep its window focused during the run.
