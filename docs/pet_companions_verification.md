# Pet companions verification

Evidence is retained under `artifacts/pets/`.

The Core tests earn all three rescue locations through public campaign commands. They verify ownership, names, appearance restrictions, dismissal and selection, one character retaining all three companions, genuine source prerequisites, precise interaction range and obstructed line of sight, material capacity, one-time manual/automatic receipts, forged ledgers, optional arenas, save/load and deterministic replay. A historical pre-pet save fixture restores to its original authoritative hash. A historical replay fixture serializes byte-for-byte unchanged; its older progression content is outside the current replay runner’s compatibility scope. Current-content pet replay tests execute successfully.

The native diagnostic plays an actual early Vanguard campaign, secures the Road, approaches the fox and rescues it with viewport-delivered F input. Naming, coat selection, dismissal, selection, gathering preference, preview buttons and drag rotation use actual UI input. It checks world following, scene changes, pause behavior, saved collection isolation via the character browser, exact material increases and reload protection. The separate three-model gallery is a detached visual sample; it does not imply that all three rescues were performed in the native route. Later rescues are exercised in Core tests.

The final native run (`native5`) passed **94 checks**, including 19 UI clicks, six captures and 225 public commands. It also verifies that Escape closes the panel while the name field is focused. Wide and compact screenshots and all three model previews were inspected. The accepted engine log is clean. An intermediate native run encountered the normal focus-loss pause before its F input; the driver now explicitly resumes when closing its menus. The game’s interruption behavior is unchanged.

All **15 pet Core cases pass** (`core-tests.log`). Solution build completes with zero warnings/errors; formatting verification, export-script syntax and whitespace checks pass.

Pets are presentation-only followers, while ownership and material rewards are authoritative Core state. No combat actors or stat modifiers are introduced. Cached supplies are deliberately limited to five materials once in each of 15 ordinary campaign encounters. Native automation does not replace human playtesting or physical hardware certification.

## Prism review

Prism/Gemini reviewed the complete staged change with default secret redaction and `tools/prism-implementation.json`. Report `7820987d47b80957ead08bd43a24eb15` contains no high-severity findings, one medium and one low:

- The follower recall lookup now has an explicit player-position fallback, so presentation cannot throw when no nearby candidate passes geometry checks.
- Escape was already handled before the focused-name-field guard in `PetPanel._Input`. The added native check focuses the actual LineEdit and verifies that Escape closes the panel. No production input change was needed.

A complete follow-up attempt timed out at the provider (`prism-final.log`); focused follow-up report `82dee2f307f0079f14e9e34b50da5d93` reviewed the affected presentation/input files. It raised three allocation/UI concerns. The spatial query object is now reused between presentation updates and the rescue-marker string key has been replaced by bounded record comparison. Its high-severity allocation premise overstated `SpatialWorld`: its constructor retains a room reference and does not build topology, but removing per-frame wrappers is still a useful cleanup. The remaining UI suggestion is not a demonstrated defect: there are only three entries, updates occur on explicit choices or meaningful data changes while the modal is paused, unchanged views do not rebuild, and the model preview is retained. Native checks cover typing, saved/draft names, focus, scrolling and compact layout.

## Broader regression baseline

The full Core run completed **1,769 cases in 14 minutes 50 seconds: 1,768 passed and one failed** (`regression-tests.log`). It reports `RoamingChampionCatalogTests.PublishedStashCatalogBytesRemainFrozenAndOnlyThreeRewardItemsAreAdded` as failing: it still assumes the entire current catalog contains only the three items added at that older milestone. The same test was reproduced independently against unmodified commit `1308ff0` in a temporary source snapshot (`baseline-test.log`). Pet changes add no equipment or content definitions. This pre-existing assertion is recorded separately from companion validation.

## Playable package

The final exported macOS app passes **88 companion checks** (`package-final-pets`) and **33 release/pause/settings/recovery checks** (`package-final-release`), with clean accepted engine logs. Six native screenshot assertions are omitted in the headless package run. `tools/export.sh` includes the companion diagnostic in future package verification. The refreshed archive is `artifacts/export/Ashenwake.zip`, SHA-256 `6746b91fac5aff33c975380e03ae89b5bdc7233110fc88bffcc6d3ce1c2cafa0`.
