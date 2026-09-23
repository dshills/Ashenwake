# Late-game legendary builds

Three signature items reward interrupt timing, sustained attacks and an ultimate followed by a skill sequence. Collect their ground drops, then equip them at Torren using the existing equipment screen or drag and drop. Spare copies can be extracted and their learned powers engraved onto compatible equipment. An innate power and an engraving of that same power do not stack.

| Item | Equipment slot | Campaign source | Repeatable sources | Engraving slots |
| --- | --- | --- | --- | --- |
| **Crown of the Unsworn** | Head | Act IV · Contract Hall | Shattered Spine Fracture room 2; final Orrun God Hunt phase | Head, Off-hand |
| **Vow of the Last Witness** | Amulet | Act V · Identity Memory | Hollow Night Fracture room 2; final Serath God Hunt phase | Amulet, either ring |
| **Greaves of the Stolen Hour** | Legs | Act V · Breach Heart | Hollow Night Fracture final room; final Nhal God Hunt phase | Legs, Boots |

Each signature reward replaces the last eligible enemy's ordinary drop in its designated room, preserving the existing loot RNG draws. Pick up the item to own it. Existing legendary sources remain available. Completed encounters are not rewarded retrospectively after upgrading a save; repeatable sources provide additional copies.

## Unspoken Verdict

Successfully interrupt an enemy's active cast or unresolved warning with hard crowd control to gain **40 barrier**, subject to the normal 200 barrier cap. The power has a **three-second cooldown**. Controlling an idle enemy, striking an already resolved warning or attempting resisted control grants nothing. This makes the Crown useful against interruptible support enemies such as Contract Keepers.

The HUD shows readiness and remaining cooldown. Trigger feedback reports the barrier actually granted after the cap. The granted barrier follows normal combat rules; removing the power clears its cooldown but does not revoke barrier already earned.

## Witness's Vow

Hit the same surviving enemy with **four distinct direct player skill actions**, with each qualifying hit within **four seconds** of the previous. The fourth releases **24 base Void damage**, after normal offense and defenses. The additional strike cannot critically hit, receive flat weapon damage, or recursively trigger hit powers.

Changing targets, losing the target or waiting too long resets the sequence. Multiple hits from one action count once, including delayed projectiles and persistent areas. Summons, damage over time, reflected hits and other triggered damage do not advance the sequence. The HUD displays the count and remaining window.

## Borrowed Hour

An accepted, unlocked ultimate prepares **three charges for eight seconds**. Each of your next three accepted non-ultimate casts with a positive cooldown spends one charge and halves its assigned cooldown, rounded up to a whole tick with a minimum of one tick. The ultimate's cooldown remains unchanged.

Rejected casts and skills without a cooldown consume no charges. A cast accepted and subsequently interrupted has already spent its charge. Another accepted ultimate refreshes the three-charge window. The HUD displays the actual charges and remaining time.

## Presentation and persistence

The Crown, amulet and greaves have distinct procedural equipment models, inventory silhouettes, lore, source hints and event-driven feedback. Readiness remains visible with Reduced Effects enabled. Combat state belongs to Core; presentation does not decide whether a power activates.

Power state clears on death, encounter replacement or removal of its owning power. Save/restore and replay retain active cooldowns, target sequences and charges. Inactive new fields are omitted from serialization to preserve older state identities. See [save compatibility](late_legendary_migration.md) and [verification](late_legendaries_verification.md).
