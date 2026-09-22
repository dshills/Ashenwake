# Midgame legendary save compatibility

The midgame legendary expansion adds three item definitions and three special properties. It keeps the existing campaign catalogs, archive schema versions, and combat rules identity. New inactive build flags and combat-state fields are omitted when serialized so pre-expansion saves and historical replay hashes retain their original field shape.

On a catalog mismatch, the save reader reconstructs one published legendary catalog generation at a time: it removes the midgame trio first, then the original legendary trio if the archive predates those items. Campaign, production, endgame, Bound Memory, local-profile, and explicit Phase III import paths use this same ordered transition. Existing regional migrations remain available for older campaign versions.

A reader authenticates the original catalog identities and checksum and restores the original logical state before rebinding identities. Unknown catalogs, future schemas, corrupt checksums, invalid ownership, or new items smuggled into an old catalog are rejected. The upgrade changes only combat, resolved progression, and derived adventure identities; it preserves character items, equipment, receipts, XP, currencies, room caches, fog, RNG, pending actions, existing legendary charges and effects, Fracture manifests, and borrowed memory. It grants no retrospective rewards and does not replay completed encounters.

Loading remains read-only. The next explicit save preserves the validated original save and profile bytes in their normal backups. Replays keep their original catalog identity and must run against that catalog; newly recorded replays start from the upgraded state.

The exact base catalogs published at commit `9c63c93` are retained as test fixtures:

- `fixtures/combat-opening-depth.json`: SHA-256 `d9965c8fceed184d0900d6fc269381a38a46cd8166d2cf98f3799dad6635ce36`.
- `fixtures/progression-opening-depth.json`: SHA-256 `f317a566aed0786445f61163ef580eb878f94fe631d15ca358fe7e64fcb63ae7`.

`MidgameLegendaryMigrationTests` exercises those catalogs, while the existing legendary and regional migration suites exercise older releases through both additive legendary generations. The new checks cover active and cached rooms, pending casts, inventory and charge preservation, Bound Memory and Fracture/God Hunt runs, replay identity, original-file authentication, and exact-byte save/profile backups.
