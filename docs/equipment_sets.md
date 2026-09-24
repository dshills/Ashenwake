# Two-piece equipment sets

Six Legendary armor pieces form three sets. They fit every discipline, occupy different slots, and can be mixed with existing Legendary weapons and jewelry. Equip both distinct pieces of a set to activate its bonus. Owning spare copies, keeping pieces in the stash, or engraving a property does not contribute to set membership.

| Set | Pieces | Campaign drops | Repeatable replacements |
| --- | --- | --- | --- |
| Vestments of the Last Vigil | Lanternkeeper’s Last Light (head), Vigil of the Unburied (chest) | Monastery, Bell Saint | Grey March Fracture rooms 1 and 2 |
| Briarbound Covenant | Thornmother’s Embrace (shoulders), Gravegarden’s Grasp (gloves) | Living Ruins, Rootheart | Verdant Maw Fracture rooms 1 and 2 |
| Ashrunner’s Oath | Cinderpilgrim’s Promise (belt), Embers Without End (boots) | Cinder Pack, Furnace Spindle | Cinder Reach Fracture rooms 1 and 2 |

These are additional ground drops after defeating the encounter's eligible enemies. Pick them up to acquire them. Existing named rewards and ordinary loot rolls remain intact. Previously cleared campaign encounters do not retroactively grant equipment; the repeatable Fracture sources remain available.

## Combat bonuses

- **Last Vigil:** absorbing hostile damage with a barrier prepares a spectral counter for four seconds. The next direct skill hit releases an additional 36 Void damage against a surviving target. The counter has a three-second cooldown after release and does not stack. Damage mitigation still applies.
- **Briarbound:** an owned poison damage-over-time kill heals up to eight nearby, living owned companions by up to 20 health each and creates a thorn patch. Companions must be within 3.5 metres and line of sight of the corpse. The patch has a 1.1-metre radius and deals 8 Physical Pierce damage every 20 ticks for two seconds. The bonus has a three-second cooldown; it can grow thorns even when there are no companions to heal.
- **Ashrunner:** evading an actual hostile hit during the dodge's seven-tick immunity window prepares the next direct skill hit for four seconds. That attack places up to three ember patches along a short path toward the target. Each has a 0.65-metre radius and deals 6 Fire damage every 20 ticks for two seconds. Walls and the global area budget can reduce the number placed. The bonus has a three-second cooldown after release. Dodging in empty space does not prepare it.

Core runs at 30 ticks per second. Set-generated damage cannot recursively trigger the sets. Removing a required piece or dying clears that set's prepared effects and active patches. New encounters and training attempts begin without carried-over charges.

## Inventory, appearance and collection

Drag pieces onto their equipment slots using the existing inventory controls. Item inspection shows the set bonus and equipped count; comparisons show the count before and after a proposed swap, including loss of an active bonus. Numerical item comparisons remain local to the inspected slot.

Open **J → Relics** or **B → Relics**, then choose **Sets**. Select a piece to see its partner, discovered status, equipped progress, and acquisition routes. Track the desired piece for Journey and Fracture guidance. Collection discovery persists after selling or salvaging equipment; it is separate from the equipped count. Set bonuses cannot be extracted or engraved. Ordinary compatible engravings can still be applied to set pieces.

Last Vigil pieces share ivory armor, lanterns and spectral blue accents. Briarbound pieces use overlapping leaves, thorns and venom pods. Ashrunner pieces use charred brass and glowing coals. Models and inventory icons change with the actual equipped pieces across all five disciplines. The combat HUD shows preparation, cooldowns and triggers, with green thorn patches and orange ember trails in the arena. Reduced Effects uses the existing restrained feedback path.

## Persistence

Set membership is derived from permanent equipped items. Combat timers and patches are optional saved state with bounded ownership, lifetime and count validation. Historical catalog fixtures remain unchanged; authenticated older saves migrate their catalog identity without granting the new pieces. The migration covers campaign, expedition, optional arenas and World Encounter combat snapshots, including active, completed and claimed states. Corrupt originals are preserved rather than silently replaced.

See [verification and Prism review](equipment_sets_verification.md) for automated and rendered evidence.
