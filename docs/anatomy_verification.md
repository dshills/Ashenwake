# Divine Anatomy verification

Validated on 2026-09-19 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `2e3b6ec3abccf848a8b0bcfc96e4ca5cc68a1332`.

## Scope and method

The shipping solo campaign replaces its Anatomy dropdowns with six cached body-slot controls, owned/installed/undiscovered fragment cards, a rotating character preview, and a before/after forecast. Applying uses the existing Mara service transaction. The opening Bell Saint reward invites inspection and a return to Mara without collecting or discarding ground equipment implicitly. The heart remains optional and does not gate travel.

Core forecasts execute the same candidate mutation, validation and discovery transaction as a real implant or Manifestation choice. Seventeen new Core cases cover real earned rewards, compatible replacement, invalid ownership and slots, removal, the 39/40 and 59/60 boundaries, historical combinations, remembered suppressed forms, away-from-hub inspection, and unchanged state/hash/save/reward behavior. The full Core suite passes **350 tests**.

The Anatomy diagnostic runs the shipping EndgameDirector. Normal deterministic campaign commands prepare a valid loadout and earn the Bell Saint's actual Heart of Serath, retaining five ground drops. Viewport input then opens reward review, selects all six body slots, inspects/removes/resets fragments, reviews the remaining ground equipment, returns to Greyhaven, approaches Mara and applies the heart once. A real 46-to-64 Resonance change exercises the second Manifestation threshold; removal suppresses the selected form and reinstallation restores it. The preparation deliberately installs two additional owned fragments and the first form; it is not the default new-player loadout.

The diagnostic compares forecast and committed state, world and preview appearances, command counts and replay branches. F, Period and backdrop wheel input must not advance or affect the paused world. Actual F5/F9 handlers preserve surgery and discard an uncommitted preview; a second byte-identical save/load must reacquire the Anatomy pause. Six slot controls, forecast and action buttons must fit 1280×720 and 780×800 windows, and hidden previews must stop rendering and processing.

The appearance diagnostic adds cosmetic checks across all five disciplines plus an evolved-weapon preview regression. The latter uses a real equipped Ashcleaver and varies only presentation metadata for Awakened, Serath and Orrun; it does not claim the fresh fixture earned those evolutions. Eight shipping fragments populate all six body slots. Four original fragments receive unique physical marks in this milestone.

## Source results

- Build: zero warnings/errors. Formatting and whitespace checks pass.
- Core: **350 tests passed**, including **17** new preview cases; `artifacts/anatomy/core-tests.log`.
- Final rendered Anatomy: **85 checks**, **11 captures**, **1,394 preparation commands**, **18 input commands**, and **2 matching runtime replay branches**; `artifacts/anatomy/rendered-final.GxOYUh/`. Its command counts and save hash match the earlier `artifacts/anatomy/rendered.Oqja8q/` run.
- Headless appearance: **176 checks**, **1,309 commands**; `artifacts/anatomy-appearance.OKCzVr/`.
- Opening journey: **55 rendered checks**; `artifacts/anatomy/journey-rendered.ZrhWHd/`. This source run preceded the final modal and immediate-world-refresh fixes; the package journey below validates the completed implementation.
- Anatomy save hash: `B77A18853D505184E652A80463EFFE6153628FFF0A184CBA97B22A6207CFA06A`.

Visual inspection covered the body map, implanted heart and smaller-window layouts. Review and diagnostic work caught and fixed an evolved-weapon preview losing its evolution, world keyboard actions leaking through the modal, and an implant remaining visually stale while the world was paused. Load now explicitly discards the temporary selection and restores the modal's pause owner even when the saved state is byte-identical.

## Prism review

Prism/Gemini review `3e718407b7ef8de32237b0b5b01b0b38` reported two medium findings and no high findings. Both cleanup requests were addressed: the UI no longer substitutes a hardcoded phrase in the authored fragment description, and child minimum sizes now update only when the viewport size or Anatomy visibility changes. Panel bounds also adjust when content changes enlarge the container. A package test caught an initial resize-cache regression that could leave actions below the viewport after switching views; restoring changed panel bounds fixed it, and the full rendered source diagnostic passed again. The generic conversion from simulation ticks to displayed seconds remains. The first finding's reported subdirectory and line were inaccurate; its concrete string-replacement concern existed in `AnatomyWorkbench.cs` and was fixed.

Follow-up `825df090277bdd654458509118c479cf` reported a medium finding about repeatedly hashing anatomy in the frame loop. That call was removed: authoritative campaign updates, explicit tab opening and restoration already refresh the workbench, while local card selections refresh their own forecast. The frame callback now handles only cached layout and modal visibility. Its low suggestion to centralize slot names was deferred: the referenced `Core/Model/Anatomy.cs` does not exist, the Core already defines an `AnatomySlot` enum, and this fixed six-slot presentation does not expose a current correctness issue. No actionable correctness finding remains.

The initial automatic approval block was resolved when the user explicitly reaffirmed Prism approval after being informed about the staged-source upload to Gemini. Both reviews used default secret redaction.

## Exported app

The final Mac archive passed **85 rendered Anatomy checks** with **11 captures** and **55 rendered opening-journey checks** with **17 captures**. Visual inspection confirmed the body map, implanted heart, forecast and footer fit the tested windows.

- Build: `artifacts/anatomy/Ashenwake.zip`.
- SHA-256: `63057d65c3a62fb18623a634b274764e93df4ec4d5775cbecc0907dd71244fcc`.
- Exact-package Anatomy evidence: `artifacts/anatomy/package-rendered-verified.K0Y6jD/`.
- Exact-package opening journey: `artifacts/anatomy/package-journey-verified.ZwhtOB/`.

The full export verifier passed **1,636 headless checks**: release 33, mouse movement 83, mouse actions/HUD 91, Anatomy 74, interactions 17, opening journey 52, Verdant 136, Cinder 182, Spine 211, Hollow 224, services 19, character visuals 170, combat feedback 168 and appearance 176. With the **140 rendered checks**, the final archive passed **1,776 package checks**. Headless Anatomy omits 11 image captures; headless Journey omits three atmosphere assertions requiring rendered frames.

The campaign/endgame route completed **32,190 commands** with matching save/replay and state hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. Borrowed Memory completed **2,516 commands**, verified save/replay, and hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both hashes match the previous milestone. Final source and package Anatomy assertions, commands, saved hash and both replay branches match exactly. Export and all diagnostic Godot log checks pass.

- Full headless evidence: `artifacts/package/22f02d6da01b.5JLO9I/`.
- Consolidated results: `artifacts/anatomy/verification.json`.

An earlier package run was interrupted by macOS focus loss; the production resume modal correctly blocked its attempted clicks. Another package test exposed the resize-cache issue described above. Both failures were retained as evidence; the final archive's rendered checks pass.

## Limits

This verifies one actual campaign upgrade journey, replay/persistence behavior and authored cosmetic fixtures. It does not establish campaign balance, independent player acceptance, target-hardware performance, final production art or a cooperative Anatomy screen. The retained earlier Adventure/Production screens keep their existing anatomy controls. Controls and reproduction are in [Divine Anatomy](anatomy.md).
