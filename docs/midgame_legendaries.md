# Midgame legendary builds

Three additional signature items support poison, owned summons and deliberate resource recovery. Their innate powers work through ordinary pickup, Torren's equipment service and drag/drop. Spare copies can be extracted and the learned power engraved through the existing specialist services. Wearing and engraving the same power does not stack it.

| Item | Innate slot | Campaign reward | Repeatable source | Engraving slots |
| --- | --- | --- | --- | --- |
| **Rotwake Signet** | Either ring | Act II · Plague Village | Verdant Maw Fracture · first room | Either ring, Amulet |
| **Mantle of the Mourning Choir** | Shoulders | Act III · Extraction Floor | Verdant Maw Fracture · second room | Shoulders, Chest |
| **Furnaceheart Cinch** | Belt | Act III · Furnace Spindle | Cinder Reach Fracture · final room | Belt, Boots |

Each reward replaces the last eligible enemy's ordinary drop in its room and retains the same two loot RNG draws. Collect the ground item to own it. The original Pyrebound Treads, Widow's Last Echo and Oathkeeper's Reprisal sources remain available, including their final-room Fracture rewards. Cleared-room state retains uncollected items when the existing route supports backtracking. Repeated Fractures award distinct instances for farming and extraction.

## Rotwake · Virulent Wake

When the player's attributed **Poisoned damage over time** kills an eligible enemy, spread one Poisoned stack to at most three living foes within 2.6 meters and line of sight of the victim. The power has a three-second cooldown. Spread poison cannot initiate another spread; a direct hit that finishes a poisoned enemy also does not trigger it. Immune or shielded targets are excluded. Existing poison keeps its original source and receives the normal bounded stack/expiry refresh.

Let poison finish a weak enemy near its companions. The effect claims no corpse, leaving the existing Heart of Serath and corpse-harvesting rules intact. Poison's existing damage, resistance, ownership and generation rules apply.

## Mourning Choir · Rallying Chorus

A successful direct player skill hit against a surviving enemy calls up to three living, unstunned, owned summons within six meters and line of sight of that enemy. Each deals an additional **8 base Void damage**. Normal offense and enemy defenses apply; the strikes do not crit or receive flat weapon/affix damage. A three-second cooldown limits the whole chorus, independent of target count.

Create companions, ancestors or spirits first, then attack the target you want them to strike. The power creates no summons and consumes no corpses. Summon attacks, damage over time, reflected damage and other triggered hits cannot initiate another chorus. Actor limits, generation limits and effect budgets remain authoritative.

## Furnaceheart · Cinder Cycle

An accepted skill costing at least 20 resource prepares one charge for six seconds. Another qualifying cast refreshes the duration rather than stacking charges. The discipline's next qualifying recovery action consumes it:

- **Vanguard, Veilwalker and Warden:** a successful direct hit with a zero-cost generating skill restores 12 additional resource.
- **Gravecaller:** successfully harvest an available corpse to gain 12 additional Remains after the ordinary harvest reward.
- **Arcanist:** resolve Vent to remove 12 additional Instability after ordinary venting.

Resource remains bounded from zero to 100. Feedback reports the actual change after those bounds. Rejected casts and invalid harvests do not prepare or consume charges; missed attacks do not release a charge. Once a qualifying recovery action succeeds, it consumes the charge even if the meter cannot benefit further. A paid cast that is later interrupted still paid its cost and retains its prepared charge until expiration.

## Presentation and persistence

The equipment has distinct authored silhouettes and inventory icons, lore, precise power descriptions and source hints. The combat HUD reads equipped state, cooldowns, charge lifetime and summon availability from Core. Trigger effects use bounded presentation pools, and required readiness text remains available with Reduced Effects.

New combat timers and build flags omit inactive defaults from serialized state. Charges/cooldowns clear on death, encounter replacement or removal of their owning power. Removing Virulent Wake also removes poison statuses originating from that power; ordinary poison remains governed by its existing ownership rules. Save/restore and replay preserve active states. [Catalog migration](midgame_legendary_migration.md) authenticates original archives before rebinding the additive item catalogs; old replay catalogs retain old rewards.

See [verification evidence and limits](midgame_legendaries_verification.md) for checks and review results.
