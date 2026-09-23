# Personal stash verification

Baseline: `741b1a5` (optional secret chambers). Evidence is retained under `artifacts/personal-stash/`.

## Ownership and access

Four named tabs hold 128 items each. Stash locations reference the existing permanent item ledger; transfers do not clone, reroll, destroy, or reidentify equipment. Favorites, locks, affixes, engraving, evolution and saved outfit/build references remain intact. A stored item is unavailable to combat inventory, equipment, crafting, extraction, salvage and discard until retrieved. Collection ownership remains intact.

The chest requires Torren's rescue, a living character in Greyhaven and the exact 1800-unit interaction range. Active journeys and Borrowed Memory experiments block changes. Transfer, tab movement and rename commands revalidate access and capacity in Core. Equipped items cannot be deposited. Loading a character closes the panel and invalidates previous drag payloads through an independent session token.

Unused stash state is omitted from serialization. Restoration validates tab names, locations, capacities and ownership; future stash schemas protect the archive from replacement. Cleared campaign rooms are reprojected after transfers, and revisiting them derives carried inventory from permanent ownership. The legacy Adventure Godwrought ledger represents Ashcleaver specifically: stored legacy identities are excluded from its automatic reward projection, including after rejected transactions restore runtime fields.

## Automated checks

The focused run passes **116 tests**, including **18 new stash cases**, in 8.7 seconds (`core-focused.log`). Coverage includes transfer identity and metadata, equipped-item rejection, destructive/crafting restrictions, tab and backpack capacity, names, malformed ownership, exact 1800/1801/2600 proximity boundaries, rescued-Torren access, active journeys, saved builds, cached room reentry, replay, archive protection, character independence, evolved Godwrought metadata, rollback and duplicate combat projection rejection. Existing Production, Expedition, equipment preset, build loadout and loot-management cases are included.

The server suite passes **17 tests** (`server.trx`). The release engineering audit passes automated fixture integrity and file-presence checks; its established public-distribution gates remain separate.

The initial full regression run passes **1,511 tests** in 12 minutes 31 seconds (`core-full.trx`). The final full run passes **1,529 tests**, with zero failures or skips, in 13 minutes 2 seconds (`core-final.trx`), including the final Godwrought fix and all 18 new stash cases. Final builds have zero warnings or errors; changed-source formatting verification and strict export/runtime log checks pass.

## Client checks

The first passing headless diagnostic has **82 checks**, **359 public gameplay commands**, **7 clicked actions** and **5 drag gestures** (`headless-3/personal-stash-review.json`). It earns Torren through gameplay, uses the actual chest navigation, deposits/moves/retrieves protected equipment, renames and filters tabs, checks keyboard isolation and stale payload rejection, follows saved outfit/build retrieval controls, and verifies save/load/replay and carried-inventory exclusion through campaign travel. Both 1280×800 and 780×720 layouts fit the viewport.

Early diagnostic failures were setup assumptions: build saving needed an actual visit to Mara, and a build-name search correctly matched all referenced carried items as well as the stored item. The resulting inspection also caught cramped Rename/Clear buttons, which now have explicit minimum widths.

The final gameplay build passes **89 native checks**, **359 commands**, **8 clicked actions** and **5 drag starts**, with four rendered captures (`native-recheck/personal-stash-review.json`). A subsequent packaged run also passes (`native-instrumented`). The chest, wide panel and compact panel were visually inspected. Same-tab selection retains card instance identities, and layout reconciliation is bounded to four settling frames after content or viewport changes.

The final native diagnostic (`native-diagnostics`) also passes all 89 checks and records valid drop targets, unchanged drag epochs, focused windows and clean panel state for every transfer. Its assertions and captured layouts match the packaged run. Verified package SHA-256: `b062468795b8f4b1c8a2d0593427a045baaa693766c0be71f7294e29e2830123`. The source diagnostic additionally serializes its drag evidence; gameplay code is identical.

One earlier native run rejected its second deposit gesture after retrieval, leaving the item and metadata unchanged in the backpack. The same unchanged gameplay binary passed the repeat run, and the next packaged run also passed. No deterministic transfer defect was reproduced; the diagnostic now records payload validity, epoch, mouse/hover target and window focus to help distinguish future native input interruptions. This isolated failed gesture remains in the evidence rather than being represented as an uninterrupted first-pass success.

## Prism review

Prism/Gemini review `7b6af895e2d14b4cca59de35fb838df5` ran with default secret redaction enabled. It reported one high, one medium and two low findings. The high finding hypothesizes future non-Ashcleaver entries in the legacy Adventure ledger; that ledger and its importer currently represent Ashcleaver specifically, while other items use canonical inventory projection. Changing this filter to rarity would be inconsistent with the existing ledger identity rules. Dedicated tests cover stored Ashcleaver, its evolution, restoration and failed-transaction rollback.

The medium selection-node-churn finding is addressed by updating selection and details in place, with a diagnostic that checks card instance identities. The per-frame layout suggestion is addressed by using the existing resize/open/rebuild paths. The remaining low name-length suggestion confuses two intentional limits: 128 bounds raw input before trimming; 32 bounds the displayed tab name. Code comments document both distinctions.

The second review, `9a5b036c25a76514ca4d6a8deb223e8b`, reports two high performance findings based on health/buff updates rebuilding an open stash. That execution path does not exist: `PersonalStashPanel.UpdateModal` holds the `personal-stash` pause, the sandbox does not tick gameplay during that pause, and `RefreshPersonalStash` skips closed panels. Its view key also skips repeated refreshes, and `Observe` does not increment `_revision` for ordinary health changes or buff expiration. Transfers and tab changes intentionally refresh the displayed ownership state. The second report also cites `PersonalStashPanel.cs` for code actually located in `EndgamePersonalStash.cs` and proposes nonexistent durability/socket state. No additional actionable defect was established. Both raw review reports are retained; this is not a claim that Prism returned zero findings.

## Scope

Storage is per character and available in the solo campaign interface. It is not an account-wide trading facility. Scripted functional and rendered checks do not replace independent playtesting or broader hardware certification.
