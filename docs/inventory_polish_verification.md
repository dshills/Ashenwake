# Inventory presentation and organization verification

Implementation starts from `1e0c216` with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS.

## Scope

The solo Gear screen gains procedural item icons and muted empty-slot silhouettes, rarity borders, a pointer/focus comparison panel, search/type/rarity filters, deterministic sorting and contextual rejected-drop feedback. The icons draw directly in Godot; they use no external artwork, textures, 3D preview viewports or animation loops. Hover comparison is cosmetic and uses the owned item instances' base values, rolled affixes, properties, engravings and Godwrought state. Numeric deltas explicitly apply to one slot.

Filtering changes card visibility and sorting changes child order. Cached cards keep the same owned item IDs and remain available for native drag/drop. Filter changes cancel any in-flight drag. A native drag away from Torren can display feedback but cannot emit an equipment transaction. Core remains authoritative for all equipment changes; schemas, authored item definitions and saves are unchanged.

Search owns a separate pause while focused, and restores that owner after save/session restoration without changing other pause reasons. Noninteractive overlay panels hide on drag, filtering, ownership refresh, focus loss and closing the screen. Their bounds are repaired after Godot finishes text wrapping, keeping comparisons within the tested viewports.

## Verification method

The existing appearance diagnostic still covers equipment transactions, cancellation, stale payloads, two-handed rules, model changes, saves and replay. New checks use the actual cached controls to verify named weapon icons, an unequipped helmet silhouette, hover/focus comparisons without mutation, filter combinations and empty/reset behavior, sort order with duplicate earned items, search pause restoration and contextual rejection messages. Hover, text entry, clicks and drag gestures use native viewport events. Dropdown selection uses the public `OptionButton.Select`/`ItemSelected` boundary because the diagnostic cannot route its synthetic input into Godot's separate popup window; opening and selecting a dropdown option is not covered end to end.

Inventory organization uses the maintained campaign-complete fixture and its matching historical content, with a legal retrain at Mara. No synthetic rewards or items are added. Ownership, replay history, world appearance and selected preview are checked before and after presentation-only actions. Tests now require rejected drops to begin a native drag; a failure to start cannot satisfy the rejection cases.

## Prism review

Prism/Gemini review `349fa738a90fec374e8e5ece2c962f05` reported no high findings, one medium design suggestion and one low efficiency finding. The efficiency issue was fixed: refresh no longer reorders cards by ID before applying the chosen sort, and projection calls `MoveChild` only when the index changes.

The design suggestion concerns predictive equipment restrictions in the UI. These extend the existing slot/discipline/two-hand feedback with a concrete rejection reason; every successful drop still requests the existing Core transaction, which independently validates ownership, service location, discipline, slot and hand rules. There is no client-authorized state mutation or save bypass. The diagnostic exercises wrong-discipline and two-hand rejection with a legitimately earned Greatstaff. Consolidating the existing presentation predicates into a shared Core query is deferred; no current mismatch was identified.

Final staged review `0544fa548367c0c610af1705cdfc2018`, including the diagnostic cleanup below, reported zero high, three medium and four low findings. Its repeated Core-query suggestion has the disposition above. Its proposed automatic two-hand swap conflicts with the current `ProgressionSession.Equip` rule, which explicitly rejects an occupied off hand; this milestone preserves that rule. Its data-driven icon suggestion is future content-authoring work: named current weapons have explicit silhouettes, other equipment falls back by slot/discipline, and no missing current weapon mapping was identified. Icons remain presentation metadata rather than a new persisted Core schema.

The low suggestions concern pooling comparison labels, reducing projection allocations, unifying slot wording and naming layout constants. Comparison content is cached and rebuilt only when the hovered item/content changes; filtering runs on user edits or state refresh, not on every frame. These are deferred refinements, with no observed failure in the earned 61-item backpack or tested viewports. They are not represented as implemented fixes.

## Results

- Build completed with zero warnings/errors; `dotnet format --verify-no-changes --no-restore` and `git diff --check HEAD` passed.
- Source headless appearance: **370/370 checks**, 26 native drag gestures and 1,436 replayed commands (`artifacts/inventory-polish/diagnostic-headless.YPz9xV`). The final source rendered run also covers the subsequent feedback wording and sorting optimization.
- Final source rendered appearance: **390/390 checks**, 20 captures, 26 native drag gestures and 1,436 replayed commands (`artifacts/inventory-polish/source-rendered.KEgLhj`).
- Initial exported macOS rendered appearance: **390/390 checks**, 20 captures, 26 native drag gestures and 1,436 replayed commands (`artifacts/inventory-polish/package-rendered.C1lNe3`). Its complete assertion report and both equipment replay files match the final source run byte for byte. Both appearance logs pass the Godot log checker.
- Inspected the exported app's item grid, hover comparison and compact 780×800 comparison captures. The comparison remains within the viewport; all inventory filters and the Close button remain reachable. Automated bounds checks also cover 1280×720 and 1280×800.

## Diagnostic shutdown repair

The broader package run in `artifacts/package/22f02d6da01b.oCK950` passed **1,667 headless assertions** across release, mouse movement/actions, Anatomy, interactions, opening journey, the four later regions, services, character visuals and appearance. Combat feedback emitted all 168 passing checks, then crashed at native shutdown; two isolated headless runs also reported unsafe mesh/material references. Its rendered run passed 171 checks and exited cleanly. The raw failures remain in the initial export log and retry directories; the Godot log checker was not relaxed.

This standalone diagnostic does not instantiate inventory controls. It now queues its gallery children for disposal, clears its cached node references, drains queued deletion, collects managed garbage while the engine is still alive and allows three further frames for deferred native disposal before quitting. It never blocks the engine waiting for finalizers. The source check and **three consecutive final-package headless runs** pass all 168 checks with clean exits and logs (`cleanup-source.EQ8AvS`, `cleanup-package.F9qjDX`, `cleanup-package.cmexQ4`, `cleanup-package.ABalsV` under `artifacts/inventory-polish`). This completes coverage of **1,835 distinct headless assertions**; the three repeated cleanup runs are counted once.

The campaign/endgame route completed **32,190 commands** with matching save/replay and hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. Borrowed Memory completed **2,516 commands** with matching save/replay and hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both are unchanged from the preceding milestone. These broader routes ran before the final diagnostic-only cleanup; no production implementation, Core, content or save format changed afterward.

## Final archive

- Archive: `artifacts/inventory-polish/Ashenwake.zip`.
- SHA-256: `e24642619911f54b56d5b98ccfbbe24513d3f8915522d938d086624ee9f79449`.
- Exact final archive: **390/390 rendered appearance checks**, 20 captures, 26 native drag gestures and 1,436 commands in `artifacts/inventory-polish/final-rendered.N9qCW0`.
- The complete appearance report and both equipment replay files match `source-rendered.KEgLhj` byte for byte. The final export, cleanup and appearance logs pass the strict Godot log checker; build, formatting and whitespace checks pass.
- Consolidated results: `artifacts/inventory-polish/verification.json`. The default `artifacts/export` archive and extracted app are refreshed from this final archive.

The initial archive is retained as `Ashenwake-before-cleanup.zip` with SHA-256 `437e04b85657787ff299ba85b5fdb0dfe04471145a2d4d1cc406f4fbb9c27166`. Its production inventory implementation is identical to the final archive. No independent human playtest is claimed, and popup option selection retains the diagnostic limitation described above.
