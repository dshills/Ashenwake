# Equipment names

Every authored equipment definition has a display name in `content/text.en.json`. Inventory, comparisons, crafting, discard confirmation, ground-loot inspection, pickup notices and co-op rewards share this catalog. Search and name sorting use the names shown on screen.

The stable item IDs and serialized combat names remain unchanged so existing characters and replays retain their identities. Names are presentation text; changing one does not change item stats, rarity or ownership. The generic starting weapon and off-hand names suit their discipline-specific appearances.

| Item definition | Display name |
| --- | --- |
| `item.ash_axe` | Gravefire Axe |
| `item.march_plate` | Lastwatch Cuirass |
| `item.ember_lens` | Eye of the Fallen Sun |
| `item.ashcleaver` | Ashcleaver |
| `item.cinder_edge` | Cinderwake Saber |
| `item.oath_hammer` | Oathbreaker's Gavel |
| `item.pilgrim_pike` | Pilgrim's Last Reach |
| `item.ash_weave` | Ashwound Vestments |
| `item.oath_plate` | Vowkeeper's Carapace |
| `item.serath_shroud` | Serath's Funeral Shroud |
| `item.stone_seal` | Orrun's Oathseal |
| `item.war_token` | Red March Reliquary |
| `item.starter_head` | Bellwatch Helm |
| `item.starter_shoulders` | Wakeguard Mantle |
| `item.starter_chest` | Wayfarer's Hauberk |
| `item.starter_gloves` | Gravesoil Grips |
| `item.starter_belt` | Last-Rite Girdle |
| `item.starter_legs` | Mourner's Greaves |
| `item.starter_boots` | Cindertrail Boots |
| `item.starter_amulet` | Lanternkeeper's Vow |
| `item.starter_ring1` | Gravesalt Signet |
| `item.starter_ring2` | Widow's Promise |
| `item.starter_mainhand` | Last Light |
| `item.starter_offhand` | Pilgrim's Vigil |
| `item.echo_ring` | Choir of the Unburied |
| `item.greatstaff` | Hollowstar Greatstaff |

`aw authoring validate`, `aw sandbox compile` and `aw production compile` require name and lore catalog entries for every authored item. Both compilers include the catalog in the client. When adding equipment, add its `equipment.<id suffix>` and `lore.<id suffix>` text entries too. See [equipment lore and discovery](equipment_discovery.md) for power descriptions and discovery feedback.

Verification: solution build and formatting pass; all 26 names are present and unique. Rendered Appearance, Crafting and HUD diagnostics pass 407, 90 and 104 checks respectively, with compact layouts and confirmation text visually inspected. The refreshed macOS package passes the same three headless suites (385, 74 and 86 checks), including save/replay. Prism/Gemini reviewed the staged changes twice; individual candidate dispositions are retained under `artifacts/equipment-names/`. Gameplay content and maintained archive fixtures are unchanged.
