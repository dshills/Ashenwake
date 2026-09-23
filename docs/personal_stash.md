# Personal stash

The personal stash stores equipment for the current character in Greyhaven. Rescue Torren to unlock the chest beside his workshop. Storage is separate from the shared profile: another character has its own tabs and items.

## Opening and access

Click the **Personal stash** chest, or approach it and press **F**. You can also choose **Open personal stash** in the Gear panel. Saved outfits and complete builds offer an **Open stash** button when their equipment is stored.

You can inspect your storage while exploring. Depositing, retrieving, moving equipment between tabs, and renaming tabs require a living character at the Greyhaven chest. Finish or abandon an active journey, resolve any outstanding regional hunt, and end a Borrowed Memory experiment before making changes. In Greyhaven, **Approach stash chest** closes the panel and walks the character to the chest using the normal interaction navigation.

The panel pauses gameplay while open. Closing it releases that pause without clearing an existing manual pause. **Esc** first cancels an active drag or leaves a text field; otherwise it closes the panel.

## Organizing equipment

There are four tabs, initially named **Weapons**, **Armor**, **Relics**, and **Keepsakes**. Each holds 128 items, for 512 stored items in total. Tab names are organizational labels: any tab can hold any kind of equipment. Select a tab, enter a name, and choose **Rename tab**. Names must be unique and contain 1–32 visible characters.

The left pane shows carried equipment; the right pane shows the selected tab. Select an item to inspect its stats, affixes, equipment details, favorite/lock markers, and saved outfit or build references.

| Action | Control |
|---|---|
| Deposit | Drag an unequipped item from the backpack onto the storage pane or a tab button. Alternatively, select it and use **Deposit selected item**. |
| Retrieve | Drag a stored item onto the backpack pane, or select it and use **Retrieve selected item**. |
| Move between tabs | Drag a stored item onto another tab button. |
| Find equipment | Search by equipment name or saved-reference text; combine type, rarity, and favorite/locked/saved-build filters. **Clear** resets the filters. |

Equipped items must be unequipped before depositing. Favorite and locked items can be stored without changing their markers. A full destination tab refuses a transfer. Retrieving also requires space within the 512-item carried inventory limit. A refused transfer leaves the item in its existing location.

## Builds, crafting, and ownership

Transfers have no currency cost. They move an existing item between carried and stored locations while retaining its identity, rolled stats, affixes, engraving, evolution progress, favorite/lock state, and saved equipment references.

Stored equipment remains owned and counts toward collection ownership. It does not appear in backpack equipment choices or crafting selections, and cannot be equipped, crafted, extracted, salvaged, or discarded until retrieved. Saved outfits and complete builds identify equipment as stored in its named tab and explain that it must be retrieved before applying the build.

## Saves and validation

Tab names and item locations are saved with the character. Existing characters start with empty storage; merely inspecting the default tabs does not create stash state or grant items. Combat and cached campaign-room inventories are projected from carried equipment, so returning to an earlier room does not restore a second copy of stored equipment.

Loading or switching characters closes the panel and invalidates its previous selections and drag payloads. Transfers are rechecked by permanent progression authority, including ownership, equipped state, destination capacity, and chest access. Invalid or duplicate location entries are rejected during restoration.

## Verification status

Build, transfer, persistence, native mouse/layout checks and Prism review are recorded in [personal stash verification](personal_stash_verification.md), including review dispositions and the limits of scripted testing.
