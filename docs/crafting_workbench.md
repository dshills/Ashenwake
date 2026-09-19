# Visual crafting workbench

Open **C → Craft**, or interact with a crafting specialist in Greyhaven. The six service cards show which workshops are available and who provides them. Selecting a locked service explains its prerequisite. Inspection is available away from the specialist; applying a craft requires the existing location, ownership, materials and recipe rules.

The equipment tray includes both equipped and unequipped items. It uses the Gear screen's item icons and rarity colors, with search, type and rarity filters, and stable type/rarity/name sorting. Click an item or drag it into the workbench to inspect it. Selecting, dragging and filtering never equip, destroy or modify an item. Escape cancels a drag, and closing the screen or losing focus cancels pending interaction.

Choose a service and its options:

- **Tempering:** strengthen an existing affix, or temper Godwrought damage within its existing five-step limit. Endgame Godwrought tempering can explicitly use an owned catalyst in place of common materials.
- **Rebinding:** replace an existing affix with a compatible alternative at its initial strength.
- **Engraving:** put a learned property into an empty engraving slot.
- **Extraction:** permanently destroy a Legendary item and learn its exceptional property. The preview also identifies any equipment slot that will become empty.
- **Divine Grafting:** permanently choose the Serath or Orrun evolution for an awakened Ashcleaver, with its required fragment and, under the endgame policy, lineage catalyst.
- **Purification:** select an owned, unpurified divine fragment. Its ownership is preserved.

The preview shows current and resulting item totals, changed affixes, engraving, evolution or other permanent effects. Damage and armor include retained base rolls and affixes. Critical chance uses percentages and percentage-point changes. Material and catalyst balances show the exact projected deduction and remaining amount. A blocked recipe shows current values and the rejection reason, with no invented result.

**Apply improvement** sends the existing authoritative transaction. Extraction and grafting instead open a confirmation describing the permanent outcome. Cancel keeps the item and materials unchanged. Changing selection, service, authoritative state or session invalidates the pending confirmation. Completion feedback appears only after the actual transaction succeeds.

The workbench pauses the world while visible and releases only its own pause when closed. Manual pause and focus-loss pause remain in effect. Loading a save restores the workbench's pause and discards stale confirmation or drag state.

## Implementation and verification

`ProgressionSession.PreviewCraft` restores an isolated copy, invokes the existing crafting transaction there and returns detached before/after snapshots. It does not duplicate crafting rules or adopt the result into live state. The live request still requires explicit permanent confirmation. The preview receipt is isolated and cannot collide with an existing receipt. Core schemas, crafting balance, save formats and reward generation remain unchanged.

The standalone diagnostic runs with `--crafting-smoke --output=<fresh-directory>` and optionally `--capture-crafting`. It earns items through the existing dungeon and specialist routes, exercises the shipping HUD, compares preview and actual outcomes, and verifies saved results and replay. `tools/export.sh` includes this diagnostic. Exact runs, review dispositions and test boundaries are recorded in [crafting workbench verification](crafting_workbench_verification.md).
