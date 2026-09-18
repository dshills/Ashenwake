# Opening-slice playtest usability

This milestone removes navigation and feedback gaps encountered while preparing Greyhaven and Act I for independent playtests. It does not mark the game's art, combat feel, accessibility, hardware support, or release acceptance as complete.

## Player-facing changes

- Clearing an encounter exposes a next-step button and a count of remaining ground drops, with pickup/filter guidance. It opens Map or Story according to the current authoritative state.
- Continue onward, the pending story choice, return to Greyhaven, and travel to the next act appear before the regional list. Travel labels explicitly identify ground loot that will be left behind. A completed act no longer presents an unusable Continue action.
- Entering another fight closes the navigation panel. Reopening the journey after a fight selects the relevant next-step tab.
- Bell Saint phase two shows the remaining ritual-anchor count, labels both anchors without requiring target selection, and distinguishes the protected boss from the damage window after both anchors die.
- Successful interactions with Torren, Sister Cael, Oris, Kesh, and the restored workshops open the relevant Gear or Craft tab, including its matching crafting service. Opening a service does not spend resources or bypass rescue/unlock checks.
- P opens a visible pause card with keyboard focus on Resume. Settings, loot inspection, Echoes, and expedition confirmations release only their own pause. Manual pauses and focus/controller interruptions survive modal closure until explicit resume. P cannot resume behind an active modal.

## Automated and visual checks

The solution builds without warnings and formatting verification passes. No Core gameplay, save schema, or content catalog changes are part of this milestone.

- **Journey scenario: 17 checks.** Real Core combat commands produce the first encounter, monastery, and Bell Saint states. The actual Campaign HUD receives viewport mouse input for navigation and keyboard input for its native confirmation window. Checks cover loot guidance, visible next actions, the required choice, both anchors and their changing count, the damage window, Act II entry, and replay verification. This is a Core-backed HUD scenario; combat is driven by the deterministic test policy, not a human player.
- **Release UI: 33 checks.** Actual Godot keyboard events, focus notifications and connection signals cover manual pause, focus and disconnect recovery, settings/loot modal closure, overlapping modal owners, cleared movement, and local diagnostics. Seven pause checks failed before the fix; all checks pass after it. Physical controllers remain untested.
- **Ordinary client interaction: 15 checks.** The real Endgame scene exercises discipline choice, Mara, map entry, and manual/focus pause recovery through Echoes. Keyboard fixtures include physical and logical keycodes so Escape follows the same UI-cancel path as a real key.
- **Specialist services: 19 checks.** Successful service-event presentation fixtures exercise the real client HUD's tabs, selected services and focus. Unknown/rejected/Mara events are ignored; progression is byte-equivalent before and after opening services. These fixtures do not simulate rescuing the specialists or certify crafting outcomes.

The journey and pause screens were rendered and inspected on this Mac. All four scenarios run against the packaged application through `tools/export.sh`, as well as the release verification workflow. Every automated caller uses an isolated output directory. Evidence is retained under `artifacts/opening-playtest/`, with the pause reproduction under `artifacts/pause-flow/`.

The rebuilt macOS application passed all **84 checks** across these four scenarios. Its broader 32,190-command campaign/endgame route and 2,516-command Echoes route also passed, including replay verification. Their final state hashes remain `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55` and `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. The playable package is retained at `artifacts/opening-playtest/Ashenwake.zip`.

## Prism review

Prism/Gemini run `288491afa4dc07494248c0388ca9cb22` reported three high, four medium, and two low findings. The findings were checked against the complete implementation and independently reviewed by a second agent:

- **`2865b9c87b48a560`, `1bcdb1006cb540fd`, `a1b0c460ee0c1f7b` (high): presentation is not simulation authority.** Choice readiness selects text, a tab, and enabled buttons; it cannot commit a choice or permit travel. `CampaignRuntimeSession.ChangeWorld` and `CampaignSession.Choose` revalidate every command, and `ChoiceAllows` gates boss entry. Anchor counts and mechanic labels only change Godot controls. `CombatSession.CampaignShielded`, `TryCampaignPhaseTransition`, and the Core damage path still own immunity, actor creation, phase changes, and damage. Moving English labels into the simulation is not required to retain authority. The new scenario checks the displayed state against real Core encounters and replays the resulting commands.
- **`9176cbfce3d662b6` (medium): nonblocking optimization suggestion.** The campaign has five choices; checking this small array while updating the HUD adds no unbounded work. No demonstrated performance regression was supplied. Wider content scaling can use a cached projection if measurements justify it.
- **`6cb74e682293853d` (medium): nonblocking maintenance suggestion.** The ready-choice predicate used for automatic Story opening and the next-step prompt agrees for the current contract; the choice/confirmation/boss-entry scenario checks both. Consolidating those presentation queries is future cleanup, not an execution-path defect or bypass of Core validation.
- **`52a9c17e74bb6a20` (medium): unsupported path assumption.** The smoke's `--output` is an operating-system directory, supplied as a fresh absolute path by both launchers. It is created with `Directory.CreateDirectory` and deliberately uses `System.IO`; no `res://` or `user://` output is part of this contract.
- **`265f2fd8a0322c26` (medium): nonblocking maintenance suggestion.** All paired modal source IDs match. The named-owner regression covers overlapping pauses and the actual client scenario covers Echoes closure with manual/focus pauses. No mismatched key or stuck-pause execution path was identified.
- **`522debe67fc9babf` (low): the loop is bounded.** Each iteration advances one actual simulation tick, stops on the requested encounter condition, and fails after 6000 ticks. It is a functional test limit; separate performance/soak checks own latency acceptance.
- **`a7ae2e92b03714da` (low): visual-data refactoring suggestion.** The phase-specific label height is presentation-only and was adjusted after rendered inspection to avoid overlapping the ritual-anchor label. Moving it into a future visual prefab is not needed for correctness.

No actionable blocking finding remains. The independent review found the same authority boundary and no regression in travel, services, or pause ownership.

## Next acceptance work

Run an independent opening-slice playtest across several disciplines. Record combat targeting, skill/resource comprehension, loot comparison, story comprehension, deaths, and whether players can finish without coaching. Continue production art/audio work only against an explicit quality bar; this milestone adds no finished assets or release certification.
