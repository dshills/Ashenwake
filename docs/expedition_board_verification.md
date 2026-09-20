# Visual expedition board verification

Implementation baseline: `7437123`, using Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS.

## Scope and ownership

The endgame board presents selectable Sigil cards, deity emblems, authored route previews and progress states, an exact rule replacement preview, locked and unlocked hunts, current attempts/deaths/reward percentage, and permanent reward receipts. The HUD reads the existing Core views and immutable manifest, then dispatches the existing director transaction events. Gate range, compatibility, Sigil/material consumption, retries, combat completion and permanent rewards remain in Core. No Core, content, balance, save-schema or reward-formula changes were made.

Selection and preview do not submit transactions. Sigil consumption and hunt entry require an explicit commitment; abandonment and actual travel with ground drops require review. Finishing the final room keeps its loot available, matching Core's behavior. Receipt amounts come from the actual recorded reward and are associated with the current run before presentation as that run's result.

The full board uses an independent `expedition-panel` pause owner and backdrop. It releases only its own pause, blocks world input and hands menu shortcuts to the appropriate screen. Closing, hiding, tab/selection changes, state revision, session replacement or application focus loss cancel pending confirmations. Accepted requests clear their pending state before dispatch, preventing repeated native confirmation signals from submitting twice. Restoring a session reacquires the visible board's pause without resetting movement when the board is closed.

## Diagnostic scope

`ExpeditionSmoke` begins with a fresh locked campaign, then imports the maintained Phase 4 completed-campaign fixture into an isolated character. It earns its Sigils and resources through public commands: gate proximity and free recovery, a real combat death, retry and abandonment, a completed four-room Fracture, a paid compatible attunement, Fracture progression to tier three, and the three-phase False Vael hunt. No items, completions, rewards or death states are fabricated.

Native viewport input selects cards, navigates tabs and activates transaction controls. Popup acceptance/cancellation uses public native dialog signals; popup-button input is not claimed as end-to-end coverage. The route checks exact authored room names and completed/current/upcoming/failed labels, actual attempt and death counts, reward receipts, hidden secret-hunt details and disabled known-hunt entry. It captures layouts at 1280×800, 1280×720 and 780×800. Save/load checks exact state, and replay segments cover every executed command before the 1,800-command window rolls over.

The separate `InteractionSmoke.gd` launches the actual shipping Endgame scene and exercises discipline selection, B open/close, frozen simulation and blocked gameplay input, independent manual pause, C/I/J/H handoffs, and the existing Mara-to-Act-I flow. The other package suites retain campaign, endgame, mouse, crafting, skills, Anatomy, equipment, combat feedback and Borrowed Memory coverage. No independent human playtest or broad campaign balance assessment is claimed.

## Source results and review

- Full Core regression: **434/434 passed**, with no failures or skipped tests, at `artifacts/expedition-board/core-tests.log`.
- Initial client build: zero warnings/errors.
- Initial rendered route: **201 checks**, **6,599 commands**, **31 captures**, at `artifacts/expedition-board/source-rendered.tNg4mZ`. This precedes final completion-warning and shortcut/input fixes; it is not the final artifact acceptance record.
- Read-only agent review found a misleading loot-loss warning on final completion. Core retains the final arena; the warning now applies only to intermediate advancement and return to Greyhaven. Follow-up diagnostic assertions require final completion to retain the exact drop count without a departure dialog.
- Local integration review corrected J handoff outside expeditions and prevented closed-board session changes from clearing ordinary world movement. Session replacement reacquires pause only when the board is open.

Prism staged review `68ba99352800c3370e68386ecb10857f` is saved in `artifacts/expedition-board/prism-review.json`. It reported **two low**, **zero medium**, and **zero high** findings, both conditional concerns about collection ordering. The last reward is taken from Core's `SortedDictionary<long, EndgameReward>` keyed by increasing run ID. Available Sigils are a preserved-order `FractureSigil[]`, projected by filtering the stored array. Neither is an unordered collection, so the hypothesized unstable ordering does not occur. No Core ordering or save representation change is needed.

The final source route passed **195/195 rendered checks**, **6,599 commands**, **17 UI transaction requests**, and **31 captures** at `artifacts/expedition-board/source-final.gakyur`. Its five replay segments cover 63 + 1,800 + 1,800 + 1,800 + 1,136 commands, ending at `6A7E00D027DF4C6B662EE2B96CC300CAD3D53C214299A4776C76C18A12F0690D`. The smaller check total reflects removing obsolete final-Finish confirmation assertions and adding direct loot-retention assertions. Shipping-scene interaction checks passed **29/29** at `artifacts/expedition-board/interaction-source.qlxLnv`.

Final staged Prism review `38b2d569b6cc417217624c2891b5e2d5`, saved in `artifacts/expedition-board/prism-final.json`, reported one medium and one low finding. Both were addressed: input handlers now reject mouse events before keyboard processing and use retained action arrays with direct loops, and the unreachable final-room departure message was removed. The first package attempt was deliberately stopped and retained as `export-superseded.log` so final package acceptance could use the corrected code. No test pass is claimed for that interrupted attempt. The exact archive validation below includes these final input/text cleanups.

## Final package results

The final macOS archive passed all **19 suites**, including **2,199 headless UI assertions** across 17 UI suites plus campaign/endgame and Borrowed Memory replays. Reports are in `artifacts/package/22f02d6da01b.4i0jy9` and `artifacts/expedition-board/export.log`; consolidated evidence is `artifacts/expedition-board/verification.json`.

- Campaign/endgame: **32,190 commands**, final state `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`.
- Borrowed Memory: **2,516 commands**, final state `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`.
- Both hashes match the previous release, and their archived replays verified independently.
- Packaged expedition diagnostic: **164/164 headless checks**. Shipping-scene startup, interaction and menu handoffs: **29/29**.
- Exact-archive rendered expedition diagnostic: **195/195 checks**, **6,599 commands**, **17 UI transactions**, **31 captures**, at `artifacts/expedition-board/package-rendered.4dhimz`. Assertions and all five replay segments match the successful final source run exactly.
- Inspected the exported app's 1280×720 Sigil view, 780×800 attunement view, known hunt cards, completed hunt receipt and loot-departure dialog. Cards, route labels, scrollable details and action controls fit the captured viewport sizes.

The playable archive is `artifacts/expedition-board/Ashenwake.zip`, SHA-256 `f312d9f93eb74fd37c9a799867d29dd4fd7f5848425150da4a55b671a34fa9b5`. Rendered verification ran from a fresh extraction of that exact ZIP. The standalone diagnostic projects Core data into the board; the separate shipping-scene checks validate actual director integration. Existing Godot ObjectDB shutdown warnings are not represented as warning-free native execution. The unchanged strict log checker and each required pass marker succeeded. No production changes were made after this archive was built.
