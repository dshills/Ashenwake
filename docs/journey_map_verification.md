# Visual Journey verification

Implementation starts from `1877c9d`, using Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS.

## Scope and authority

Journey now contains a connected map of Greyhaven and five regions, inspectable locked destinations, current/completed/unlocked state, the current objective, regional encounter progress, next-encounter guidance and authored completion rewards. Selecting a region changes only presentation. Existing director events execute explicit travel and Core retains all progression, reward, exploration and choice validation.

The Journal separates objectives, discoveries, reached choices and residents. It hides unreached choices and later regional testimony; discovery text follows the same penultimate-encounter boundary already used by Core's campaign view. No new content schema, progression rule, reward, balance or save field is introduced.

All Journey tabs block world input and independently own pause. Anatomy keeps `divine-anatomy`; Map/Story/Journal use `journey-panel`. Loot departure and permanent story confirmations are invalidated by selection/tab changes, closing, state revision, session replacement and application focus loss. Confirmed requests revalidate against the latest view and cannot be submitted twice. Native popup focus transfer does not count as leaving the application.

The shipping discipline selector remains above a closed Journey panel. Explicit Character and Echoes presentation close Journey first. Public-command campaign/endgame and regional art diagnostics explicitly close automatically opened menus so their existing world/animation checks continue; the dedicated Journey diagnostic tests the actual modal behavior instead of bypassing it. Hollow retains the earned ending panel until its existing presentation check observes and closes it.

## Automated scope

The earned Journey route starts a fresh Vanguard campaign, fights the road and monastery encounters, commits a real regional choice, defeats the Bell Saint and travels to the newly unlocked second act. It never fabricates completion, items, discoveries or rewards. Native viewport clicks drive the map, region selection, explicit travel, journal filters and dialogue-opening controls.

Checks cover locked inspection and state/replay purity, current and completed markers, 1280×800/1280×720/780×800 bounds, independent pause owners, gameplay-key blocking, unrevealed story masking and earned journal entries. A real loot-bearing road checkpoint exercises canceled departure, stale selection, hidden panel and save/load invalidation. Story cancellation and changing tabs similarly invalidate a pending choice. Repeated confirmation cannot submit twice.

Dialog acceptance/cancellation uses public native dialog signals after real viewport button clicks. Native popup-button input is not claimed as end-to-end coverage. The actual on-disk road save is restored, then travel continues. The pre-restore replay contains 157 frames; the final segment contains 1,165 frames, both checked against their exact state hashes. These segments cover all 1,322 recorded commands in the earned diagnostic route. The final save preserves exact campaign rewards and choices.

The native startup and service diagnostics separately launch the actual Endgame scene, select a discipline with the mouse, interact with Mara, enter the first region and exercise existing specialist routing. Anatomy retains its earned Heart implant/save/load checks and now accepts the explicit loot-departure dialog. Regional art, combat feedback, mouse controls and endgame/experiment coverage remain in the package suite. No independent human playtest or broad campaign balance assessment is claimed.

## Core and source results

- Full Core regression: **434/434 passed**, `artifacts/journey-map/core-tests.log`.
- Client build: zero warnings/errors. Formatting and whitespace checks pass.
- Rendered Journey: **103/103 checks**, 25 captures, five navigation/choice requests, no skipped checks; `artifacts/journey-map/source-rendered.xgZiNi`. Inspected wide and compact maps, the earned-choice journal and loot confirmation.
- Shipping mouse actions: **96/96**, `artifacts/journey-map/mouse-actions-source.aVouXN`.
- Divine Anatomy: **75/75**, `artifacts/journey-map/anatomy-source.s25SKo`.
- Native startup/interactions: **19/19**, `artifacts/journey-map/interaction-source.HN7sr7`.
- Specialist routing: **21/21**, `artifacts/journey-map/service-interaction-source.eiu39R`.

The rendered source run precedes the final content-name display and startup/programmatic-menu handoff refinements. The exact-archive check below covers the final implementation. The first rendered attempt reached the existing defeat sequence but waited for an automatic draw while the native window was occluded and exited at its frame bound without a pass report. Captures now explicitly request a diagnostic draw; that incomplete run is not counted as a pass. A missing diagnostic namespace import was also corrected after the first compile.

## Prism review and dispositions

Initial staged review `957cbc10b3bd57e9107b1e9babd123ce` is saved in `artifacts/journey-map/prism-review.json`: one high, four medium and three low findings.

- **Penultimate encounter index (high):** `CampaignContent.Validate` requires every act to contain at least two encounters before its definition can reach a shipping director. Core's own revelation boundary uses the same `[^2]` index. The proposed one-encounter crash is outside this validated contract.
- **Timer-triggered rebuilding (medium):** the render key and second granularity predate this change. The newly modal Journey screen pauses the world; its exploration timer does not advance while open. Hidden panels return before rebuilding. The suggested every-second visible rebuild does not occur in ordinary play.
- **Exploration completion caption and travel gating (medium):** the client retains the existing presentation checks to label or disable actions. It cannot complete exploration, grant rewards or bypass a rule: each request still reaches `CampaignRuntimeSession.ChangeWorld` and its transaction validation. Consolidating availability into a separate Core projection would be a future API refinement.
- **Region names (medium) and article stripping (low):** addressed. Actual names come from `CampaignDefinition` through `SetRegions`; fallback labels are generic placeholders, and full provided names are displayed without removing an English article.
- **Tick conversion (low):** the existing campaign clock is fixed at 30 ticks per second. This is a presentation unit conversion; no tick-rate configurability is introduced or claimed.
- **Linear path assumption (low):** Core explicitly validates exactly five acts ordered 1–5, with sequential unlocking. The map renders that actual campaign contract. Branching acts would require a future content/schema change.

Final staged review `9a499b40a9043b897ccd3017952398d3` is saved in `artifacts/journey-map/prism-final.json`. It covers the final implementation, startup/menu integration fixes and diagnostic updates. It reports no high findings, repeating only the travel-availability projection suggestion (medium) and linear-route assumption (low). The dispositions above apply: authoritative validation already resides in Core, and the validated campaign is sequential. Neither finding identifies a reachable bypass or an incorrect current route. The optional API/content-generalization suggestions are not represented as implemented changes.

After this review, package validation caught a diagnostic-only integration error: Hollow's unconditional menu cleanup hid the earned ending before its existing visibility assertion. Its cleanup now leaves that panel open until the check observes it and closes it through the native control. No production logic changed after the final review.

## Final package results

The final macOS export passed all **18 suites**, including **2,025 headless UI assertions** across 16 UI suites and the campaign/endgame and Borrowed Memory replay suites. The results are in `artifacts/package/22f02d6da01b.5UDRf0` and `artifacts/journey-map/export.log`; machine-readable evidence is `artifacts/journey-map/verification.json`.

- Campaign/endgame: **32,190 commands**, final hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`.
- Borrowed Memory: **2,516 commands**, final hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`.
- Both hashes match the previous release. Their archived command replays passed independently.
- Exported Journey in headless mode: **100/100 checks**. Hollow ending coverage: **224/224**.
- Exact-archive rendered Journey: **103/103 checks**, **25 captures**, five navigation/choice requests and no skipped checks, at `artifacts/journey-map/package-rendered.VQNtGJ`. Its assertion results and both replay segments match the successful source run exactly.
- Inspected the exported app's wide and compact maps, complete region names, earned-choice journal and loot-departure dialog. Text, controls and map cards fit at the captured sizes.

The playable archive is `artifacts/journey-map/Ashenwake.zip`, SHA-256 `e52397b59956aa898e6a77aef14c47571fc47612592ac32cfa744715375ee8a1`. It was extracted into a fresh directory for the rendered check. The first export attempt is retained as `artifacts/journey-map/export-first-attempt.log`; it stopped at the diagnostic-only Hollow ending check described above and is not counted as the final package pass. Regional headless rendering skips remain explicit in their individual reports. Existing Godot shutdown ObjectDB warnings are not represented as warning-free native execution; the unchanged strict log checker and each required pass marker succeeded for the final suite.
