# Loot management

Press **I** and select an equipped item or backpack card. **Favorite item** and **Lock item** are independent, persistent choices. Either protects that item from discard, extraction and salvage; remove both protections before destroying it. Equipment changes, tempering, engraving and other crafting that keeps the item remain available. Protection can be changed wherever the character is alive, including campaign and endgame encounters, without changing combat state.

Backpack cards show a star for a favorite, **L** for a lock, and a diamond for saved-outfit use. Select the card for full status and outfit names. The usage filter offers **All gear**, **Favorites**, **Locked**, **Unused**, and **Saved outfits**, combined with the existing search, type, rarity and sorting controls. Unused means an unequipped item absent from all saved outfits; it is not a recommendation to destroy it. Clear resets these browser filters without changing saved ground-loot preferences.

## Salvage at Torren

After rescuing Torren, visit his Greyhaven workshop, select an unwanted unequipped item and choose **Salvage selected item…**. The preview and confirmation show the exact material return. Keep item cancels without spending or changing anything. Confirming permanently removes the selected item and adds the displayed materials in one transaction.

| Rarity | Materials |
|---|---:|
| Common | 1 |
| Tempered | 2 |
| Rare | 4 |
| Relic | 6 |
| Legendary | 10 |

Equipped, favorited, locked and Godwrought items cannot be salvaged. Affixes, engraving and past crafting expenditure do not increase the return. Salvage does not learn legendary properties, refund catalysts or award XP, mastery or loot. If the complete return would exceed the material limit, the operation is blocked without losing the item. This is a single-item action; there is no bulk or automatic salvage.

## Saved outfits and permanent changes

Gear details and crafting inspection list every saved outfit using the selected item. Discard, salvage and extraction confirmations name the outfits affected by permanent removal. Outfit membership itself is not a lock: a confirmed removal leaves the historical reference in place, and applying that outfit then explains the missing item. Re-save the outfit with replacement gear to repair it. Upgrades to retained items remain attached to the original item instance.

Salvage confirmations default to **Keep item**. Changing the selection, character, transaction revision, active combat session or service eligibility invalidates the pending operation. Closing or hiding Gear, switching tabs, or losing application focus cancels it. Confirmation pauses combat using its own pause owner and releases only that owner.

## Persistence and authority

`PermanentItem.IsFavorite` and `IsLocked` are booleans omitted from serialized output when false, preserving historical unused-state hashes. New production command values are appended to the existing enum. Strict command values, operation receipts, atomic rollback, save validation and replay checks apply to these actions. Flags belong to the owned item and survive ordinary combat inventory projection and reconciliation.

The engine-free Core calculates salvage eligibility and yield. The client displays that result and requests the authoritative transaction; it cannot grant materials or bypass protection. Campaign and endgame wrappers permit organization metadata changes without stepping or rebuilding combat, while salvage retains the living-character, Greyhaven, Torren proximity and rescue requirements.

See [verification](loot_management_verification.md) for automated and native evidence and its limits.
