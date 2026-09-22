# Campaign pacing

The pacing pass moves some late campaign XP into earlier encounters, gives optional testaments useful craftable affixes, and adds a direct walk action from the Fracture board to its gate.

## Levels and rewards

The main campaign still grants **5,150 XP**. Level thresholds, the level cap, passive costs, and the level-10 ultimate unlock remain the same. The revised schedule removes Identity Memory's two-level jump and rewards the first two act finales with a level.

| Encounter | XP | Cumulative XP | Level after victory |
| --- | ---: | ---: | ---: |
| Road | 50 | 50 | 1 |
| Monastery | 100 | 150 | 2 |
| Bell Saint | 175 | 325 | 3 |
| Living Ruins | 175 | 500 | 3 |
| Plague Village | 200 | 700 | 4 |
| Rootheart | 300 | 1,000 | 5 |
| Cinder Pack | 300 | 1,300 | 5 |
| Extraction Floor | 325 | 1,625 | 6 |
| Furnace Spindle | 400 | 2,025 | 6 |
| Bone Causeway | 400 | 2,425 | 7 |
| Contract Hall | 425 | 2,850 | 8 |
| Covenant Warden | 500 | 3,350 | 8 |
| Repeating Rooms | 550 | 3,900 | 9 |
| Identity Memory | 650 | 4,550 | 10 |
| Breach Heart | 600 | 5,150 | 10 |

These are fresh-character milestones. Imported characters and characters with other earned XP may reach levels earlier. Optional exploration continues to provide equipment, discoveries and materials. Main encounters provide 230 materials and all optional branches another 220, in addition to the starting 25. This pass does not change crafting prices.

The Widow's Crypt, Briarheart Shrine, Sealed Foundry, Oathkeeper Archive and Unremembered Vault now grant equipment with authored affixes that can be Tempered. Kesh retains his introductory legendary ring for Extraction. The vault's later Choir of the Unburied has stronger critical and resource affixes, so it improves on that gift; its summon power still does not stack with another copy.

| Testament reward | Added affixes |
| --- | --- |
| Serath's Funeral Shroud | 150 armor, 3 resource |
| Orrun's Oathseal | 500 critical, 5 resource |
| Cinderwake Saber | 8 damage, 6 resource |
| Vowkeeper's Carapace | 350 armor, 8 resource |
| Choir of the Unburied from the vault | 1,200 critical, 12 resource |

Armor and critical values use the existing basis-point units. These fixed rolls leave room for Tempering. Rebinding still requires a different eligible affix that is not already present.

Previously earned items retain their stats and crafting history. Save upgrades authenticate the published catalogs and reward receipts before adjusting campaign XP. Partially completed campaigns receive their earlier XP difference once; existing experience is never reduced. Completed campaigns keep the same total. Upgrades preserve choices, inventory, pending combat, RNG, retained rooms and exploration maps.

## Entering Fractures

After the ending, the expedition board opens in Greyhaven. **Walk to the Fracture gate** closes the board and uses normal mouse navigation to approach the gate. Arrival reopens the board, where the player can recover or prepare a Sigil. **X** cancels the walk. Walking does not claim a reward, spend a Sigil, or start an expedition.

## Reproducible measurements

```bash
source tools/env.sh
aw balance campaign artifacts/balance/new-campaign 1 --managed-build
aw balance campaign artifacts/balance/new-main-route 1 --main-path --managed-build
python3 tools/compare-campaign-balance.py /path/to/before /path/to/after
```

Every measurement requires a new output directory. One to three seeds are supported, beginning at 42, across all five disciplines. The default route includes all 15 main encounters and eight optional encounters. `--main-path` skips optional branches.

`--managed-build` spends earned passive points alternating Offense and Defense and equips compatible owned stat upgrades at Torren between acts. It uses the existing public combat policy, starting anatomy and Stone Memory, and never injects XP, equipment, points or victories. It does not craft, select mutations, optimize legendary combinations or spend materials. Without that flag, the tool retains the existing smoke policy's starter equipment and unspent points; its separate policy hash prevents accidental comparisons between the two.

Reports separate combat, travel, pickup ticks and menu commands; include incoming player damage, deaths, attempts, potion use, resource affordability, XP, materials, ability unlocks, equipment grants and duplicate definitions; and save continuous replay segments plus a final save. Every segment is replayed and restored with identical hashes. Simulation time uses 30 ticks per second; wall time includes diagnostic overhead. Arcanist Heat pressure is measured separately from raw zero-resource ticks. A resource-limited tick means at least one available, cooldown-ready skill is unaffordable; it does not mean the character cannot attack.

These runs reveal regressions and progression discontinuities. They do not establish human difficulty, reading time, build diversity, presentation performance or enjoyment. Enemy health and damage need representative human playtests before further tuning. See [verification](campaign_pacing_verification.md) for the measured before/after results and review evidence.
