# Visible progression verification

Verified on 2026-09-18 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `67d4bc7c29a4a11d968c09ec4578f237c86e5681`.

## Scope and checks

The pass adds four visible equipment slots, Ashcleaver evolution art, four combinable manifestation forms, a rotating equipment preview, explicit equipment actions, and recognizable ground loot. Filters update while paused; changing the authored room restores actors and drops immediately. The Core awakening projection is nonserialized and shares the existing rule with crafting; it changes neither the awakening requirement nor the save schema.

- Solution build: zero warnings or errors. Formatting verification and `git diff --check` passed.
- Core suite: **312 passed**, including the 999/1000 awakening boundary, crafting, and nonserialization assertion.
- Appearance diagnostic: **126 headless checks** and **134 rendered checks**, including eight captures from the exported Mac app.
- Full exported regression suite: **575 checks** — release 33, interaction 17, journey 42, services 19, original character visuals 170, combat feedback 168, and appearance 126. The export verifier and its Godot log checks completed successfully.
- Campaign/endgame route: **32,190 commands**, save/replay verified, final state hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`.
- Borrowed Memory route: **2,516 commands**, save/replay verified, final state hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both route hashes match the preceding combat-feedback build.
- Equipment checks cover the five disciplines, individual empty slots, weapon/chest variants, all four Ashcleaver states, and isolated materials. Manifestation checks cover individual and combined forms, pause, reduced effects, root transforms and resource reuse.
- The real progression route inspects without changing state, rotates while paused, explicitly unequips/equips, enforces service range in the UI, completes the dungeon with a fragment manifestation, filters and collects real drops, returns to town, saves, reloads and replays.
- Rendered inspection covered the 1280×800 and 1280×720 equipment panel, a compact 780×800 layout, the four manifestations, and the loot catalog. The compact test concerns the new panel; it does not certify the older global HUD at that width.
- Exported co-op regression: **1,017 matching authoritative snapshots, zero mismatches, ten personal reward receipts** using two authenticated local WebSocket peers.

## Prism review

Prism used Gemini with `tools/prism-implementation.json`. Both raw reviews are retained under `artifacts/visible-progression`; neither is represented as a zero-finding result.

The initial staged review (`247e2124396f2cb5c8361f6d81a674b2`) returned one high and one medium finding. Both were addressed:

1. The client no longer calculates awakening with its own kill threshold. It reads `PermanentItem.Awakened`, backed by Core's `GodwroughtProgress` rule. The new computed property is ignored by JSON serialization.
2. Loot art classification now uses exact slot/item pairs for authored variants, with generic slot defaults for unmapped content.

The focused follow-up (`65b312b4f1cb1ae4629528ff9290f7dc`) returned one high and one medium suggestion. Manual assessment found no remaining current-content defect:

1. **Restore generic substring classification:** this reverses the original recommendation and assumes additional axe/hammer/robe definitions. The complete item catalog in `content/combat.json` has thirteen relevant weapon/off-hand/chest definitions; all named variants are mapped and the starter/plate defaults are intentional. Campaign and endgame combat files add no item definitions. Unknown future definitions use slot defaults until authored art is added.
2. **Generalize the Ashcleaver ID:** Ashcleaver is the sole supported Godwrought item in the existing kill tracking, grafting, validation and production projection. A tag system for additional awakenable items is a future rules/content extension. The property preserves current behavior.

Full dispositions and patch inputs are saved in `prism-disposition.json`, `reviewed.patch`, and `reviewed-followup.patch` beside the raw review reports.

## Artifacts and limits

- Playable package: `artifacts/visible-progression/Ashenwake.zip`.
- Package SHA-256: `03c230b8d5e48fcb38282d505052fa76842ba3974f63595e1aca38a07b825655`.
- Exact-package rendered evidence: `artifacts/visible-progression/package-appearance.TgsFH8/`.
- Build, formatting, Core tests, package route and co-op logs: `artifacts/visible-progression/`.

The eight remaining equipment slots affect stats without separate body attachments. Cooperative clients retain fixed equipment and their existing ground-drop presentation. This is procedural art and software regression evidence, not hardware performance certification, final texture/LOD work, or an external player study. Reproduction steps and controls are in [Visible character progression](visible_progression.md).
