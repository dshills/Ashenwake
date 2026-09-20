# Equipment drag and drop

Press **I** to open Gear directly. The left grid arranges all twelve equipment slots around the body; the right grid contains owned, unequipped items. Equipment has recognizable icons and borders matching the six ground-loot rarity colors. Empty slots retain muted equipment silhouettes. At wider window sizes, the rotating character preview appears beside them. Click a card to inspect it and compare stats before committing. **C → Gear**, the slot/item selectors and the explicit Equip/Unequip buttons remain available.

Hover or keyboard-focus an item to see it beside the currently equipped item. Damage and armor include base values and rolled affixes; critical chance is shown as a percentage, with changes in percentage points. Signed numbers and colors distinguish gains and losses. Other rolls, properties, engravings and Godwrought progression appear when relevant. The comparison covers one slot, and explicitly excludes effects on other equipment from a two-handed swap. Hovering never equips, changes your selected preview or mutates saved state.

Use **Search items**, **All types** and **All rarities** to narrow the backpack. Filters combine, and the header shows visible/total item counts. **Clear** restores every backpack item and type sorting. Sort by type, highest rarity or name; equal items use instance ID as a stable tie-breaker. Filtering and sorting preserve ownership and only arrange the display. Search pauses the world while it has keyboard focus, including after loading a save, and preserves any existing pause when focus leaves.

Stand near **Torren in Greyhaven** to change gear:

- Drag an inventory card onto a compatible equipment slot to equip it. Valid targets highlight while dragging.
- Drop onto an occupied slot to replace its item. The previous item returns to inventory.
- Drag equipped gear onto the inventory header, an inventory card, or the open inventory background to unequip it.
- Scroll the inventory to reach additional items. Filtered-out items still belong to you; clearing filters reveals them again. There is no manual item-position saving in this interface.

Slot, discipline and two-handed weapon rules still apply. Remove your off-hand item before equipping a two-handed weapon; remove or replace a two-handed weapon before equipping an off-hand item. You can inspect and drag gear away from Torren, but cannot commit a change there. A message beside the pointer explains an incompatible slot, required discipline, hand conflict or the need to visit Torren; rejected-drop messages remain briefly after release.

Dropping outside a valid target, onto an incompatible slot, or back onto the same slot changes nothing. **Escape** cancels an active drag. Opening another menu, closing the equipment panel or losing application focus also cancels it. The world pauses during an equipment drag, and finishing the drag preserves any pre-existing pause. Dropping an item outside the panel never discards it.

To free inventory space, stand near Torren, select an unequipped backpack item, and choose **Discard selected item…** below its equipment controls. The confirmation names the item and explains the permanent loss; **Keep item** has default focus. Discarding grants no materials or rewards. Equipped items and your last Godwrought weapon are protected. An extra Godwrought copy can be discarded, including its own awakening and evolution progress. You can also select gear for another discipline to discard it.

The inventory holds 512 items for loot collection, including equipped gear. Characters with larger existing collections must discard until fewer than 512 items remain to collect again. Canceling the confirmation, closing Gear, changing the character/loadout, or losing application focus leaves the item intact and releases only the confirmation's pause. Successful discards are saved and replayed as ordinary authoritative transactions.

Successful drops use the existing equipment transactions, so stats, world appearance, preview, saves and replay all reflect the same committed item. Weapons, off hands, helmets and chest armor have visible model attachments; the other eight slots retain their existing stat effects. If ownership or the current loadout changes during a drag, that old drag is rejected.

This interface is available in the solo Endgame, Campaign and Production scenes. The cooperative equipment interface is unchanged. The layout takes inspiration from Diablo's equipment-and-backpack arrangement and uses Ashenwake's existing Godot controls and styling.

## Reproduce verification

The appearance diagnostic uses native viewport press, motion and release events to exercise actual Godot drag/drop. It checks equipment removal and replacement, ownership conservation, invalid and canceled drops, stale payloads, service gating, two-handed rules, appearance, save and replay. Inventory checks add named icons, read-only comparisons, filtering and deterministic sorting against earned items, cached controls, search pause restoration and rejection messages. Discard checks cover the actual confirmation buttons, cancellation, stale context, pause ownership, item removal, save and replay. The mouse-actions diagnostic checks the shipping director's **I** routing.

```bash
source tools/env.sh
gear_output=$(mktemp -d "$AW_ROOT/artifacts/gear-visual.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 6000 \
  --log-file "$gear_output/smoke.log" -- \
  --appearance-smoke --capture-appearance --output="$gear_output"
```

Run after compiling content and building the client. Omit `--capture-appearance` and add `--headless` for assertions without screenshots. `tools/export.sh` includes both diagnostics in the exported application. See the [drag-and-drop verification](gear_drag_verification.md) and [inventory polish verification](inventory_polish_verification.md) for reviewed builds and actual results.
