# Acts II–III combat depth save compatibility

The paired catalogs advance to `campaign.midgame_depth.9` and `campaign-combat.midgame_depth.9`. The exact campaign and campaign-combat files from commit `99405ec` are retained as embedded predecessor catalogs. Archive schemas, combat rules identity, equipment definitions, rewards, and progression rules remain unchanged. The new inactive Forge Overcharge timer is omitted from serialized actor state, preserving the original field shape of older archives and replays.

`OpeningCatalogMigration` now begins with the midgame-depth transition before the existing opening-depth, pacing, and regional transitions. The reader reconstructs and authenticates the original catalog identities, checks the original serialized checksum, and restores the old logical state before any rebinding. The two additive legendary generations remain available at each campaign stage. The paired story resolver also recognizes the preceding opening-depth combat catalog when an explicit multi-build import uses that intermediate target.

The transition changes combat and story identities without replaying encounters, replacing live formations, or granting loot. Actor positions, live warnings, pending casts, cooldowns, RNG, equipment, inventories, receipts, XP, materials, room caches, fog, Fracture manifests, and Bound Memory ownership remain intact. New formations apply when the ordinary runtime creates the next encounter. Historical warnings retain their already announced geometry and timing; subsequent actions use the newly selected catalog's behaviors.

The pacing migration recognizes both published pacing and opening-depth story hashes. A character that already received the pacing adjustment cannot receive it again merely because the story catalog identity changes. Earlier supported archives still receive the existing authenticated one-time adjustment on their way through the ordered migrations. Testament claims in the new catalog use the same authored reward and receipt as the pacing and opening-depth releases.

Loading remains read-only. The next explicit save retains validated original save and profile bytes in their normal backups; corrupted primary saves can recover the same authenticated original backup. Unknown catalogs, future schemas, forged receipts, invalid actor positions, and changed field shapes without a matching original checksum are rejected. Historical replay records keep their original catalog and are not rewritten. New replay records begin at the restored current state.

## Frozen catalog bytes

- `fixtures/campaign-opening-depth.json`: SHA-256 `cb59961e8c26814f37e6e418c9fbf6e942717b01f6ecc0dac99ee7bb162930ba`.
- `fixtures/campaign-combat-opening-depth.json`: SHA-256 `0f673e01759cd530164eb3bbbb9c722b336ffa7accde8a47da21a9689997abb6`.

These fixtures preserve the campaign release shared by the opening-depth and midgame-legendary milestones. Older frozen fixtures are unchanged.

## Regression coverage

`MidgameDepthMigrationTests` compares every serialized field except authenticated catalog identities in the hub, all six Act II–III main encounters, and the Sealed Foundry. It also covers active old warnings and immutable historical replays, Fractures and Bound Memory, upgrading a character that predates the midgame legendary items, malformed original archives, read-only loading, exact-byte save/profile backups, and backup recovery. Existing opening-depth, pacing, legendary, and testament tests exercise the previous migration stages through the new target.

The focused migration run passed **120/120 tests**, with no failures or skips: 19 new midgame-depth cases, 29 pacing cases, 30 testament cases, 13 original legendary cases, 16 midgame legendary cases, and 13 opening-depth cases. The run used the repository .NET 8 test runner and current catalog files. Evidence is retained locally in `artifacts/midgame-combat-depth/migration/focused.log` and `artifacts/midgame-combat-depth/migration/tests/migration-focused.trx`. Native client validation and the milestone's final Prism review are recorded in the main verification report.
