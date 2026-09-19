# Equipment drag and drop

Press **I** to open Gear directly. The left grid arranges all twelve equipment slots around the body; the right grid contains owned, unequipped items. At wider window sizes, the rotating character preview appears beside them. Click a card to inspect it and compare stats before committing. **C → Gear**, the slot/item selectors and the explicit Equip/Unequip buttons remain available.

Stand near **Torren in Greyhaven** to change gear:

- Drag an inventory card onto a compatible equipment slot to equip it. Valid targets highlight while dragging.
- Drop onto an occupied slot to replace its item. The previous item returns to inventory.
- Drag equipped gear onto the inventory header, an inventory card, or the open inventory background to unequip it.
- Scroll the inventory to reach additional items. There is no backpack capacity limit or manual item-position saving in this interface.

Slot, discipline and two-handed weapon rules still apply. Remove your off-hand item before equipping a two-handed weapon; remove or replace a two-handed weapon before equipping an off-hand item. You can inspect gear away from Torren, but cannot change it there.

Dropping outside a valid target, onto an incompatible slot, or back onto the same slot changes nothing. **Escape** cancels an active drag. Opening another menu, closing the equipment panel or losing application focus also cancels it. The world pauses during an equipment drag, and finishing the drag preserves any pre-existing pause. Dropping an item outside the panel never discards it.

Successful drops use the existing equipment transactions, so stats, world appearance, preview, saves and replay all reflect the same committed item. Weapons, off hands, helmets and chest armor have visible model attachments; the other eight slots retain their existing stat effects. If ownership or the current loadout changes during a drag, that old drag is rejected.

This interface is available in the solo Endgame, Campaign and Production scenes. The cooperative equipment interface is unchanged. The layout takes inspiration from Diablo's equipment-and-backpack arrangement and uses Ashenwake's existing Godot controls and styling.

## Reproduce verification

The appearance diagnostic uses native viewport press, motion and release events to exercise actual Godot drag/drop. It checks equipment removal and replacement, ownership conservation, invalid and canceled drops, stale payloads, service gating, two-handed rules, appearance, save and replay. The mouse-actions diagnostic checks the shipping director's **I** routing.

```bash
source tools/env.sh
gear_output=$(mktemp -d "$AW_ROOT/artifacts/gear-visual.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 6000 \
  --log-file "$gear_output/smoke.log" -- \
  --appearance-smoke --capture-appearance --output="$gear_output"
```

Run after compiling content and building the client. Omit `--capture-appearance` and add `--headless` for assertions without screenshots. `tools/export.sh` includes both diagnostics in the exported application. The [verification record](gear_drag_verification.md) records the reviewed build and actual results.
