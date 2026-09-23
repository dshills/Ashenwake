# Late-game legendary save compatibility

The expansion adds three combat item definitions and three progression properties without changing archive schema versions. New inactive build flags and legendary state fields are omitted from serialization.

Catalog fallback reconstructs one published generation at a time, newest first: late-game items, midgame items, then the original legendary trio. Existing campaign, production, endgame, Borrowed Memory, local-profile and explicit-import readers use the shared transition. Each reader validates the original catalog, checksum and logical state before rebinding catalog identities.

Migration preserves equipment, inventory instances, currency, XP, receipts, room caches, pending actions, RNG, existing legendary state and expedition progress. It grants no new items for already completed encounters. Unknown or corrupt archives and items incompatible with the original catalog remain invalid.

Loading is read-only. The next explicit save preserves the validated original save and profile bytes in their ordinary backup files. Historical replays continue to require their original catalogs; new replays use the upgraded identity.

The exact combat and progression catalogs at baseline commit `f93bbbc` are retained as `fixtures/combat-midgame-legendary.json` and `fixtures/progression-midgame-legendary.json`. `LateLegendaryMigrationTests` checks these fixtures, original-file authentication, campaign and production state, cached rooms and pending actions, Fractures, God Hunts, Borrowed Memory, replay identity, and exact-byte save/profile backups. The older published fixtures remain unchanged.
