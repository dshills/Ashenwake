# Companions of the Ashen Road

Open **J → Companions** or **B → Pets** to manage your rescued travel companions. This collection is separate from the Hunter’s Bestiary and from Warden/Gravecaller combat summons.

Three companions await rescue:

| Companion | Default name | Rescue location | Appearances |
| --- | --- | --- | --- |
| Ashen Fox | Ember | Act I, the Road | Ashen or Ivory coat |
| Gloam Moth | Morrow | Act II, the Living Ruins | Moonlit or Dusklit wings |
| Cinder Beetle | Clinker | Act III, the Cinder Pack | Brass or Obsidian shell |

Clear the listed campaign encounter, then look near its entrance for the stranded creature and rescue label. Click it to approach and rescue, or approach and press **F**. Rescues remain available when you revisit a cleared region. Later companions stay hidden in the collection until you reach their act.

Your first rescued companion follows automatically. Use **Select companion** to take another along, or **Send companion home** to dismiss it without losing ownership. Each pet has two appearances and a saved name of up to 24 characters. **Save name**, appearance choices and selection changes save immediately; there is no separate Apply step. The panel shows a notice if saving fails.

Pets follow through campaign rooms, Greyhaven and optional/endgame arenas. They do not attack, have health, attract enemies, block movement, grant combat stats or occupy summon slots. They recall nearby when a scene changes or they fall behind. Pausing also pauses their movement. The front menu, defeat screen and training hide travel pets. Cosmetic changes are unavailable while a borrowed experimental build is active; an already selected companion can still follow.

## Optional material gathering

**Gather nearby materials** is off by default. Fifteen ordinary campaign encounters each have a small supply cache after the room is secured. Each cache grants **five materials once per character**. Anybody can collect a cache manually with a click or **F**, including characters with no rescued pet.

With a rescued pet selected and gathering enabled, approaching within 1.8 metres along an unobstructed line collects the cache on the next gameplay tick. Manual and automatic collection use the same reward receipt. Dismissing your pet stops automatic collection. Gathering does not collect equipment, salvage items, claim quest treasure or spend currency. Caches remain if the material wallet cannot accept all five materials. Optional arenas and training do not generate these caches.

## Saves and compatibility

Pets, names, selected appearances and collected-cache receipts belong to the character’s main save. Other characters have their own collection. Pet actions and material collection are included in deterministic replays. Existing saves receive the optional pet state when opened in the normal client; saves/replays without pet state keep their existing serialization and behavior. A future pet schema is preserved for its matching build rather than treated as recoverable corruption.

The retained legacy and networked co-op prototypes do not include permanent travel pets. See [verification and Prism review](pet_companions_verification.md) for the tested scope.
