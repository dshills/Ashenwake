# Regional hunt verification

Baseline: `2c3767d`. This change adds the Greyhaven board and three repeatable regional contracts. Evidence is retained under `artifacts/regional-hunts/`.

## Core and regressions

The full Core suite passes **1,445 tests**, with no failures or skips, in 11 minutes 5 seconds (`core-full.trx`). The 12 focused regional-hunt cases pass again after the final capacity, catalog and returned-victory projection fixes (`regional-hunts-final.trx`). The rebuilt server suite passes all **17 tests** (`server-final.trx`).

Focused coverage includes each actual combat victory, exact item/material claims, ordered and proximate clue interactions, replay, checked saves, stage restoration, repeat contracts, death, abandonment, duplicate claims, forged victory and inconsistent reward receipts. Existing Phase 4 migration fixtures provide earned characters in unit tests; the client diagnostic separately earns its campaign unlocks through ordinary commands.

Core owns the contract rules, encounter composition, unlocks, run identities and rewards. The client projects the board and sends commands. Rewards are committed by the existing outer runtime transaction. The optional unused regional state is omitted, preserving existing content identities and unused archive serialization. Regional arenas do not generate ordinary drops or intermediate progression rewards. Departure reserves room for the eventual legendary item; permanent build changes and overlapping journeys are blocked until the contract resolves.

After returning victorious, ordinary hub skill use can change mastery. The retained victorious arena is reprojected against permanent progression on successful hub ticks, so a save does not retain an obsolete build projection. Failed contracts resolve without payment when returning to Greyhaven.

## Client checks

The diagnostic route uses a fresh Vanguard, earns Acts 1–3, exercises the board with viewport pointer events, approaches the first clue and the hub board through the actual movement driver, and fights every hunt with ordinary combat inputs. Confirmation dialog decisions use explicit public signals because the dialogs are native windows. It checks tracking, combat, victory awaiting claim and claimed saves/replays, duplicate claims, cancellation, abandonment, actual death and restored recovery. Layout checks cover 1280×800 and 780×720.

Early diagnostic iterations exposed and corrected initial wrapped-label expansion and delayed child layout after resizing/reopening the board. Standalone and integrated layout probes are diagnostic artifacts only. The initial journey tutorial must be dismissed before clicking navigation, and Resume must happen before requesting movement because resuming intentionally cancels pending movement. The diagnostic waits for layout before measuring/clicking newly opened controls.

The complete headless run passes **133 checks, 22 viewport clicks and 6,969 scripted gameplay commands** (`headless-10/regional-hunts-review.json`). It includes additional movement-driver ticks and checks all three explicit item/material claims, every persisted hunt stage, and replay at the save checkpoints. The strict runtime log error check passes.

The native capture run additionally exposed a real minimap overlap with the fifth navigation row. The client now reserves space below the hunt button for the minimap and reward feed. Subsequent native testing completed the gameplay route but caught a diagnostic focus issue: after resizing, the interruption-pause overlay intercepted the unlocked-board navigation click. The diagnostic now resumes before that click and verifies the board is visibly open before measuring it.

The final macOS export passes **151 checks, 22 viewport clicks, 6,969 scripted commands and 13 captures** (`native-3/regional-hunts-review.json`). The headless training/equipment-preset regression passes **156 checks and 11 replay verifications** across all five disciplines (`training-regression/training-review.json`). Runtime and export log checks pass. The integrated solution and final client builds have zero warnings/errors; formatting verification passes.

The verified package SHA-256 is `c6bbf218e1ab5d5cdd5cc24d9a3e7feadb1c3b8f651b6660c0a6f1bb21ca3f45`. Inspected captures show the three unlocked cards, fitting controls at both window sizes, regional evidence props, named quarry actors, counterplay and explicit victory/recovery controls. The final native run also allows the audio mixer to release streams before exiting.

## Prism

Prism/Gemini review `92b84d7f6925fa6ec9b823f5c8edf5ca` reported no high findings, one medium and one low. The medium catalog-duplication finding is addressed: `PrimaryEnemyId` is canonical contract data shared by encounter composition and client quarry naming. The low nullability claim assumes an unknown contract can enter live restored state; restoration explicitly rejects unknown contract IDs, so that proposed execution path is not reachable.

Independent integration review additionally caught training scenery visibility, the full-inventory bounty dead end and an unwanted Echoes fork before a rejected departure. Training now hides/restores hunt scenery, departure requires reward capacity, and Echoes preflight rejects unresolved regional hunts before creating a fork.

Follow-up review `c2e4922af9e7e6e1e4bc7e9b190cc167` reports two medium findings. The proposed receipt mismatch is not present: `ProgressionSession.GrantItem` and regional validation use the same ordered payload (`Action`, `definitionId`, `rarity`, sorted `Affixes`), and claimed archives/replays pass for all three contracts. The proposed victory-modal delay is a presentation suggestion; immediate contract review is intentional, there are no ground drops to inspect, and Core clears outstanding hazards/projectiles/statuses on victory.

Final UI review `ad89cc735bfbc68d3fa3d65281de9478` reports one medium and one low finding, neither an unresolved functional defect. The medium suggests removing the panel's per-frame layout reconciliation. That path performs bounded comparisons and sorts only stale layouts; it is retained to handle demonstrated delayed wrapped-label/container sizing after opening or resizing. The low repeats the already checked unknown-contract nullability assumption. All three raw review reports remain in the artifacts; they are not represented as zero-finding reports.

## Scope

The three hunts reuse established combat archetypes with distinct formations, elite traits and arena rules. They are regional contracts, separate from endgame God Hunts. The current arenas have evidence markers and objective guidance but no local-map overlay. The ledger permits at most 1,000 admitted hunts per character, including abandoned attempts. Scripted victories establish functionality, not independent balance acceptance across every discipline or hardware platform.
