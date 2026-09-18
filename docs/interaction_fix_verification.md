# Greyhaven interaction and travel fix

The opening campaign accepted Mara's interaction in Core but discarded its dialogue event in the client. Pressing F therefore appeared to do nothing. The Echoes button also occupied the Journey button's position, hiding the mouse route to the campaign map. Mara's displayed interaction radius exceeded the radius accepted by the expedition rules.

## Result

- F near Mara displays her dialogue and opens the Journey map, including an explicit instruction to choose an act to leave Greyhaven.
- The conversation offers a Divine Anatomy button. A successful Mara service interaction opens the Anatomy tab.
- Journey, Fractures, and Echoes have separate buttons; the map and expedition panels sit below them.
- Mara's prompts use the existing authoritative interaction radius of 2400. Existing permanent-service commands retain their 2600 reach, preserving their save/replay behavior.
- The README describes the exact opening flow and distinguishes F interactions from map travel.

## Regression coverage

`InteractionSmoke.gd` loads the ordinary Endgame scene with a fresh character and isolated output. It sends keyboard and mouse events through Godot's viewport input path, rather than invoking gameplay actions directly. The original client failed dialogue, service, and map-button checks; the fixed client passes all nine checks:

1. Choose Vanguard with the mouse.
2. Close the Journey map with J.
3. Press F and see Mara's dialogue.
4. See campaign entry exposed by the interaction.
5. Verify that the three navigation buttons do not overlap.
6. Click the Journey button successfully.
7. Open Divine Anatomy through Mara's service button.
8. Enter Act I and save through F5.
9. Read the saved authoritative state and confirm Act I outside Greyhaven.

Both the headless scene and rendered macOS scene passed. The rendered map was visually inspected for readable dialogue, an available Act I button, and separate navigation controls. Eight Core regression cases cover Mara's interaction boundary, rejected actions preserving state, the permanent-service boundary, and replay verification.

The input regression runs in `tools/release-verify.sh` and against the exported application in `tools/export.sh`. It complements the broader command-driven gameplay routes, which did not catch the missing dialogue presentation or overlapping buttons.

Local evidence is retained under `artifacts/interaction-fix/`.

## Validation and Prism review

The solution builds without warnings, formatting verification passes, and all 312 Core tests pass (including the eight new boundary/replay cases).

The rebuilt macOS application passed all nine interaction checks, all 17 release-settings checks, the 32,190-command campaign/endgame route, and the 2,516-command Echoes route. Their final hashes remain `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55` and `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`, respectively. The playable fix is retained at `artifacts/interaction-fix/Ashenwake.zip`; the earlier Phase 8 evidence package remains unchanged.

Prism/Gemini reviewed the staged implementation in run `8ee28a30fbeb2accf74ac66fb174b8ec`. Its six findings were assessed against the execution paths and tests:

- `b798cc96381ba3e6` (high): false positive. Mara interaction commands delegate through `ExecuteExpedition` to `ExpeditionSession.ChangeWorld` / `InRange` at 2400; they do not call `ProductionSession.Near`. `Near` retains the established permanent-operation radius, covered separately by the passing 2600 boundary tests. The review's claim that the new interaction boundary tests fail is contradicted by their passing execution.
- `74ced5f4bc41f753` (medium): no reachable missing-player case. The existing `Single(Id == 1)` lookup is unchanged. Combat creation installs that player and snapshot validation requires exactly one player with ID 1; death retains the player actor. This patch does not change actor lifecycle.
- `b0dc04da2026cf0a` (low): no competing interaction events are produced by these commands. A successful Mara command emits either dialogue or service-open; additional progression events do not match `PresentInteraction`. The conversation remains visible in the map.
- `042181b026d1db8d` (low): layout refactoring suggestion, not a new functional defect. The existing fixed 1280×800 logical viewport uses canvas-item scaling. The corrected positions were checked in the rendered client and through real pointer events.
- `4c9268f70f8432c0` (low): typed/localized event identifiers are a future maintainability improvement. This fix consumes existing Core event IDs and checks the emitted dialogue in the client regression.
- `7b6384bb776f5e8f` (low): full-tree lookup is intentionally limited to the short smoke scenario. UI rows are destroyed and recreated during the scenario, so caching node references would risk stale controls. No gameplay loop uses this traversal.

No actionable blocking finding remains. An independent agent also reviewed the client event flow, layout, travel action, and input regression without a blocking finding.
