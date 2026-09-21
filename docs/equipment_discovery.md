# Equipment lore and discovery

All 26 equipment definitions now have individual lore in the shared text catalog. Hover equipment in **I → Gear**, select an item for the scrollable inspection, or inspect it in **C → Craft** to read its history. The ground-loot inspector also shows lore before collection.

Special powers explain their triggers and effects alongside the actual property, engraving, awakening or evolution on the item. The Choir of the Unburied's property is called **Unburied Chorus** throughout the workbench and property library. Ashcleaver explains Burning Stacks, its 1,000 burning-kill awakening requirement, and the Serath and Orrun branches. Comparisons retain their explicit numeric scope: this equipment slot's base values and conventional affixes; the descriptions do not add hypothetical damage to the displayed deltas.

Evolved-item comparisons use wider columns to fit both power descriptions at 780×720. Selected-item and crafting details scroll. Permanent-craft confirmation shows the selected item's identity, irreversible outcome, power and exact projected material/catalyst costs; the full lore and stat comparison remain in the workbench.

A new visible **Legendary** ground drop produces an ascending metallic chime and a brief rise of golden sparks. **Godwrought** ground drops have a deeper bell and a short ember crown. Both use the existing bounded effects and audio pools, Effects volume, pause behavior and reduced-effects setting. Reduced effects suppresses the new visuals while retaining the sound and item label. Combat warning voices remain reserved for warnings.

Discovery is driven by a matching `LootDropped` event and a currently live item. Repeated events do not replay the cue, restored ground loot starts silently, encounter replacement clears presentation, and receipts are bounded by live drops. Hidden drops do not announce, and changing the filter does not replay discovery. Pickup retains its separate sound.

The current ordinary drop table produces Legendary rings but excludes Ashcleaver. The Godwrought ground-drop presentation is supported and verified with detached visual fixtures; this change does not make Godwrought items randomly drop. Inventory grants do not play ground-drop cues.

## Content and compatibility

`content/text.en.json` owns `lore.<item suffix>` and `power.<property/evolution/awakening id>.name` / `.description` entries. Shared presentation helpers read these alongside the equipment names once at launch. Ground inspection reads innate property IDs from the packaged progression catalog once, without hardcoded item exceptions. Authoring and production validation require lore for every equipment definition and text for every authored property and awakening state. The standalone sandbox compiler requires equipment names, lore and property text, and ships the progression catalog used by ground inspection.

No Core rules, item identifiers, gameplay catalogs, random streams, save formats or replay formats change. Existing equipment gains the descriptions when opened in the updated client.

## Verification

The Appearance diagnostic covers lore completeness, power descriptions, native inventory behavior, compact comparisons, a detached evolution-confirmation layout, and unchanged saved state. Crafting checks real earned items, native drag/drop, five authoritative service commits, permanent confirmation, exact costs and save/replay. HUD retains its established campaign regression seed and checks quiet refresh/load baselines. CombatFeedback covers both cue waveforms, effect lifetime/pooling, reduced effects, duplicate receipts and detached presentation galleries. Its separate fresh Vanguard at seed 20 fights the authored road encounter using ordinary commands, earns a Legendary through the unchanged drop table, and verifies delivery, duplicate suppression, silent restored loot and exact combat replay.

Source and package evidence, screenshots and Prism review dispositions are retained under `artifacts/loot-discovery/`. The evolution-confirmation and Godwrought-drop galleries are presentation fixtures, not successful graft or earned Godwrought ground-drop playtests.

The solution builds with zero warnings and errors; formatting, whitespace, authoring validation and both content compilers pass. Rendered checks passed for Appearance (560), CombatFeedback (196), Crafting (91) and HUD (108), with compact comparison and confirmation captures inspected. The final macOS package passes Appearance (532), CombatFeedback (191), Crafting (75) and HUD (90): **888 checks**, including the final catalog-map assertion, startup loading, native inventory transactions and save/replay. Export and runtime logs pass the Godot log checker. Final package evidence is `artifacts/loot-discovery/package-final.48vI7n/`.

Prism/Gemini reviewed the implementation three times with default redaction. Property lookup, cache allocation, repeated labels and startup loading findings were addressed. Remaining candidates were checked against validated-state contracts, existing naming validation and overlay lifecycle; no actionable findings remain. Individual dispositions are recorded in `artifacts/loot-discovery/prism-disposition.md`.
