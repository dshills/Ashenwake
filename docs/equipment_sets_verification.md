# Equipment sets verification

Evidence is retained under `artifacts/equipment-sets/`. The milestone adds six named Legendary pieces, three equipment-derived combat bonuses, repeatable replacement sources, matching armor and icons, and equipment/collection guidance.

## Automated checks

- 94 targeted content, source, collection and historical migration cases passed. New tests freeze the exact predecessor catalogs and exercise active World Encounters, storms, puzzle progress, victory, claimed rewards, retained rooms and corrupt-primary recovery.
- 72 combat/projection/training cases passed. This includes 45 new set cases covering all five disciplines' public pickup/equip/unequip projections, actual dodge-to-trail replay, counter damage and RNG preservation, poison ownership, the eight-companion cap, wall clipping, cooldowns, death and equipment cleanup, forged timers/areas, and training attribution/reset.
- A broader historical migration, Legendary, World Encounter and campaign selection passed 432 cases. One existing Furnace reward test assumed the ground-item count would remain unchanged when using the expanded catalog. It was corrected to verify exactly the additional authored set drop while retaining the original RNG assertion. All six cases of that method then passed independently. The original broad run is retained with its one failure; it was not represented as an entirely passing run.
- After simplifying the ember-trail endpoint during review, all 45 set combat/projection cases passed again. These selections overlap and are not a unique combined test total.

The first native diagnostic passed 315 checks, including 12 actual viewport equipment drags, eight collection clicks, 27 rendered captures and 8,965 public campaign commands. It earns the six pieces through ordinary campaign play, checks collection tracking and partner navigation, completes and breaks every set through equipment swaps, and verifies permanent saves/replays. Comparisons and the journal are checked at 1280 × 800 and 780 × 720. Cosmetic checks cover all six slots across all five disciplines. Screenshots were inspected for set silhouettes, distinct icons, readable effects and compact comparisons. A cramped possessive name and low-contrast leaf icons were improved after that run.

The final native run passed **341 checks**, with **34 rendered captures**, eight collection clicks and 12 real equipment drags. Additional explicitly detached presentation fixtures use real Core hit/status processing to exercise all three readiness/trigger messages, orange ember patches, green thorn geometry, reduced effects and expiry cleanup. They do not advance the earned character or its replay; the subsequent real-character save/load/replay still passes. These fixtures are rendering evidence, separate from the ordinary campaign acquisition proof.

An intermediate repeat failed because the native comparison hover had not settled after dragging. The diagnostic now uses bounded real pointer enter/exit retries with fresh item rectangles and window focus; it still requires the shipping tooltip to appear and never invokes tooltip internals. The final native log is clean. Full solution build, authoring validation, formatting verification, export-script syntax and staged whitespace checks pass.

The exact final macOS export passed **307 packaged equipment checks** and **33 packaged release/pause/recovery checks**, with clean accepted engine logs. `tools/export.sh` runs the equipment diagnostic in future package verification. The refreshed local game is `artifacts/export/Ashenwake.zip`, SHA-256 `5e92d0f625e0c537fe181829f969b3b792e1136cc364ce070e01dff578225af4`.

## Review

Prism/Gemini reviewed the staged implementation with `tools/prism-implementation.json` and default secret redaction. Initial review `caa3e9430388fe856923a8c650e94f06` reported two findings:

- The proposed generation-order issue does not apply: `LegendaryCatalogMigration.TryPrevious` and `PreviousPolicy` identify the newest present generation by its item/power IDs, remove it, then try older authenticated catalogs. No generation index is serialized or used for reward grants. Prepending is required to reconstruct the immediately preceding catalog. The fixed predecessor hashes and historical migration cases exercise this behavior; appending would be incorrect.
- The new trail calculation redundantly floored a square root before passing its distance to the established `Toward` helper. It now calls `Toward(start, target.Position, 2400)`, which already clamps at the target. This removes the redundant calculation and a possible one-unit undershoot for nearby targets. The existing movement helper and pinned-runtime replay contract remain unchanged. All 45 relevant cases passed after the change.

Follow-up review `15c7fcb2d372efd4a3ee6469373c3865` included the surrounding migration implementation and raised five further observations. Skill validation was moved behind the prepared-set guard and uses a lazy per-session ID lookup. Thorn meshes and materials now share cached resources. Two UI observations concern at most 21 already-materialized catalog cards and slicing immutable, authored `item.*` IDs; neither exposes an unbounded loop or player-controlled invalid ID. The remaining observation proposed preserving a prepared trail when the area budget is full. The intended rule consumes the triggered attempt and starts its cooldown even when walls or the shared budget prevent patches, preventing repeated retry triggers; the zero-patch result is reported honestly and has explicit boundary coverage.

The final cache and full-budget follow-up passed all 46 set combat/projection cases. Collection partner navigation also switches to the Sets filter so a Collected/Missing filter cannot hide the chosen partner.

The combat renderer also discards set events belonging to an arena replaced in the same command batch, matching the existing Legendary event boundary rule.

## Scope

Set effects are authoritative in Core; UI previews and collection tracking cannot grant equipment or activate a bonus. Collections remember discovery separately from ownership and equipment. New set state is omitted when unused, and all old published catalog fixtures remain byte-for-byte intact. No new retroactive rewards are injected into old saves.

Scripted play and deterministic checks demonstrate implementation correctness, not a completed human balance study or hardware certification. Set damage, healing and cooldown values are authored starting values for further playtesting.
