# Verdant animation verification

Local evidence is retained under `artifacts/verdant-animation/` (ignored artifacts).

- Solution build succeeds with zero warnings/errors.
- Headless combat-feedback diagnostic: 579 checks passed, covering the six selected creature rigs, existing player/weapon feedback and Rootheart scenery transitions.
- Final packaged native combat-feedback diagnostic: **620 checks passed**; native Act II diagnostic: **570 checks passed**. Reports are in `feedback-final-native/combat-feedback-smoke.json` and `verdant-final-native/verdant-review.json`.
- Broader headless character/geometry diagnostic: **170 checks passed**.
- Initial headless Act II diagnostic: 488 checks passed. Real campaign commands complete the region, observe live attacks from vines, swarms, carriers, the Antler and Rootheart, exercise feeding-root loss and victory, and reproduce the campaign through replay.

The Act II diagnostic now forwards every combat event batch through the production presentation adapter, not only batches coinciding with a scenery-state change. It checks each creature's attack cue immediately after dispatch, before any capture wait can legitimately expire the cosmetic clip. Native screenshots are complementary visual evidence; they do not establish independent player acceptance or a target-hardware performance certification.

Creature checks cover actor-root isolation, immediate contact, stale-view/new-start priority, readable tells under damage, pause, distinct finite death poses, 30/144 Hz gait consistency, unchanged node/mesh instance IDs and bounded physics-free geometry. Rootheart scenery checks cover smooth root release, seed recoil, repeated snapshot idempotence, exposed breathing, staggered petals, delayed growth, restored-victory equality and reduced-effects settlement while paused. Existing depth diagnostics verify that the animated flower remains beyond the arena wall.

No Core rules, content definitions, rewards, save formats or replay commands changed. No fresh full Core/server suite or cooperative network session is claimed for this presentation pass.

## Prism review

Prism/Gemini used default secret redaction and `tools/prism-implementation.json`. Initial report `b894dd84728d4facd1dd1f42fd770193` contained one medium and one low finding. Animation now reads cached joint enums instead of comparing motion names each frame, and all creature rigs have a dedicated defensive tuck rather than inheriting the generic dodge pose.

Follow-up `223f70889e40a64d11c05bd87bfa3e76` flagged a separate cached-type array as a synchronization risk. The final implementation stores that type directly on the immutable `Limb` record, eliminating the parallel array. Existing death snapshot arrays remain allocated by `CaptureRigBounds` after construction; creature limbs are not added or removed during animation, as the instance-identity checks verify. The mapping from existing procedural joint names runs once at limb construction, not through a changing imported-asset pipeline.

The forward death-pitch sign is intentional and was selected after native image inspection: the earlier positive pitch raised faces toward the sky. Plant stems now fold forward over anchored ground tendrils; the Antler lowers its head into its collapse. Cosmetic cue priority remains in presentation because it selects visible poses only. It neither rejects Core commands nor changes attack results; moving cosmetic clip blending into Core would violate that separation.

Final complete review `d1c995a8f75763f490094149b83a2c8f` reported one high and one medium finding. Both were checked against the call path. Enemy **role** and player **discipline** are separate arguments to `CharacterVisual.Create` and `ConfigureAnimation`; the creature selector receives the discipline, which is empty for these enemies. All six fixtures use their authored roles and assert the selected creature family, and the production Act II route confirms actual attacks reach those rigs. Removing the player guard would not fix an enemy-routing problem. The selector also runs once during model construction and stores an enum; it does not run per frame or allocate motion objects. No unresolved actionable finding remains from these reviews.

Native captures were inspected for anticipation/contact/recovery, grounded plant bases, forward Rootheart collapse, the Antler's lowered head, and the arena flower's settled victory. Package SHA-256: `c05c6ae80ed465ea00d439d531640bb968d89be9bd6b35cf47d11cd8715aaf2a`.

## Reproduce

Source `tools/env.sh`, build the solution, export the macOS package and run with fresh output directories:

```sh
Ashenwake -- --combat-feedback-smoke --capture-combat-feedback --output=/absolute/fresh/feedback
Ashenwake -- --verdant-smoke --capture-verdant --output=/absolute/fresh/verdant
```

Require the generated `combat-feedback-smoke.json` and `verdant-review.json` reports to say `passed: true`, then inspect their native captures. Keep native diagnostic windows focused during input checks. Add `--headless` before `--` for structural runs without rendered evidence.
