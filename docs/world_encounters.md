# World encounters and Resonance Storms

Secure a campaign room, then explore its optional markers. Click a marker to walk to it, or approach and press **F**. The encounter opens at a safe threshold; its fight begins only after you choose it. **J → Encounters & Resonance Storms** shows nearby encounters and those you have entered. During an encounter, **J** opens its journal directly.

## Grey March stories

| Encounter | Source | Choice and reward |
|---|---|---|
| The Last Lantern | Road | Defeat the captors surrounding a stranded traveler. Claim **Grief’s Reprieve** and 10 materials from the supply cache. |
| The Mourning Caravan | Monastery | Follow the inscription and lay three keepsakes to rest for **Mantle of the Mourning Choir** and 10 materials. Alternatively, fight the spectral escort for the same equipment and 30 materials. |
| The Hungry Shrine | Monastery | Explicitly accept **25% more incoming damage during this fight**. Victory earns **Oathkeeper’s Reprisal** and 30 materials. The penalty ends with the fight or when you leave. |

The caravan remembers correctly resolved puzzle steps through leaving, saving, and loading. Wrong responses do not spend resources or erase steps. Completing either route closes the other route for that character.

## Regional Resonance Storms

Storms are optional encounters reached from a secured regional room. Each combines regional terrain hazards and enemy traits. Implanted fragment effects deal **25% more damage while the storm battle is active**; ordinary skill damage is unchanged. Terrain warnings and the encounter objective explain what to avoid.

| Storm | Source | Threats | Legendary reward |
|---|---|---|---|
| The Tolling Squall | Grey March: Road | Sonic lanes and Stormbound archers | Pyrebound Treads |
| The Briar Tempest | Verdant Maw: Living Ruins | Poison lanes, a Devourer vine, and pursuing swarms | Rotwake Signet |
| The Ember Cyclone | Cinder Reach: Extraction Floor | Conveyor fire, a heat tender, and emberlings | Furnaceheart Cinch |
| The Oathbreaker Gale | Shattered Spine: Bone Causeway | Staggered faults and warded defenders | Crown of the Unsworn |
| The Unmaking Front | Hollow Night: Repeating Rooms | Causal marks, rift attacks, and storm links | Greaves of the Stolen Hour |

Each storm also awards 25 materials. Storm fronts have authored locations and can be attempted when their source room is secured; they are not real-time schedules or changes imposed on an unfinished campaign battle.

## Leaving, rewards, and saves

- All eight encounters are optional and have one reward claim per character. They do not gate the campaign.
- Keep one backpack slot free before accepting an encounter. Victory grants access to a cache; approach it and claim the equipment and materials together.
- Retreat through the entrance or use **J → Return to the campaign**. Leaving an unfinished fight resets that fight without a reward. Death offers the same return and retry path.
- Completed encounters and unclaimed treasure remain recorded. You may return to claim the reward later.
- The source campaign room, your position, its ground loot, and permanent progression are retained. Equipment and build changes are unavailable inside optional encounters.
- Storm boosts and shrine penalties never become permanent character stats. Decorative motion respects pause and Reduced effects.

These encounters award existing named equipment through additional sources. Published content catalogs remain unchanged. The optional runtime state is omitted from older saves until an encounter is entered; existing commands do not acquire new automatic discovery mutations. Core owns combat, puzzle choices, completion, receipts, and replay. Godot presents the journal, scenery, warnings, and authoritative interactions.

Run the client diagnostic with `--world-encounters-smoke --discipline=Vanguard --output=<fresh-directory>`; add `--capture-world-encounters` for rendered captures. See [verification](world_encounters_verification.md) for the completed validation and review record.
