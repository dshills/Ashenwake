# Skills & Mastery verification

Implementation starts from `41d8570`, using Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS.

## Scope and authority

The Skills tab displays six ability cards, mastery progress, ultimate requirements, base/mutation comparisons, passive investment previews and confirmed passive refunds. Open it with C. Build changes still go through the existing Production, Campaign or Endgame director and require Mara's interaction range. The same code-drawn ability icons appear on the combat bar, which distinguishes cooldown, insufficient resource, Heat cap and locked states.

`ProgressionSession.PreviewBuild` restores detached progression state and invokes the existing transaction. It grants no points or mastery and confines hypothetical receipts to the projection. Preview receipt collisions, rejected transactions, detached snapshots/views and exact preview/commit agreement are covered by Core tests. `CombatSession.InspectSkill` reads static skill and mutation parameters without granting access to them. Shared passive formula helpers replace three identical inline calculations; content, balance and save schemas are unchanged.

The panel owns a separate pause and mouse backdrop. Closing releases only its own pause. Changing selection, state, reach, visibility, application focus or session invalidates a pending refund. A native confirmation's transfer of focus within the application does not invalidate itself. Gameplay shortcuts cannot leak through the screen, and session restoration reacquires its pause. A successful passive investment preserves the selected passive for reviewing another point.

## Diagnostic boundaries

`--skills-smoke` starts a fresh ProductionSession and completes four actual expeditions. Recorded successful casts account for earned mastery, including 127 Cleave casts. Seven build requests allocate all three passive types, confirm a paid refund, and select/remove/reselect a mastered mutation. Each actual outcome is compared with the detached Core preview. The full pre-restore command history replays before testing session restoration; the final save and replay cover the restored session and subsequent changes.

Native viewport clicks select abilities, variants, passives and Apply. Confirm/cancel uses the public native dialog signal after the actual Apply click because viewport-injected input cannot route to a separate native popup. Popup-button input is therefore not claimed as a complete end-to-end check; the rendered refund dialog is inspected. The earned UI route exercises Vanguard. Core inspection tests and a separate gallery cover all five disciplines and thirty authored abilities.

The gallery uses isolated CombatSession views to check six hotbar states, exact cooldown ticks/progress, native click dispatch, disabled ultimates, configured Orrun charge bindings, child bounds and reuse of controls. Its Heat-cap example deliberately passes the maximum resource as an argument to the UI adapter. This is a UI boundary fixture, not an earned resource grant. The gallery asserts that the earned character, command history, request count and world appearance remain unchanged.

Skills panel layout is checked at 1280×800, 1280×720 and 780×800. Hotbar bounds are checked at its existing 148×54 footprint and 1280×800 viewport. This does not claim that the entire existing combat HUD adapts to a 780-pixel-wide window. This is automated verification and screenshot inspection, not an independent human playtest.

## Core and source results

- Full Core suite: **434/434 passed**, including 55 new preview, skill inspection and passive-effect cases (`artifacts/skills-mastery/core-tests-final.log`).
- Client build: zero warnings/errors. Formatting and whitespace checks pass.
- Final source rendered diagnostic: **78/78 checks**, 15 captures, seven build requests and **5,119 commands**, under `artifacts/skills-mastery/source-rendered.UM8HVo`. Inspected the thirty-icon gallery, live hotbar, compact panel, mutation comparison and refund dialog.
- The rendered route covers actual mastery and mutation gates, unearned points, Mara range, modal pause/input, cancellation and selection/session invalidation, exact fees/refunds, successful result feedback, save and replay.

Earlier failures are retained in the artifact directory. The native confirmation initially canceled itself on parent-window focus loss; cancellation now follows application focus loss. The final visual gallery exposed default-theme minimum sizes retained by compact labels and progress bars; their sizes are now applied after theme setup. An initial defense test scheduled a hit at tick 1 while checking the tick 0 step; the fixture was corrected without changing gameplay.

## Prism review

Initial staged review: `1fc624c66fa5519a4c63e94e8227bb20`, saved in `artifacts/skills-mastery/prism-review.json`. It reported two medium and two low findings:

- **Restoration allegedly runs every frame (medium):** `ProductionHud.Rebuild` already checks operation revision and specialist reach before calling `SkillsPanel.SetView`. Other renders follow explicit selection, submission or result events. The visible screen pauses combat. There is no panel `_Process` callback or per-frame restore path; adding a second cache is unnecessary.
- **Unhandled corrupted state or invalid preview request (medium):** the panel receives a capture of an authoritative validated session, not raw save input. Core construction/restoration validates saves before the director publishes a view. UI selections are checked against available skill/mutation/passive identities, and rejected transactions return a failure preview. No reachable corrupted-state crash was identified, so exceptions are not broadly swallowed in rendering.
- **Wolf glyph origin (low):** the hand-drawn wolf's asymmetric local extent is positioned around `(20, 22)` to center its visible geometry in the 40-unit icon canvas. That is an intentional drawing coordinate, not a layout inconsistency; the final gallery verifies the result.
- **Damage-family string replacement (low):** addressed. An explicit display-name mapping now covers every current damage family.

Final staged review: `c0dd5671a373cc14896e540c0a735dbe`, saved in `artifacts/skills-mastery/prism-final.json`. It includes all final production code and diagnostics and reports three medium and four low findings, with no high findings:

- **Full snapshot hashing (medium):** the same parent revision/reach gate described above already prevents per-frame hashing. Including the full snapshot deliberately invalidates a pending confirmation when any authoritative progression state changes. A revision is not a substitute for validating restored state identity.
- **Behavior descriptions and damage-family localization (medium/low):** these are English presentation labels, derived from inspected Core behavior/shape identifiers. The client does not execute combat rules through them. Core-authored mutation descriptions remain authoritative. Moving all new screen text into the existing catalog is a future localization refinement; no current mechanics or translated language support is claimed.
- **Preview receipt collision (medium):** each call restores a separate projection, searches its complete receipt dictionary and chooses the first unused key. Repeated calls never append to live history. The suggestion itself acknowledges the loop finds an available key; collision, repeatability and isolation tests pass. Random GUIDs would add nondeterminism without improving this guarantee.
- **SkillButton equality (low):** `CombatSkillView` is a record, so `==` compares values. The proposed defect assumes reference equality that this type does not use.
- **Synchronous result contract (low):** all three owning directors synchronously execute Core, refresh and report success/failure. The fallback detects missing result wiring. A hypothetical future server-based implementation would require a different contract; no asynchronous transaction exists here.
- **Preview string concatenation (low):** at most a small fixed set of comparison lines is assembled on an explicit state/selection render, not in a frame loop. A builder is optional cleanup, not a demonstrated performance issue.

No actionable correctness defect remained after review. These dispositions do not claim the deferred localization or asynchronous architecture suggestions were implemented.

## Final package

- `tools/export.sh` completed successfully. All **1,972 headless assertions** passed across 16 UI suites, including **63/63 Skills checks**. The full campaign/endgame and Borrowed Memory routes completed with save/replay verification. Reports are under `artifacts/package/22f02d6da01b.noKD9K`; the combined log is `artifacts/skills-mastery/export.log`.
- Campaign/endgame: **32,190 commands**, final hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`, unchanged from the previous milestone. Borrowed Memory: **2,516 commands**, final hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`, also unchanged.
- Final archive: `artifacts/skills-mastery/Ashenwake.zip`, SHA-256 `4797ee01358a3bf4151121d9dc80174eec4d1c67d09ad530aaeab67b8060a30b`. The standard `artifacts/export/Ashenwake.zip` contains the identical archive.
- Exact-archive rendered Skills: **78/78 checks**, 15 captures, seven build requests, four earned expeditions and **5,119 commands**, under `artifacts/skills-mastery/package-rendered.XKeM7S`. Its assertion map, capture list and earned cast counts match the source rendered run. Both runs independently verify their command replays; client-generated operation IDs are not claimed to be byte-identical between runs.
- Inspected the exact archive's mastered mutation screen and all thirty icons/hotbar states. The final hotbar labels and cooldown bars fit inside each button. Source inspection additionally covers the live hotbar, compact panel and explicit refund confirmation.
- Export and native logs pass the strict Godot log checker. Formatting and whitespace checks pass. Consolidated evidence: `artifacts/skills-mastery/verification.json`.
