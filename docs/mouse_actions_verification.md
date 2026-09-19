# Mouse actions and combat readability verification

Validated on 2026-09-19 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `185a49a7bede0646d5ebab9e28c6a131c803dc2e`.

## Scope

The shipping solo campaign accepts clicks on visible NPCs, clues, available hunt mechanisms and individual loot drops. The character approaches within the current authoritative interaction radius, stops and performs the selected action once. Hover names and highlights the target. Existing attacks, movement overrides, menus, focus loss, loading, death and scene replacement cancel the pending action. Hidden loot cannot consume a floor click; holding Alt reveals it for picking.

Living enemies have compact cached health bars. Selected or hovered enemies expose exact health and conditions in one focus card; ordinary unfocused overhead names are hidden. Required boss, anchor and channel instructions remain visible. Contextual objective buttons offer the next action, and a cached WAY FORWARD marker appears after a main campaign encounter. Both reuse existing reward review and story confirmation, preserving uncollected drops until the player explicitly chooses to leave.

These changes translate local input into existing commands and UI actions. Combat, inventory, mechanism availability and progression validation remain in Core. No input intent, path or hover state is added to saves, replays or network messages. Cooperative mouse movement retains its existing controls.

## Verification method

The new diagnostic runs the shipping EndgameDirector, feeds actual viewport mouse/key events and advances the production Sandbox at the real fixed interval. It reaches a distant Greyhaven checkpoint by mouse movement, saves through F5, approaches Mara, opens her actual conversation once and tests cancellation and restoration through the shipping handlers. Real campaign commands earn multiple drops; selected pickup must leave every other drop intact. The actual filter widget and Alt input exercise hidden loot. WAY FORWARD must open reward review once without travelling, collecting or losing drops. Every recorded campaign branch is replayed.

An isolated authored Orrun hunt phase uses the earned character and real Core mechanism rules. It verifies term pickup, changed availability, cancellation, plinth deposit and a matching combat replay. It does not claim a complete mouse-driven endgame unlock journey. The separate opening journey diagnostic covers Greyhaven through the Bell Saint, including story confirmation, anchor instructions, reward collection and entry to Act II.

Seven new Core cases exercise approach routing with actual movement and pickup commands: obstacle detours, targets inside obstacles, living-body clearance, unreachable routes and inclusive already-in-range behavior. The full Core suite passes **333 tests**.

The readability branch inspects exact health-bar depletion, shared texture and stable node identities, real pointer focus, pause, actual combat damage/status/death, protected Bell Saint instructions and authored invisible enemies. Its isolated Bell fight has its own combat replay and leaves the live campaign unchanged. The corresponding boss-detail capture uses the existing diagnostic backdrop; the journey diagnostic captures the actual sanctuary. Visual inspection and the new layout check caught a one-pixel conditions label; reserving two lines restores readable protection/status text.

## Source results

- Build: zero warnings or errors. Formatting and whitespace checks pass.
- Core: **333 tests passed**.
- Rendered mouse actions and HUD: **104 checks**, **13 captures**, **384 input commands**, **196 campaign setup commands** and **14 matching campaign replay branches**. Separate readability and mechanism combat replays also match.
- Rendered opening journey: **53 checks**, including reward preservation, story confirmation, boss anchors, progression into Act II and replay.
- Mouse/HUD source evidence: `artifacts/mouse-actions/actions-rendered.5Pmadu/`.
- Opening source evidence before the conditions-height fix: `artifacts/mouse-actions/final-journey.cZdUWm/`; the final package journey below verifies the fixed display in the sanctuary.

## Prism review

Initial Gemini review `30b886f3ccf72f783daf84faeef7714e` reported two medium findings and no high findings. The picking-cost finding was addressed with a one-projection distance rejection before recursive mesh inspection; enemy picking also rejects distant bodies. The other finding misclassified immutable-view prompt formatting as Core progression logic. The HUD routes existing validated actions and reads authoritative shielding; it does not decide combat or progression outcomes.

Follow-up `e81a78d520cba8884ca3e0c51f48161f` reported one medium and one low finding, neither actionable. Its requested distance cull already exists inside `MouseHitsNode`, before mesh enumeration, and its cited `WorldInteractionHandler.cs` file does not exist. Its duplicate-tab warning assumes `_Ready` runs again on reattachment; the pinned Godot API explicitly says it does not without `RequestReady`, which this client never calls. Each new HUD has four unique tab IDs and its own dictionary. Both reviews used default secret redaction. Full reports and per-finding dispositions are in `artifacts/mouse-actions/`.

The previously pending Act V follow-up also completed; see [Hollow Night verification](hollow_night_verification.md).

## Exported app

The exact exported Mac app passed **104 rendered mouse/HUD checks** with **13 captures** and **53 rendered journey checks** with **16 captures**. All mouse-action assertions, command counts and three replay files match the final source run exactly. Visual inspection confirmed the fixed protection line in the actual Bell Saint sanctuary.

- Build: `artifacts/mouse-actions/Ashenwake.zip`.
- SHA-256: `abfa7bb7b8529acff59acf6bc6600ab1cade64d30e0c01a663ca3c481342dd3a`.
- Exact-package mouse/HUD evidence: `artifacts/mouse-actions/package-actions.W0T9zb/`.
- Exact-package journey evidence: `artifacts/mouse-actions/package-journey.YfMFrf/`.

The full export verifier passed **1,510 headless checks**: release 33, existing mouse movement 83, new mouse actions/HUD 91, interactions 17, opening journey 50, Verdant 136, Cinder 182, Spine 211, Hollow 224, services 19, character visuals 170, combat feedback 168 and appearance 126. With the **157 rendered checks**, this package passed **1,667 checks**. Headless runs omit the 13 mouse captures and three journey atmosphere assertions requiring rendered frames.

The campaign/endgame route completed **32,190 commands** with matching save/replay and state hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. Borrowed Memory completed **2,516 commands**, also verified save/replay, with hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both hashes are unchanged from the prior milestone. Export and every diagnostic Godot log check passed.

- Full headless evidence: `artifacts/package/22f02d6da01b.J2hM1O/`.
- Consolidated results: `artifacts/mouse-actions/verification.json`.

## Limits

This is single-click approach-and-act, with existing combat controls; clicking an enemy does not automatically chase it. The authored readability and hunt fixtures verify presentation and mechanisms separately from the campaign journey. Automated checks and screenshots do not replace independent player feedback or establish performance on other hardware. The cooperative prototype retains its existing movement and combat controls. Reproduction and controls are in [mouse movement](mouse_movement.md).
