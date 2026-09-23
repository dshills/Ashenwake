# Legendary collection journal

Open **J → Relics** or **B → Relics** to inspect the nine signature legendary items and any secret relics you have revealed. The journal pauses the world. Its item cards, rotating character preview, power descriptions and source guidance help plan a build without granting or equipping anything.

**All**, **Collected** and **Missing** filter the collection. Collected means the character has discovered the item, even if no copy remains. Each item shows the current owned-copy count and whether its innate power has been learned through extraction. Uncollected visible items expose their equipment appearance and combat power; their lore stays hidden until discovery. The three hidden-chamber relics do not appear at all until their doorway is revealed or the item is owned.

Choose **Track this item** to retain one target. Journey and the expedition board show its name and a suggested source; clicking the tracking line returns to the collection. **Stop tracking this item** clears the target. Tracking a missing item never adds it to the discovered count.

## Finding an item

The source list explains campaign, Fracture and supported God Hunt routes:

- Unreached campaign encounters show their act and a progression requirement. Encounter names appear only once reached. A cleared campaign source explicitly explains that revisiting does not grant another copy.
- Fractures identify the correct region and room, an owned matching Sigil when available, and any unmet campaign or Sigil requirement. Sigils from other regions do not satisfy that source.
- Revealed hidden chambers identify their one-time treasure. Defeated guardians keep unclaimed treasure available, and claimed chambers never promise another copy. **View discovery** opens the chamber journal without traveling. Extraction consumes the unique item, so its collection card explains the one-copy limit.
- God Hunts use the character's actual unlocks. Secret hunt names and destinations remain hidden while locked. Known locked hunts explain their tier requirements.

**View Journey route**, **View Fractures** and **View God Hunt** select the relevant existing screen. They never travel, consume a Sigil, abandon a run or begin a hunt. The normal gate, combat, loot-departure and permanent-choice controls remain authoritative. An active expedition routes campaign inspection to the run screen until the player returns.

Escape closes the journal. I, C, J, B and H transfer explicitly to their corresponding screens. In particular, H opens Echoes rather than performing its contextual in-world binding action. Closing the collection preserves other pause owners, and its preview stops rendering while hidden.

## What is remembered

Discoveries come from actual owned item definitions and learned innate powers, not quest completion, seeing a ground drop or previewing an item. Successful pickups persist collection knowledge; tracking and ordinary saves persist pending changes as well. Salvage or extraction does not erase a remembered discovery. Old characters can recover discoveries from their current inventory and learned powers; items destroyed before this feature existed cannot be inferred.

Knowledge and the single tracked target live in an optional `<character-save>.legendaries.json` sidecar. They are separate from gameplay snapshots, profile rewards and replay hashes. Loading is read-only, with validated backup recovery. Corrupt, incompatible or future files remain untouched, and the journal explains when changes can only last for the current session.

Each sidecar is bound to the character ID and save filename. Separate character slots and Echoes characters retain independent journals. Moving a save folder with its sidecars preserves discoveries and tracking, including on another computer. Keep the save filenames unchanged when moving them; renaming a slot makes its old sidecar protected.

See [verification](legendary_collection_verification.md) for automated, native and review evidence.
