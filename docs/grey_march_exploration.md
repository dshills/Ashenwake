# Grey March exploration

Act I now has authored collision layouts: the road bends around a ruined bank, the monastery leads through passages into its courtyard, and four pillars frame the Bell Saint sanctuary. The paved routes and visible obstacles use the same room definitions as combat and mouse navigation. Enemies navigate around these walls deterministically.

After securing the road, follow the northern branch and click **WIDOW'S CRYPT · OPTIONAL**. Clicking approaches the passage; **F** also works within reach. Inside, defeat the Gravewake guard and its companions, then approach the **WIDOW'S TESTAMENT** reliquary. Claiming it awards one Rare Serath Shroud, 25 materials and a discovery in **J → Journal → Discoveries**. The elite also has its normal Rare-or-better combat drop. The western passage returns to the road; leaving before claiming the testament keeps the discovery available.

Cleared opening rooms retain remaining floor loot, corpses and room identity. Return and revisit markers connect the road, monastery and sanctuary. Revisiting does not recreate enemies or award encounter rewards again. The crypt remains accessible after its testament has been claimed. Partial fights restart when abandoned; floor-loot retention applies only to secured rooms. Other regions keep their existing travel behavior.

The Journey map and objective card distinguish exploration, reward collection, returning through a passage, and advancing the campaign. The monastery decision still gates the Bell Saint. Crypt treasure and passages require a living player within interaction range. Permanent equipment ownership remains in the production character; returning to a cached room applies the current equipment and build rather than restoring an old inventory.

## Persistence

Campaign runtime saves include a bounded map of inactive cleared opening rooms. Entering a retained room removes its entry until the next departure. Item IDs are reserved globally, cached floor loot cannot duplicate owned items or another room's drops, and the testament has a bound permanent reward receipt. Loot collection, the crypt discovery and the testament remain consistent across save/load and replay.

The opening catalogs are `campaign.grey_march.2` and `campaign-combat.grey_march.2`. Save readers support the exact preceding catalogs using frozen, embedded fixtures. They authenticate the original checksum and validate the complete old state before rebinding identities. Only positions obstructed by a new layout move to a deterministic legal location; valid positions, ownership, rewards, random state and combat timers are preserved. Campaign, Endgame, Echoes and shared profiles use this path. Unsupported versions remain compatibility failures and are preserved. Historical replays remain bound to their original catalogs.

## Verification

Focused Core coverage exercises authored room geometry, navigation, encounters, migration and exploration persistence. The shipping mouse-actions smoke includes the crypt excursion, scene geometry checks, reward claim, return with retained loot, revisit and replay. Run it with a fresh artifact directory:

```bash
source tools/env.sh
"$GODOT" --path game/Ashenwake.Client -- \
  --mouse-actions-smoke --discipline=Vanguard --capture-mouse-actions \
  --output=/absolute/fresh/directory
```

Validation on macOS Apple Silicon / Godot 4.6.2 Mono / .NET 8: the solution builds without warnings or errors; all **558 Core tests** pass; `dotnet format --verify-no-changes` passes. The exported native application passed **964 assertions**: mouse actions and crypt 202, Journey 347, mouse movement 83, settings 162, and visual presentation 170. Every native report passed and its Godot log passed the strict checker. Screenshots were inspected for the crypt and road return.

The final gameplay package evidence is `artifacts/grey-march-exploration/package.anoirnyk/`; the complete test and formatting logs are `tests-final.log` and `format-verified.log` in the same milestone artifact directory. The refreshed application is `artifacts/export/macos/Ashenwake.app`. Automated tests establish correctness and bounded scene geometry; they do not replace a human combat-feel playtest.

Prism/Gemini reviewed the staged milestone twice (`prism.json`, `prism-final.json`). The temporary-state caching concern was fixed by clearing statuses, pending actions, buffered input and prepared legendary effects. A separate code review found that crypt death needed explicit anchor normalization; recovery now restores health, potions and cooldown readiness without discarding road loot.

The second Prism review reported no high-severity findings or unresolved correctness defects. Its remaining performance suggestion concerns checking each paving tile against room obstacles during scene construction. The shipped opening rooms are 24 × 20 world units with at most four obstacles, and ground construction occurs on room changes; a spatial index is deferred until larger authored layouts justify it. The adjacency metadata suggestion is a future authoring improvement for the fixed four-room opening route. Its final navigation item was positive feedback about deterministic tie-breaking, not a requested fix.
