# Visual crafting workbench verification

Implementation starts from `faf7828`, using Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS.

## Scope and authority

The crafting tab now has six service cards, a searchable and filterable equipment tray, a native item drop target, recipe choices, before/after item values and material/catalyst balances. Selection and dragging inspect an item; only Apply or the permanent confirmation requests a transaction. The existing Production, Campaign and Endgame directors execute that transaction and return its result before completion feedback appears.

`ProgressionSession.PreviewCraft` captures the character, restores an isolated session and invokes the existing crafting transaction there. Before and after snapshots are detached. A collision-free hypothetical receipt is confined to the projection; previews never consume live materials, mutate equipment, record live commands or weaken permanent-confirmation requirements. Content, balance and save schemas are unchanged.

The workbench owns a separate pause, consumes gameplay shortcuts, permits search typing and blocks mouse input to the world behind it. Closing releases only its own pause. Changed selection, service, state, reach, visibility or session invalidates pending permanent confirmation; a stale confirmation cannot submit its old request. Session restoration also clears prior completion feedback.

## Test boundaries

The standalone `--crafting-smoke` diagnostic earns equipment and materials through the existing dungeon and specialist routes. The target is selected by valid Core tempering and rebinding previews. Five services commit through the actual HUD: Tempering, Rebinding, Engraving, Purification and Extraction. Their exact item, equipment, property, fragment and material outcomes are compared with the projection, then saved and replayed. A separate branch legally spends remaining materials to verify a blocked recipe.

Native viewport input drives service buttons, commit buttons, one inventory-to-target drag, gameplay-shortcut blocking, search typing and backdrop wheel blocking. Public drag payload validation additionally checks foreign and stale epochs. The diagnostic tests cancellation and selection/session invalidation of extraction confirmations. The session-identity check temporarily installs a session restored from the same earned state, then returns to the original session so all five crafting requests remain in the full replay history.

Dropdown selections use the public `OptionButton.Select`/`ItemSelected` boundary. Confirm/cancel uses the native dialog's public signals after an actual commit-button click: synthetic viewport input does not route keyboard input into the separate native popup. Consequently, popup option selection and confirmation-button input are not claimed as full end-to-end checks. The rendered extraction dialog was visually inspected.

Successful grafting and endgame catalyst payment are covered by Core preview/live transaction tests. The UI diagnostic verifies the unawakened Ashcleaver prerequisite, and does not claim a successful graft or catalyst payment through the UI. No rewards or awakening progress are fabricated. This is automated verification and screenshot inspection, not an independent human playtest.

## Core and source results

- Full Core suite: **379/379 passed**, including 29 preview tests (`artifacts/crafting-workbench/core-tests-final.log`). Coverage includes all six services, permanent confirmation, detached mutable snapshots, repeatability, receipt collisions, rejection rollback, capped/ineligible items and catalyst replacement/grafting.
- Client build: zero warnings/errors. Formatting and whitespace checks pass.
- Specialist routing: **19/19 passed**, `artifacts/crafting-workbench/service-source.xXuYrN`.
- Shipping mouse actions: **96/96 passed**, `artifacts/crafting-workbench/mouse-source.iELaEC`, including 14 command replay branches.
- Crafting source headless: **61/61 passed**, five actual service requests and 1,596 commands, `artifacts/crafting-workbench/source-headless.euRtkF`.
- Crafting source rendered: **90/90 passed**, including 16 captures and additional modal/payload/confirmation checks, `artifacts/crafting-workbench/source-rendered.atmHk7`. Previews, results, 780×800 layout and extraction confirmation were inspected. Layout assertions also cover 1280×720 and 1280×800.

The rendered source run preceded only the removal of the stale specialist notice above the workbench; service cards and the recipe already identify the current specialist. The final package runs below include that presentation adjustment.

Earlier diagnostic failures are retained in the artifact directory: a compact-layout overflow was repaired with a minimum sort-caption width and a deferred panel layout; the original earned target could be tempered but had no legal rebinding replacement, so the fixture now asks Core to validate both recipes. An initial invalid-enum test was corrected to assert the existing strict JSON exception behavior; production behavior was unchanged.

## Prism review and dispositions

Prism/Gemini reviewed staged source twice: `372656f699cf1de98cb7f6d7f44cc7b2` and final implementation review `418b63b336182dbd58285259f0a1382e`. Raw reports are `artifacts/crafting-workbench/prism-review.json` and `prism-final.json`. The final report contains one high, five medium and three low suggestions; each was checked against the callers and existing contracts rather than treated as a demonstrated bug.

- **Hashing frequency (high, both reviews):** `ProductionHud.Rebuild` already gates workbench refresh by operation revision and specialist reach mask. Inventory hashing is not a per-frame operation. User-driven service changes also refresh it. Hashing full item values intentionally invalidates stale payloads when an item is modified without changing its ID.
- **Grid reordering (medium):** the suggested `GetIndex()` guard is already present. `MoveChild` runs only when the chosen sort changes that card's position. Cards are cached, and filtering/sorting does not run every frame.
- **Operation IDs and preview collision loop (medium):** client IDs are generated once and recorded in the command stream; replay uses those exact requests. All five UI crafting requests replay successfully. Preview IDs use their own prefix and an explicitly bounded receipt search. A static counter would neither guarantee isolation nor improve determinism. Collision and repeatability tests pass.
- **Synchronous result handling (medium):** all three shipping directors synchronously execute, refresh and report the result. The fallback diagnoses a missing response. Asynchronous command dispatch would require a different lifecycle contract; none exists in this implementation.
- **Descriptions, specialist mapping and confirmation metadata (medium/low):** these preserve the existing six-service UI/Core contract. The descriptions are presentation copy; Core still validates recipes and locations. Extraction and Divine Grafting remain the two permanently confirmed enum services in both preview and transaction validation. Moving these established mappings and text into new content schemas is a deferred content-authoring refinement, not a current correctness fix.
- **Stat totals and scaling (medium/low):** displayed totals are raw item base rolls plus their corresponding affixes, matching Gear comparison. They do not claim to predict derived character combat damage. Dividing basis points by 100 is an exact unit conversion, not a gameplay scaling factor; formatting uses decimal arithmetic and invariant culture.
- **Pause reflection in the diagnostic (low):** retained only to assert independent owner state alongside public paused behavior; no shipping reflection path was added.
- **Fragment drag parity (low):** fragments keep their explicit owned-fragment selector. Gear has the requested native drag target; fragment drag/drop is a possible later presentation enhancement.
- **Search menu letters (low):** intentional. The search field must accept names containing C, I, F and other bound letters. Escape closes the workbench; C/I route normally after leaving the field. Native typing and blocked gameplay are covered by the diagnostic.

No actionable correctness defect remained from these reviews. The architectural/content-authoring suggestions above are not represented as implemented changes.


## Final package

- `tools/export.sh` completed successfully. All **1,909 headless assertions** passed across 15 UI diagnostic suites, including **74/74 crafting checks**. The full campaign/endgame and Borrowed Memory routes also completed, with save/replay verification. Logs and reports are under `artifacts/package/22f02d6da01b.yX2BMD`; the combined log is `artifacts/crafting-workbench/export.log`.
- Campaign/endgame: **32,190 commands**, final hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`, unchanged from the previous milestone. Borrowed Memory: **2,516 commands**.
- Final archive: `artifacts/crafting-workbench/Ashenwake.zip`, SHA-256 `4283da4ce3284264db061db3841e7673999c6c61e78fa393ddf85505b7f6f982`. The standard `artifacts/export/Ashenwake.zip` contains the same build.
- Exact-archive rendered crafting: **90/90 checks**, 16 captures, five crafting requests, one native drag and **1,596 commands**, under `artifacts/crafting-workbench/package-rendered.J2pJZ0`. Its entire assertion report matches the source rendered report. Each run independently verifies its recorded operation IDs and replay; replay files are not claimed to be byte-identical across fresh client GUID generation.
- Inspected the exact archive's compact 780×800 workbench and extraction result. Apply and Close remain visible; the result identifies the destroyed item, learned property and materials spent. Source screenshots also cover the confirmation dialog, stat preview and engraving feedback.
- Export and native logs pass the strict Godot log checker. Build, formatting and whitespace checks pass. Consolidated results: `artifacts/crafting-workbench/verification.json`.
