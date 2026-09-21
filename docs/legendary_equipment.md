# Legendary equipment

Three named legendary items add combat effects, distinct equipped meshes and inventory icons, lore, power descriptions, and source hints. They work through normal loot pickup, inventory drag/drop, and Greyhaven crafting.

| Item | Power | Campaign source | Repeatable source |
| --- | --- | --- | --- |
| **Pyrebound Treads** · Boots | A moving dodge leaves three burning patches for two seconds. Each patch pulses for 6 Fire damage and applies Burning. | Clear the Act I Road ambush. | Grey March Fracture final room. |
| **Oathkeeper’s Reprisal** · Chest | Enemy damage absorbed by your barrier stores up to 60 charge for eight seconds. Your next successful melee hit releases a 2.4m Crush shockwave. | Defeat the Act IV Covenant Warden. | Shattered Spine Fracture final room. |
| **Widow’s Last Echo** · Gloves | After a dodge, your first projectile skill within five seconds fires a delayed Void echo at half its base damage. | Defeat Act II Rootheart. | Verdant Maw Fracture final room. |

Clear the encounter and collect its legendary ground drop. The early Road reward introduces the system without requiring a completed campaign. Fracture rewards let existing completed characters obtain and farm these items. Each replaces the final eligible enemy's ordinary drop, using the existing two loot RNG draws; ordinary enemies retain their previous item pool. Enemy illusion copies, mechanisms and resurrected enemies do not generate extra rewards. Repeating a completed Fracture may award another item instance, including a spare of an owned legendary for extraction or a better roll.

Return to Torren in Greyhaven and drag a legendary onto its matching equipment slot to activate its innate power. Once extraction and engraving services are unlocked, extract a spare to learn its property, then engrave it on an eligible item: **Wake of Embers** on Boots/Legs, **Debt of Iron** on Chest/OffHand, or **The Second Shadow** on Gloves/Amulet. Normal costs, destructive-extraction confirmation, item ownership, and engraving restrictions apply. Wearing and engraving the same power does not multiply it.

Oathkeeper needs a barrier source, such as Iron Guard or the Guard engraving. Widow works with projectile skills; it does not duplicate melee attacks, summons, or projectile forks/chains. Its half-damage value uses the skill's base damage, excluding the original hit's equipment bonuses and critical strike. Existing offense, resistances, and other general damage rules still apply.

All three use bounded authoritative Core effects. Oathkeeper consumes its charge once per melee action; Widow consumes one token and delays the echo by six simulation ticks. Their damage cannot recursively activate direct-hit powers. Charge/readiness expires, and encounter transitions, death, or removing the corresponding equipped power clear its transient state/effects. Existing save/replay serialization includes active charge, pending echoes, and fire patches.

## Compatibility

The catalog migration reconstructs the previous item/property catalog by removing exactly these three additions. It validates the original archive checksum and logical state against that catalog before rebinding content identities, then validates the resulting state against the current catalog. Character inventory, rolls, progression, profile, campaign, active encounters, and deterministic random state are retained. Unknown or incompatible catalogs remain errors. Loading does not rewrite the source file; subsequent saves retain the original validated bytes as the backup.

The prior maintained save fixtures remain unchanged. Frozen replay catalogs continue to use their previous ordinary drops; new legendary rewards require the new definitions.

## Verification

Artifacts for this milestone are under `artifacts/legendary-builds/`. Independent review caught an authored-catalog edge case: a catalog containing only exclusive rewards passed validation but left the ordinary drop pool empty. The validator now requires an eligible ordinary item; a regression rejects that catalog before combat begins.

Prism’s first pass reported two low-priority suggestions. The damage concern was checked against `Resolve`, which passes only skill damage and mutation scaling to `LaunchWidow`; equipment bonuses are applied later in `ApplyHit` and exclude the echo. A regression verifies that adding 400 flat damage leaves both the echo’s stored damage and its hit at 12. The spatial-query suggestion does not require a change: Reprisal consumes its charge before its single 2.4m query, and the simulation caps actors at 160. No unbounded per-hit scan was introduced.

Readiness appears above the combat HUD, separate from world-space damage labels. Pyre has visible ground rings, Widow has a pale projectile wake, and Reprisal has a golden burst. Power cues use bounded shared effects/audio pools and respect reduced effects and pause.

The second Prism pass raised an ownership-duplicate concern and suggested enabling Widow forks/chains. Both conflict with the intended rules: repeatable Fractures deliberately award new instances for farming/extraction, while only enemy illusion copies are excluded from kill rewards; the spectral echo deliberately has zero fork, chain, and pierce values. Tests exercise the reward sources and these projectile limits. No actionable Prism findings remain.

Final verification: solution build has zero warnings/errors; formatting and authored-content validation pass. **514 Core tests and 17 server tests pass.** The final macOS package passes **1,186 rendered checks**: Appearance 596, CombatFeedback 245, Crafting 91, HUD 108, and FrontMenu 146. New armor, all power effects/readiness, compact descriptions, and inventory/crafting screenshots were inspected. Export and runtime logs pass the Godot log checker. Final package evidence: `artifacts/legendary-builds/package-final.vhieI8/`. The refreshed playable app is `artifacts/export/macos/Ashenwake.app`.
