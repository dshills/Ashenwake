# Acts II–III combat depth

Living Ruins, Plague Village, the Cinder fields and Extraction Floor now place enemies around distinct combat roles. Living Ruins combines a Devourer vine, a flanking needle swarm, a Bloom Carrier and a rear vine. Plague Village pairs its carriers with a forward swarm and a ranged vine. Each of these Act II rooms has four enemies instead of three. The Cinder fields bring the priest into supporting distance of the brute, with an Emberling and ghouls threatening the flanks. Extraction Floor places its Heat Tender between two forge sentinels.

Room collision, individual enemy stats, XP/material grants, boss timings and signature reward sources are unchanged. Extra ordinary enemies can drop ordinary items through the normal loot stream. The last eligible enemy still drops each room's signature reward.

## Spore mend

A Bloom Carrier can interrupt its normal attack rhythm to mend nearby wounded enemies. It announces a fixed **three-meter support circle for 1.4 seconds**, then heals eligible allies still inside it and in line of sight by **up to 18 health**. It cannot exceed maximum health or heal itself, bosses, ritual mechanisms or temporary copies. After a channel, it recovers for three seconds and performs an ordinary attack before it can mend again. Without an eligible wounded ally, it keeps attacking.

Kill or interrupt the carrier before its channel resolves, finish a wounded enemy before the heal, or draw that enemy outside the circle. The circle does no damage; the carrier's ordinary poison attacks and death burst remain dangerous.

## Forge bellows

A Heat Tender alternates ordinary heat attacks with a **1.4-second channel** when a forge sentinel is nearby. Its fixed **three-meter support circle** grants eligible sentinels still inside and in line of sight **20% increased damage for three seconds**. Repeated grants refresh the duration; they do not stack. Bellows do not heal, add barrier, empower the tender, or affect a boss or temporary copy. Channel recovery lasts three seconds.

Interrupt the tender or pull the sentinels out before the channel resolves. If it completes, avoid empowered sentinel attacks until the power expires. The buff belongs to its recipient for its remaining duration even if the tender dies. It clears when the sentinel dies or the encounter is replaced.

Both channels use the existing fixed warning geometry and cancellation rules. A hard-control effect or death before the resolve tick cancels the source's warning. Commands arriving on the resolve tick follow the existing tick order: due campaign warnings resolve first. Channel recovery is retained after interruption, so the source cannot immediately retry.

## Boss openings

Rootheart's tangle and delayed spores retain their existing geometry and deadlines. Breaking a feeding root removes its initial protection. Its recovery countdown appears only after protection, pending movement and all its announced attacks have cleared, and counts down the remaining actual recovery time.

Furnace Spindle retains its alternating vent direction, delayed slag discharge and guarded core. The guarded countdown represents its actual temporary defense. Once the guard and attacks end, the recovery countdown identifies the remaining opening. Recovery labels do not grant a new damage multiplier. Required warnings and countdowns remain available with Reduced Effects enabled.

## Persistence and validation

The paired catalogs are `campaign.midgame_depth.9` and `campaign-combat.midgame_depth.9`. Previous catalogs are frozen from `99405ec`; archive migration authenticates the original before rebinding. Existing actors and room caches retain their saved positions, wounds, warnings, rewards and RNG. New formations apply on encounter creation. Historical replay catalogs retain their original behavior mappings. The new forge timer omits its inactive default from serialized state and validates ownership and duration on restoration.

See [migration contract](midgame_depth_migration.md) and [verification evidence](midgame_combat_depth_verification.md).
