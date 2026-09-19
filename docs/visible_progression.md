# Visible character progression

Solo characters now display the equipped **MainHand, OffHand, Head and Chest** slots. Empty slots remove their weapon, shield or focus, headwear and outer armor while retaining a clothed body. Vanguard, Veilwalker, Arcanist, Gravecaller and Warden retain their discipline palettes and distinctive starter equipment. The other eight permanent equipment slots still affect stats; their individual items are not attached to the character model in this pass.

Weapons include the Ash Axe, Cinder Edge, Oath Hammer, Pilgrim Pike and Greatstaff. March Plate, Oath Plate, Ash Weave and Serath Shroud have distinct outer armor silhouettes. Rarity adds bounded ornamentation. Ashcleaver has an ivory spine, exposed ribs and a glowing axe edge; its awakened form adds illuminated bindings, Serath adds flame-shaped blades, and Orrun adds stone-colored reinforcement and pale seams. These forms reflect the item's existing awakening and permanent evolution state.

Active divine manifestations add body-mounted forms, independently or in combination:

- **Burning Blood:** charcoal patches and burning fissures across the torso and hand.
- **Whispering Shadow:** two translucent echoes behind the shoulders.
- **Stone Memory:** angular stone plates, shoulder spires and pale seams.
- **Voracious Renewal:** climbing vines, leaves and flowering buds.

## Inspect and equip

Press **I** to open Gear directly, or use **C → Gear**. Gear places twelve equipment slots beside a scrollable inventory grid. The Character tab and the wide Gear layout contain a live character preview; narrower Gear windows reserve space for the equipment and inventory. Click a card or select a slot and candidate item to inspect its appearance alongside the equipped-to-selected stat comparison. Choosing **Empty** previews removal of that slot. Selecting a candidate does not equip it or change the world character. **Show equipped item** restores the preview to the current loadout.

Stand near Torren in Greyhaven and drag inventory gear onto a compatible equipment slot, or drag equipped gear into the inventory panel to remove it. Compatible targets highlight during the drag. Replacement returns the previous item to inventory. The explicit **Equip selected** and **Unequip** buttons remain available. The existing Core slot, discipline and two-handed weapon rules remain authoritative. A two-handed candidate previews an empty off hand, but equipping it requires first unequipping the conflicting off-hand item. An equipped two-handed weapon also blocks equipping an off-hand item. Slot comparisons do not claim to be a full build damage calculation. See [drag-and-drop controls](gear_drag.md) for cancellation and persistence behavior.

Drag the preview or use its Left, Front and Right buttons to rotate it. Pausing freezes automatic poses and manifestation motion while still allowing deliberate preview rotation. Closing the panel disables its viewport rendering and processing. Reduced effects retain the manifestation silhouettes and loot models while suppressing their optional pulsing, sway and bobbing.

## Ground loot

Ground drops use recognizable weapons, armor, accessories and clothing across all 12 equipment slots. The six rarity tiers use color and one through six counted pips; a selected or nearby item also receives a separate highlight, and the nearest visible drop shows its name and rarity. Hollow ground markers leave the floor visible.

The existing rarity and discipline filters still control which drops are shown. Godwrought items remain visible, and the Show loot binding (default **Alt**) reveals all drops temporarily. Filtering never deletes an item or changes pickup range, eligibility or rewards.

## Presentation and resource bounds

`CharacterAppearance` projects equipped item definitions, rarity, evolution and active manifestations into a cosmetic descriptor. It contains no instance IDs or rolled stats. World models rebuild only when that descriptor changes; item rolls and progression remain in Core. Preview selection uses a temporary descriptor without changing saved state. Combat movement, attack timing, hitboxes, animation joints and replay decisions remain authoritative and unchanged.

The body mesh cache remains bounded at 96 templates and the shared immutable material cache at 256 entries. Equipment and manifestations use a separate cache of at most 160 independent module templates, keyed by slot and appearance variant rather than whole loadout combinations. Accent, burning-fissure and shadow materials belong to each character where their values animate. Geometry is built when an appearance changes, not during each animation frame. Ground loot has at most 19 model templates, 12 shared surface/rarity materials, six rarity-marker meshes and one selection-marker mesh; each drop owns its selection material.

Cooperative clients retain their existing fixed discipline equipment and ground-drop presentation. This pass does not add network loadout synchronization. Final texture work, LOD/skinning work and hardware performance acceptance remain separate production tasks.

## Reproduce the check

After compiling content and building the client, use a fresh directory under `artifacts`:

```bash
source tools/env.sh
appearance_output=$(mktemp -d "$AW_ROOT/artifacts/appearance-visual.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 2400 \
  --log-file "$appearance_output/smoke.log" -- \
  --appearance-smoke --capture-appearance --output="$appearance_output"
```

The isolated diagnostic exercises real Torren equipment transactions, inspection without mutation, pause and preview rotation, a dungeon run with loot and a manifestation, save/reload and replay. Structural checks cover discipline and item variants, individual and combined manifestations, empty slots, cache bounds and material isolation. Captures show equipped and inspected gear, dungeon loot, manifestations and a loot catalog. Omit `--capture-appearance` and add `--headless` for checks without screenshots. Rendered inspection is still required to judge framing and silhouette readability.
