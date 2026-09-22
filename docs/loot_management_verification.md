# Loot management verification

This milestone adds favorite and lock metadata to owned equipment, protection from destructive operations, saved-outfit awareness, inventory usage filters, and single-item salvage at Torren. [Player controls and rules](loot_management.md) describe the feature.

## Contracts under test

The Core tests cover independent flags, invalid ownership and command values, receipt reuse, false-field omission for historical snapshots, true-field save/restore, all five eligible rarity returns, material-cap rejection, equipped/Godwrought exclusion, exact removal, missing preset references, extraction/discard/salvage protection, and retained nonconsuming crafting. Runtime integration covers living-character and Torren/rescue restrictions, organization away from the hub, preservation through combat reconciliation, authoritative inventory projection, save checksums and replay.

`LootManagementSmoke` uses the shipping Endgame director and Gear UI. A fresh Vanguard marks owned equipment before Torren's rescue, earns the rescue through ordinary campaign commands, saves an outfit and unequips a starter item. The diagnostic tests visible filter membership, independent flags, blocked destructive controls and Core rejection, exact salvage preview, outfit warnings, cancel and stale confirmation, exact confirmed proceeds, missing outfit references, and save/replay. No items, experience or services are fabricated for this route.

The crafting regression uses its existing earned-character route. It equips an earned legendary at Torren to save an outfit, then visits Kesh. Favorite and lock each block the actual extraction preview, commit control and authoritative command. Clearing protection restores eligibility. The preview and permanent confirmation identify the outfit; a protection change invalidates an already-open confirmation. The normal confirmed extraction still removes the item and learns its real property.

Native diagnostics use explicit bounded commands and UI button/dialog signals; existing crafting pointer controls remain part of that regression. They do not claim an independent human playtest. The new diagnostic synchronizes presentation frames without advancing combat and records skipped captures on headless runs. Minimum-window captures and control bounds are evaluated separately from Core correctness.

## Results

The solution build passed with zero warnings/errors, and final `dotnet format --verify-no-changes` passed. The complete Core suite passed **1,276 tests** and the server suite passed **17 tests**, with no failures or skips. The focused Core run passed **71 cases**, including **17 new loot-management cases** plus adjacent inventory, preset and crafting regressions; these overlap the full suite and are not added to its total. Reports are `artifacts/loot-management/tests/core-full.trx`, `server.trx`, and `loot-core-focused.trx`.

The accepted packaged loot diagnostic is `artifacts/loot-management/native-03/loot-management-review.json`: **52 checks, 6 captures, zero skips, 191 explicit public commands and 2 replay verifications**. The command count covers the diagnostic's direct command calls; UI-dispatched actions are also present in the verified replay. It exited successfully with no runtime errors or warnings. Minimum-size inventory, management controls and salvage confirmation were inspected at 780×720. The initial headless pass passed 45 checks; its captures were explicitly skipped. Earlier native runs were superseded by the foreground ordering check and diagnostic audio teardown, which stops the active score before immediate process exit.

The existing native appearance/equipment regression passed **722 checks, 37 captures and 1,477 commands**, including actual viewport drag/drop. The extended native crafting regression passed **105 checks, 16 captures and 1,621 commands**, including the new protected extraction and outfit-warning checks. Their reports are `native-appearance/appearance-smoke.json` and `native-crafting/crafting-review.json` under the milestone artifact directory. Both exited successfully and passed the strict runtime log checker. Crafting ran after the final production changes; the subsequent package adjustment only releases audio in the new diagnostic's shutdown path.

The final `artifacts/export/Ashenwake.zip` SHA-256 is `ce64616e99b44ceb7848ec454f59a70107f5db2fcae4546cbde2125bffe04cd3`. The export log passed the export-specific checker. The entire unrelated release regression matrix was not rerun.

## Prism review

Prism reviewed the staged implementation through Gemini with default secret redaction under the user's standing approval.

- Run `7dead071ecefe0d7e366a9eaebfa235c` raised replay recording and repeated preset scans. The replay finding does not match the implementation: all three directors submit typed production commands, the outer runtime records them, and protected saves replay to the same hash. Nested `recordReplay: false` prevents duplicate histories rather than omitting the outer command. The preset-scan finding was addressed with cached membership in Gear and cached name lists in the crafting inventory.
- Follow-up `2c83aa6d357e4f6a8827c86905a2533e` raised signature allocation and dictionary ordering. Gear view signatures are computed during panel rebuilds after revision/range changes, not on every render tick; they retain restoration and drag invalidation guarantees. Both equipment maps are `SortedDictionary`, so the alleged undefined ordering does not apply. No further production change was warranted by those findings.

Raw reports and per-finding dispositions are retained in `artifacts/loot-management/prism-review.json`, `prism-disposition.json`, `prism-final.json`, and `prism-final-disposition.json`. Native visual inspection additionally found navigation overlapping Gear at minimum size; Gear now uses the same foreground/backdrop ordering as the other full character panels, with a diagnostic assertion for that ordering.

## Limits

Salvage yields are conservative fixed design values, not an economy-balance conclusion from these checks. The milestone does not add bulk salvage, automatic destruction, cross-character storage or a refund of previous crafting costs. Long-session performance and the release hardware/controller matrix remain separate acceptance work.
