# Roaming champions

Three optional champions inhabit side areas in the first three regions. Secure a campaign room, then explore away from its exit for a named creature and challenge marker. Each character has one stable sighting location per region, selected from two rooms by its seed. Sightings do not move when you reload.

| Champion | Region and possible rooms | Counterplay | Signature equipment |
|---|---|---|---|
| The Bell-Torn Pilgrim | Grey March: Road or Monastery | Sidestep its dragging chain lane; interrupt the announced bell toll. | **Last Toll**, an off-hand with Unspoken Verdict |
| Widow of the Root | Verdant Maw: Living Ruins or Plague Village | Destroy the three poisonous nests to stop their blooms, and keep a path around the Widow. | **Broodkeeper’s Knot**, an amulet with Virulent Wake |
| The Cinder Tithekeeper | Cinder Reach: Cinder Pack or Extraction Floor | Evade furnace attacks, then strike during the visible open-vent window. Armor reduces damage outside that window. | **Tithebreaker’s Grasp**, gloves with Widow’s Echo |

These equipment pieces offer existing legendary powers in different slots. They have their own names, lore and exclusive champion sources; they do not appear in ordinary random loot or starter gear.

## Playing

- Click a sighting, or approach it and press **F**, to inspect the champion. Approaching records it under **J → Champions**.
- Enter its refuge, read its counterplay, then approach the challenge standard and confirm the fight. The foyer is safe until you accept.
- Keep one free backpack slot before challenging or claiming treasure. Visit the personal stash in Greyhaven if needed.
- During the fight, **J** opens the champion journal. **Return to the region** lets you retreat after confirmation; the world return marker provides the same route.
- After victory, approach the treasure and claim the signature equipment. Return to the region to continue the campaign.

Your source room, position, ground loot and progress are preserved. Retreating or dying before victory resets the unfinished fight. A defeated champion and its unclaimed treasure stay recorded through retreat, travel and reload. Each character can claim each signature treasure once. The campaign never requires these encounters.

The journal and legendary collection reveal a champion's source after discovery. Claimed equipment retains its normal ownership, extraction, crafting and storage behavior. A champion arena freezes permanent build changes and gives no ordinary enemy drops or additional XP.

## Implementation and diagnostics

Core owns seeded locations, proximity, encounter stages, combat, inventory capacity, permanent receipts and deterministic replay. Godot supplies the journal, explicit challenge confirmation, world markers and custom creature models. Collection and journal inspection do not start fights or travel.

The client diagnostic runs with `--roaming-champions-smoke --discipline=Vanguard --output=<fresh-directory>`. Add `--capture-roaming-champions` for native rendered captures. It earns its campaign route through ordinary gameplay commands. See [verification](roaming_champions_verification.md) for completed checks and review results.
