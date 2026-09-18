# Fractures and God Hunts

Phase 5 connects the campaign, permanent progression, deterministic expeditions, actual combat, and local archives through `EndgameRuntimeSession`. The Godot endgame scene and the CLI use the same public commands. `EndgameSession` is the bounded run/reward ledger; it cannot replace observed combat victory in the runtime.

## Ownership and commands

`Create(composedCombatJson, adventure, progression, campaign, endgame, seed, discipline, profile)` starts a fresh character with the whole campaign available. `Restore` takes those five content arguments and an `EndgameRuntimeSnapshot`. The composed combat catalog contains the campaign and endgame overlays; the latter embeds the exact `EndgameContent` policy. A mismatched policy hash is rejected.

The runtime exposes `Campaign`, `Production`, the active `Combat`, its authored `Room`, `View`, `RunView`, `Interactions`, `WorldEvents`, and `StateHash`. Input goes through `Execute(EndgameRuntimeCommand)` or its convenience methods: `ClaimRecoverySigil`, `AttuneSigil`, `StartFracture`, `StartGodHunt`, `AdvanceEncounter`, `RetryEncounter`, `Abandon`, `ReturnToHub`, `ExecuteCampaign`, and `ExecuteProduction`. The nested objects are inspection/integration surfaces; the client routes mutations through the outer coordinator so its atomic state and replay remain authoritative.

Permanent item instances, equipment, common materials, mastery, anatomy, and catalyst inventory belong to `ProgressionSession`. Campaign and endgame arenas receive validated projections. Endgame ability receipts have their own run/attempt/room namespace; returning to an earlier campaign clock cannot collide with them. Imported loot retains unique long instance IDs, rolls, and per-instance Godwrought history. Reanimated, already-consumed corpses do not award another awakening kill.

Only this character's valid campaign ending unlocks entry. A shared profile's Fracture or hunt discovery cannot bypass the campaign or grant power to a new character. The Greyhaven gate is a real interaction point; entry, recovery, and attunement require its range. Active expeditions block campaign travel and permanent workshop actions until completion or explicit abandonment.

## Sigils, generation, and attempts

A Sigil records region, tier 1–10, seed, one to three compatible rules, boss family, and reward tendency. The finite generator shuffles eligible candidates once and filters exclusions symmetrically. `PreviewSigil` exposes all rules/counterplay, room names, selected and skipped inheritance, and the manifest hash before commitment.

The combat manifest deterministically chooses three authored regional packs and one family boss. It records static arena/template IDs, exact spawns and elite traits, room seeds, rules, run identity, and inheritance decisions. Restore regenerates the manifest from its immutable Sigil or hunt source and compares it, rather than trusting saved generated encounters. Each arena uses its actual Core collision geometry for combat and navigation.

Starting a Fracture consumes that Sigil once and grants three attempts. Every observed player death consumes one attempt; the dead arena waits for an explicit Retry, and the final death produces a failed outcome. Retry resets the current unfinished room and its scoped effects; previously cleared rooms remain cleared. Entry and retry restore anchor health/potions. Moving between won rooms retains current health and consumable use. A completed room pauses for loot inspection; Advance commits its XP receipt and enters the next room. It may leave disclosed ground drops, including when the 512-item projected inventory is full. Actual victory seals hostile hazards during the loot pause. Completion retains the final arena until Return to Hub.

Abandonment consumes the started Sigil, grants no final reward, and returns to Greyhaven. A failed run can return there explicitly. If no unconsumed Sigil and no active run remain, the gate provides a free tier-one recovery Sigil. Its seed comes from the stored campaign seed and Sigil sequence; callers cannot submit a replacement seed or repeatedly claim while keeping an unused Sigil. Successful Fractures grant a next-tier Sigil, capped at tier ten. There is no schedule, account service, daily cap, or reset.

Run starts reserve permanent receipt headroom before consuming a Sigil. Sigils, run history, room receipts, rule entities, and operation histories have explicit limits. An exhausted archive returns a preservation/migration error before commitment; it does not create a boss whose permanent reward cannot be stored.

## Actual rules and inherited traits

| Rule | Executed behavior |
| --- | --- |
| Fevered Cinders | Burning enemies move at 125% base speed. |
| Borrowed Relief | Positive player healing creates a delayed, announced hostile echo attack; creation is deduplicated and capped at eight per room. |
| Scars of the Mighty | First eligible elite deaths create announced hazards that persist until room exit/victory; at most eight are created. |
| Unequal Shelter | The highest equipped family resistance contributes one quarter of its value as damage bonus; the lowest family loses 1,500 basis points. |
| Divine Surges | Fragment-owned effects gain 50% during the first 30 ticks of each 120-tick room cycle. |
| Accumulated Memory | Three prior rooms nominate elite traits in order; the boss keeps at most two distinct compatible traits. |

Family resistances are real permanent armor/jewelry affixes and a validated combat projection across all nine damage families. Ties use a stable enum order. The exposed family's penalty may be negative; the UI receives the actual families and values. Overcharge applies to fragment-owned effects and their attribution, with normal proc/entity budgets preserved.

Healing echoes and persistent elite scars are mutually exclusive. Inheritance shares Combat's maximum of two and its exclusions: Mirrorborn/Gravewake, Null/Hunter, and Devourer/Martyr cannot coexist. Duplicates, incompatible candidates, and a full two-trait cap are recorded as deterministic skips. The four-room scope deliberately caps inheritance instead of allowing a third trait with an unrelated compatibility policy.

The Core adapter owns actual effects, counters, attribution, timers, mechanisms, and cleanup. The ledger's isolated numeric-rule helpers are diagnostic contracts, not another source of combat effects. Death/retry, abandonment, room changes, victory, and hub return remove the appropriate scoped hazards and temporary Godwrought stacks. Normal actor/projectile/area/chain caps remain enforced.

## Hunts and permanent rewards

| Hunt | Required cleared tier | Three authored phase themes | Catalyst |
| --- | --- | --- | --- |
| The False Vael | 3 | Forge vents, rebuilding limbs, solar core | Vael rib |
| Ilyra Reborn in Teeth | 4 | Jaw roots, brood channels, exposed seed | Ilyra seed |
| The Thousand Memories of Serath | 5 | Processions, repeating echoes, silent bell | Serath memory |
| Orrun Without an Oath | 6 | Faults, broken terms, empty contract | Orrun oath |
| The Shape That Remembers Nhal | 8 and all four primary hunts | Absent form, marked copy, remembered door | Nhal absence |

The four primary hunts and the optional Nhal hunt have actual phase arenas, weak points/interactions, counterplay, and two attempts per run. The secret hunt is optional for ordinary Fracture progression. Completion requires each phase's actual enemies/mechanisms to resolve; client labels cannot mark a phase won.

Each cleared room grants `100 + 25 × tier` character XP. Final rewards grant another `100 × tier` XP, common materials, starter-skill mastery for the active discipline, and the declared catalyst when applicable. The base common-material reward is `20 + 5 × tier`, reduced by 20% per death to a 40% floor; Materials tendency adds ten. Mastery tendency grants `100 + 10 × tier` mastery rather than 25. Godwrought tendency grants a divine catalyst. Common currency caps saturate while reward receipts and victory still commit.

Final run status, completed-room history, permanent rewards, catalyst ownership, and next Sigil commit in one runtime transaction. Room and final-reward receipts are validated in both directions, including XP/mastery/catalyst/profile payloads. Phantom permanent receipts are rejected. Catalyst ownership plus permanently spent catalyst counts must equal authoritative rewarded quantities. Historical award totals in `EndgameSession.View` are descriptive counters; only `Production.ProgressionView.Materials` is spendable currency.

Endgame-enabled Divine Grafting retains the 1,000 burning-kill awakening threshold, owned lineage fragment, common-material fee, and explicit permanent branch confirmation. It additionally consumes the explicitly selected Serath memory or Orrun oath in that same transaction. Previously evolved imported items remain evolved without a retroactive charge. Godwrought Tempering can explicitly consume one selected catalyst instead of its common-material fee, adding two damage-affix points up to the existing ten-point limit. Failed eligibility, insufficient common materials, capped tempering, and changed operation retries consume neither wallet nor catalyst. The UI names the exact consumed catalyst and resulting effect; no substitution occurs silently.

## Archives, compatibility, and evidence

The joint archive contains the campaign/story and canonical production snapshot, run ledger, optional active arena, manifest, clear/retry flags, and global operation sequence. Atomic primary/backup writes use a character lease and the validated local-profile merge. Profile merging resets the replay origin. The outer replay keeps a bounded 1,800-operation window; verification drivers check overlapping windows every 600 commands to cover the whole route.

Raw schema/rules/content headers are inspected before strict typed parsing, including nested permanent/endgame-combat/manifest headers. Future or mismatched archives remain compatibility errors and are preserved rather than replaced by a backup. Corrupt known-format saves may recover a valid backup. The reserved shared-profile filename cannot be used for a character archive.

`EndgameRuntimeMigration.ImportPhaseFour` validates the original Phase 4 archive under its original composed catalog first, requires Greyhaven, then explicitly rebinds the retained character/story to the new catalog. It never writes source data. The CLI requires a new destination. The maintained real `fixtures/phase4-campaign-complete.json` and its five original source catalogs have a byte-hash manifest and provenance from commit `20e1534`; regression tests verify those original bytes and retain earned items, XP, choices, ending, and profile metadata.

Use `aw endgame validate`, `compile`, `demo`, `exhaustive`, `benchmark`, `replay <file>`, and `migrate-phase4 <source> <new-destination>`. `EndgameRuntimeSmoke.Next/Complete(session, targetTier, allHunts)` emits ordinary player commands: fresh campaign-to-Fracture routes, Sigil progression, and actual hunts. It does not inject victories, gear, or rewards. Runtime tests cover maintained migration bytes, death/retry/recovery, rule cleanup, receipts, catalyst transactions, full inventory/capped currency, replay, nested future schemas, and actual encounter routes. Combat tests separately measure rule effects, mechanisms, manifests, budgets, and all-five-discipline reference builds.

These are playable engineering and greybox presentation contracts. Automated completion is not evidence of human readability, enjoyment, final art/audio, or supported-platform release acceptance. Exact build hashes, route measurements, exported-client checks, and the remaining qualitative/platform gates belong in the phase verification report.
