# Secret chamber verification

Baseline: `27f39c6` (regional hunts). This milestone adds three optional hidden chambers, environmental clue choices, guardian encounters and three unique legendary rewards. Validation evidence is retained under `artifacts/secret-chambers/`.

## Gameplay and persistence contract

The Unrung Belfry branches from the secured Monastery, the Hollow Nest from the secured Living Ruins, and the Cold Furnace from the secured Extraction Floor. Each puzzle requires three correct, ordered responses at its physical clue positions. Incorrect answers leave progress unchanged. The client reveals observations as the player approaches or solves them; unrevealed chamber and reward information remains hidden from the discovery and collection screens.

A revealed doorway leads to a safe foyer. The player explicitly challenges the guardian and needs reward capacity before that challenge is accepted. The room grants no ordinary enemy drops or intermediate XP, mastery or materials. Victory records a permanent receipt; collecting the treasure is a separate, once-per-character operation through permanent progression authority. The sole chamber reward is its named legendary item.

The exit returns to the preserved campaign room. Leaving an unfinished fight resets that attempt, and death requires leaving before trying again. Solved clues persist. A defeated guardian remains defeated even if the player leaves before claiming its treasure; returning exposes the outstanding claim. Reentering a claimed chamber does not grant another item. Campaign, equipment and expedition changes are blocked while a chamber is active.

The optional chamber state stores puzzle progress, defeated guardians, claimed rewards and any active arena. Save restoration checks the source encounter, arena, stage, seed and permanent victory/reward receipts. Old characters are authenticated against their original catalog before their content identities are rebound. The migration also updates independently stored regional-hunt and secret-chamber combat snapshots. Reading old archives does not overwrite them or grant retrospective equipment.

## Equipment and compatibility checks

The initial focused run passes **65 tests** with no failures or skips in 12 seconds (`equipment-tests.log`). Its filter includes `SecretLegendaryTests`, `SecretLegendaryProgressionTests`, `SecretLegendaryMigrationTests` and the preceding late-legendary migration suite. The subsequent combined run passes **39 tests**: 28 secret-power cases and 11 chamber cases (`core-focused.log`, `secret-focused.trx`). These focused runs overlap and are not a full-project test result.

Coverage includes actual interrupt healing with health caps; resisted, idle and otherwise ineligible interrupt rejection; bounded poison-kill roots with ownership, range and immunity rules; Fire hits avoided specifically during dodge invulnerability; exclusion of Burning, ordinary hits and other invulnerability; direct skill empowerment; invalid-state rejection; save/replay; extraction and eligible engraving slots; and innate/engraved duplicate suppression. The subsequent run also verifies power expiry, DoT exclusion, death and room-change cleanup.

The migration cases use exact combat and progression bytes from `27f39c6`, retained as `fixtures/combat-regional-hunts.json` and `fixtures/progression-regional-hunts.json`. They cover active and cached campaign rooms, pending actions, existing equipment effects, Fractures, God Hunts, Borrowed Memory, active regional tracking/combat arenas, original checksum verification, foreign definitions, read-only inspection and exact original backups. Earlier published fixtures remain unchanged.

The new equipment powers are:

| Item | Power contract |
|---|---|
| Grief’s Reprieve | Accepted hard crowd control that actually interrupts a cast or warning heals up to 20 health; 3-second cooldown. |
| Widowthorn | A player-owned Poisoned damage kill roots up to three eligible nearby foes within 2.6m and sight for 1.5 seconds; 3-second cooldown. The root deals no damage. |
| Emberwake Mantle | An enemy direct Fire hit avoided during actual dodge invulnerability grants 20% increased direct skill damage for 4 seconds; 3-second trigger cooldown. Burning and unrelated invulnerability cannot activate it. |

The new items are excluded from ordinary loot selection and starter inventory. Their powers may be extracted and engraved onto eligible equipment; multiple copies activate one power. Prepared state clears when its power is removed, the player dies or the encounter changes. New inactive combat fields are omitted from serialization to preserve historical field shapes.

## Client and release checks

The final integrated solution and client builds have zero warnings/errors; changed-source formatting verification passes. The generated content bundles were rebuilt through the normal `aw` compile pipeline, and the release engineering audit passes its fixture-integrity and file-presence checks. Existing public-release gates remain separate.

The headless journey passes **236 checks, 29 viewport clicks and 6,130 public gameplay commands** (`headless-3/secret-chambers-review.json`). The final packaged macOS capture run passes **257 checks, 29 viewport clicks and 6,130 commands**, with **21 captures** (`native-final/secret-chambers-review.json`). Strict runtime and export logs pass. The diagnostic uses a fresh Vanguard, earns all three source rooms, approaches clues with the actual navigation driver, rejects incorrect answers without mutation, exercises explicit challenge cancellation/acceptance, suffers and recovers from an actual guardian death, defeats all three guardians, claims each unique treasure, and verifies exact save/replay checkpoints and source-room restoration. Native confirmation decisions use explicit public signals.

Rendered captures were inspected for readable clue choices at 1280×800 and 960×720, the three regional chamber settings, recovery controls, completed discoveries and each item's equipped preview. Collection checks prove the three secrets stay hidden initially, appear only after discovery, and preview without changing gameplay state. The initial diagnostic claim failure traced to stale generated client content; rebuilding the bundles resolved it. It was not a reward transaction or missed-click defect.

The full Core suite passes **1,511 tests**, with zero failures/skips, in 11 minutes 22 seconds (`core-full.trx`). The server suite passes **17 tests** (`server.trx`). The final focused run passes **105 tests** in 26 seconds (`secret-focused-final.trx`), covering secret chambers, equipment, migrations, collection projections, and the last defeated-state and malformed-state validation changes.

Verified package SHA-256: `fe5be26619180affe82055de4e6bac9d4574a57f9cbb3b78895666ddf95dd4c7`.

Core tests additionally cover persistent won-but-unclaimed return, inventory capacity and forged/corrupt state. Native checks are scripted functional and visual evidence, not independent balance acceptance.

## Prism

Prism/Gemini reviewed the staged implementation twice with default secret redaction enabled.

Initial review `2f1c3b738ede0b4a2e218df4d92b5837` reported one high, two medium and three low findings. The high syntax-error claim and one medium constant-context claim arose from Prism replacing identifiers/strings containing “Secret” with `[REDACTED]`. The actual source compiles, and chamber context includes both chamber ID and attempt sequence. The Fire-dodge concern is inapplicable: both actual dodge invulnerability and its provenance timer are exactly seven ticks, and the provenance timer cannot be extended by another skill. The low draft-document note is resolved by this final evidence record. The other low suggestions already match the source: proximity feedback calls `Notice`, and all new power IDs are centralized constants in `LegendaryEquipment`.

Final review `5ac824c2ee4cbefe335785279705fc97`, with the redaction behavior explained in its review context, reports **zero high, one medium and one low**. The medium item-sequence concern assumes multiple unreprojected rewards in one call. The actual route reserves both arena sequences, grants exactly one item, immediately projects permanent inventory, refreshes the campaign arena, and reprojects the secret arena within the outer transaction; duplicate claims and all three earned claims pass. Its cited path is also nonexistent (`Combat/ProductionSecretChambers.cs`; actual file is under `Production`). The low suggestion concerns possible future non-`Poisoned` poison statuses; currently all qualifying poison damage uses `Poisoned`, and future hypothetical content is outside this change. No actionable unresolved defect remains from these reviews. Raw reports remain in `prism-review.json` and `prism-final.json`.

## Scope

The three chambers reuse established combat archetypes in separate optional arenas. Their puzzles and rewards are character-owned, not recurring chores or account-wide unlocks. They do not block the five-act campaign. Scripted checks establish functionality and reproducibility; independent balance acceptance, every-discipline playtesting and broader hardware certification remain separate gates.
