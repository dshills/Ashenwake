# Acts IV–V combat depth save compatibility

The paired catalogs advance to `campaign.late_depth.10` and `campaign-combat.late_depth.10`. The exact campaign and campaign-combat files from commit `6259723` are retained as embedded predecessor catalogs. Archive schemas, combat rules identity, progression and equipment definitions, rewards, and serialized state fields remain unchanged. The new Oath Ward channel uses the existing campaign hazard and barrier state; boss recovery, protection, and attack-sequence labels are read-only views.

`OpeningCatalogMigration` starts with the late-depth transition before the existing midgame-depth, opening-depth, pacing, and regional transitions. The reader reconstructs and authenticates the original catalog identities, checks the original serialized checksum, and restores the old logical state before any rebinding. Earlier additive legendary generations remain available at each campaign stage. Explicit multi-build imports recognize the published midgame combat bundle and use its matching story when authenticating the source archive.

Migration changes authenticated combat and story identities without replaying encounters, replacing live formations, or granting loot. Actor positions, barriers, forge overcharge timers, warnings, pending casts, cooldowns, RNG, equipment, inventories, receipts, XP, materials, room caches, fog, Fracture manifests, and Bound Memory ownership remain intact. New formations apply when the ordinary runtime creates the next encounter. Active Oath Mark, Covenant Fault, and delayed Breach Heart warnings retain their announced geometry and deadlines. Subsequent enemy decisions use the new catalog behaviors.

The pacing migration recognizes the published pacing, opening-depth, and midgame-depth story hashes. Characters that already received the pacing adjustment cannot receive it again when their story identity changes, including explicit imports that skip an intermediate reader. Earlier supported archives still receive the existing authenticated one-time adjustment. Testament claims use the same authored rewards and receipts as those published releases.

Loading remains read-only. The next explicit save retains validated original save and profile bytes in their normal backups. Corrupted primary saves can recover the authenticated original backup. Unknown catalogs, future schemas, forged receipts, invalid actor positions, and changed field shapes without the original matching checksum are rejected. Historical replays keep their original catalog identities and bytes; new replay recordings start at the restored current state.

## Frozen catalog bytes

- `fixtures/campaign-midgame-depth.json`: SHA-256 `6c8776ff00e23d6503845195a786e4d10725e274a377c3e9a19c9346bc9d21b0`.
- `fixtures/campaign-combat-midgame-depth.json`: SHA-256 `ad50d736346e9363e4f5e15aa08bc67d1710d4df735a8be26865aeb66e7aaea3`.

Both files are exact bytes from the accepted Acts II–III combat-depth commit. Older frozen fixtures remain unchanged.

## Regression coverage

`LateCampaignDepthMigrationTests` compares every serialized field except authenticated catalog identities in the hub, all six Act IV–V main encounters, the Oathkeeper Archive, and the Unremembered Vault. It covers old Oath Mark and late-boss warnings, active midgame support, immutable historical replays, Fractures and Bound Memory, explicit cross-build imports, malformed archives, read-only loading, exact-byte save/profile backups, and backup recovery. Existing midgame-depth, opening-depth, pacing, legendary, and testament tests exercise the previous stages through the latest target.

The focused migration checks passed **144/144 tests**, with no failures or skips: 24 new late-depth cases, 19 midgame-depth cases, 13 opening-depth cases, 29 pacing cases, 30 testament cases, 16 midgame legendary cases, and 13 original legendary cases. The first run passed 131 cases; a second run covered the 13 original legendary cases omitted by the initial name filter. Both used the repository .NET 8 test runner and current catalogs. Evidence is retained locally in `artifacts/late-campaign-depth/migration/focused.log`, `legendary-predecessor.log`, and the corresponding TRX files under `migration/tests/`. Native client validation and the milestone's Prism review are recorded in the main verification report.
