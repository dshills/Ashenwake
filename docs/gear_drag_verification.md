# Equipment drag-and-drop verification

Validated on 2026-09-19 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `b6405e6`.

## Scope and method

The solo Gear screen now has twelve cached equipment controls arranged around the body and a separate scrollable inventory grid. Native Godot drag/drop calls the existing authoritative equip/unequip transactions. Inventory shows only unequipped owned items; replacement never removes ownership. Click inspection, selectors and explicit buttons remain available. The shipping Endgame, retained Campaign and Production directors route **I** directly to Gear.

Payloads bind the item, source slot, UI owner and current view generation. Ownership, slot/discipline/two-hand compatibility and Torren eligibility are checked again at release. A successful request invalidates its payload. Controls remain alive until native drag dispatch finishes. Dragging temporarily owns an independent world-pause reason; cancellation releases that reason without resuming an existing pause.

`AppearanceDragChecks` drives actual viewport press/motion/release events. It covers weapon, helmet and chest removal/re-equipping; weapon replacement; invalid and same-slot drops; outside, Escape, I, focus-out and panel-hide cancellation; stale payload rejection after another valid transaction; away-from-Torren rejection; conserved item IDs; cached controls; and save/replay. It also checks that closing the panel before a deferred menu press arrives prevents reopening. World and preview appearance are compared after changes. A maintained, validated completed-campaign fixture is loaded with its original content and legally retrained to Arcanist for two-handed/off-hand rejection and restoration. This branch has its own save/profile directory, preserves the fixture source and restores the original Vanguard session afterward. No items are minted for the diagnostic.

`MouseActionsSmoke` uses actual I/C input and a Character-tab click on the shipping EndgameDirector to verify direct Gear opening, closing and switching without equipment, ownership or history changes. Visual inspection covers the wide layout, 1280×720, 780×800 and a scrollable inventory with more than sixty items.

## Source results

- Build: zero warnings/errors. Formatting and whitespace checks pass.
- Existing Core suite: **350 passed**, zero failures; `artifacts/gear-drag/core-tests.log`. No Core implementation or tests changed.
- Final rendered appearance/drag diagnostic: **328 checks**, **25 pointer gestures**, **12 captures**, **1,394 preparation commands**, and matching Vanguard/Arcanist save and replay branches; `artifacts/gear-drag/source-rendered.GACsrW/`.

Native testing found that Godot consumes a menu key when it cancels an active drag. The UI now consumes the original event explicitly and delivers one menu press after drag dispatch finishes. A generation and visibility check discards that press if the panel or state changes first. Actual input asserts one I-menu delivery normally and zero after synchronously closing the panel. The earlier failing run is retained at `artifacts/gear-drag/source-rendered.P4htfH/`.

## Prism review

Prism/Gemini reviews `100d8e36b8b8c22f70568046b20085f2` and `17d57b3ec7ecec8857526066e83f00e9` used default secret redaction under the user's standing approval. Two small efficiency findings were fixed by caching menu action names and reordering inventory cards with the loop index. The deferred menu-input concern was addressed with the generation/visibility guard and native regression described above.

The follow-up's two high findings hypothesized an item definition or equipped item missing from an adopted snapshot. Source inspection confirms these states are rejected before the UI: `ProgressionSession.ValidateItem` rejects unknown definitions, `Validate` rejects duplicate IDs and invalid equipment references, and every transaction validates its independent candidate before adoption. Runtime archives reject changed content identity before restoration; UI state and definitions come synchronously from the same adopted session. Silently skipping invalid items in the UI would weaken these established invariants. No change was made for these findings. Its color-cleanup finding requests the instance-validity/deletion guard already present in `GearDragCard.RestoreDragColor`.

Other suggestions were assessed against their actual execution paths. The view signature runs behind `ProductionHud.Rebuild`'s revision/range gate, not every frame; it also protects session/view changes. Conservative invalidation during a drag is intentional, and its world pause prevents ordinary simulation progress. Two-hand checks supply UI feedback, while Core independently authorizes every requested transaction. The test reflection fails explicitly on a renamed field rather than silently passing. Layout constants remain covered at the three tested sizes. Localization of item names and the other existing literal Gear labels is deferred together; the current text catalog has no item-name keys, and existing item selectors use the same ID-based presentation. No actionable correctness finding remains.

## Exported app

The final archive passes **328 rendered equipment/appearance checks** with **25 pointer gestures** and **12 captures**, plus **109 rendered shipping mouse-action checks** with **13 captures**. All five new I-routing assertions pass. The latter includes **384 input commands**, **196 campaign preparation commands** and **14 verified replay branches**. Source and packaged appearance assertion maps, command/gesture counts and both saved drag replay files match exactly.

- Archive: `artifacts/gear-drag/Ashenwake.zip`.
- SHA-256: `a70763ec9b1f89b79db4dade80c6f0e079236fef39207decafc065f89aa39682`.
- Exact-package equipment evidence: `artifacts/gear-drag/package-rendered.JDquaL/`.
- Exact-package shipping mouse evidence: `artifacts/gear-drag/package-mouse.vkUXXf/`.

The broader export suite passed **1,775 headless checks** across fourteen diagnostics: release 33, mouse movement 83, mouse actions 96, Anatomy 74, interactions 17, opening journey 52, Verdant 136, Cinder 182, Spine 211, Hollow 224, services 19, character visuals 170, combat feedback 168 and appearance 310. Evidence is in `artifacts/package/22f02d6da01b.NpJ0s4/`. That suite started before the final deferred-menu generation guard; the exact final archive's affected drag and shipping menu paths were retested above (**437 rendered checks**). The guard has no Core, content or save-format changes.

The campaign/endgame route completed **32,190 commands** with matching save/replay and unchanged hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. Borrowed Memory completed **2,516 commands** with matching save/replay and unchanged hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Export and all Godot log checks pass. Consolidated results are in `artifacts/gear-drag/verification.json`. The default `artifacts/export` archive and extracted app are refreshed from the final archive after verification.

## Limits

This validates the current solo interface and its existing equipment rules. It does not add manual inventory sorting, inventory capacity, ground discarding, new item art, cooperative equipment synchronization, or equipment changes away from Torren. No Core schemas, authored item definitions or save formats change. See [equipment controls](gear_drag.md).
