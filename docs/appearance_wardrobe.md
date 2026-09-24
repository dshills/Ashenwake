# Appearance wardrobe

Open **I → Appearance wardrobe** in the Gear screen to dress your character using collected armor appearances. Seven slots are supported: head, chest, shoulders, gloves, belt, legs and boots. Weapons retain their equipped appearance and animation identity.

Armor currently owned in the backpack or personal stash unlocks its appearance. Future pickups unlock appearances as they are acquired. The wardrobe remembers the highest rarity observed for each armor definition, including crafted rarity upgrades; selling, extracting or salvaging an item does not remove its previously observed look. Items discarded before the wardrobe existed cannot be reconstructed without ownership evidence.

Select a slot and choose an unlocked appearance, or **Equipped appearance** to follow its actual item. The full character preview shows the draft. **Hide helmet** removes the visible helmet while preserving the equipped head item's armor and bonuses. A chosen override is remembered while its slot is empty and becomes visible when armor is equipped there again.

Choose **Apply look** to save the draft and show it on your character. **Cancel / Close** discards unapplied changes. **Reset appearance** stages a return to actual equipped armor and a visible helmet; apply that reset to commit it. Neither reset nor cancellation removes collected appearances.

## Named looks

Up to eight named looks store the seven appearance choices and helmet visibility. Names have a maximum of 32 characters. Saving an existing name replaces that look in the draft. The saved-look selector previews a look without changing the current draft; **Use look** loads it into the draft. Saving, replacing or deleting named looks is committed only by **Apply look**. Closing first cancels those edits too.

Wardrobe looks are separate from equipment presets and build loadouts. A Last Vigil helmet can visually use another collected helmet while its real item continues contributing to Last Vigil. The inventory's names, stats, comparisons and set counts always describe equipped items. Item comparison previews still show the actual candidate item; wardrobe previews and the normal character/world view show cosmetic choices.

## Persistence and scope

The optional `<character-save>.wardrobe.json` file keeps character-specific unlocks, applied choices and saved looks, with bounded validation, backup recovery and conflict detection. It is bound to the character identity and save filename and can move with the save directory. It does not alter character archive schemas, content hashes, equipment, crafting, resources, combat rules or replay state. Corrupt, foreign, unknown-catalog and newer-schema files are preserved rather than silently overwritten. Protected files allow session-only choices with an explicit notice.

A successful Apply is written immediately. A concurrent change that conflicts with the draft is rejected without changing the active appearance; closing and reopening loads the current saved choices while retaining appearances observed in this session. Collecting gear adds unlocks independently from applying a draft.

Wardrobe appearances work in solo campaign, optional encounters, Fractures, God Hunts, training and the local Echoes character flow. Each character keeps its own choices. The optional networked co-op prototype retains its existing equipment presentation.

See [verification and Prism review](appearance_wardrobe_verification.md) for validation evidence.
