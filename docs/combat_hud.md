# Combat HUD and rewards

The solo campaign, Fractures and God Hunts use a bottom combat dock that fits 1280×800, 1000×720 and 780×720 presentation sizes. Health, barrier and the discipline's actual resource appear above the six existing ability buttons. The right side shows potion charges and cooldown, and dodge readiness. Potion and dodge labels follow their configured bindings. The ability buttons retain their existing cooldown, resource, locking and input-buffer behavior.

The XP strip shows experience earned toward the next level, using the authoritative current and next level thresholds. At the level cap it shows MAX LEVEL. The smaller-window layout retains Inventory, Settings and Pause buttons; hover the dock background for the full default mouse and keyboard control reminder. Journey, Character, Fractures and Echoes share a right-hand navigation column.

Active player conditions appear above the dock. Each timed status has a distinct glyph, readable name, duration and stack count. Durations come from remaining simulation ticks at 30 ticks per second; the final fraction of a second rounds upward. Barrier displays its actual amount. Guarded and Shielded show Active because their view does not provide a duration. An overflow chip exposes additional conditions in its tooltip.

The upper-right reward feed acknowledges collected equipment with its existing item silhouette, name and rarity. Levels, mastery milestones and newly available abilities use progression and skill views. A mastery milestone identifies the skill's mutation unlocks; open **C → Skills** to inspect the build, and visit Mara to apply eligible changes.

Up to three notices appear at once, with eight pending. Matching equipment pickups coalesce; build milestones take priority when the queue fills. Notices normally remain visible for five seconds, and their lifetimes pause with gameplay. New characters and restored saves establish a quiet baseline. Reopening a screen or refreshing the character does not earn or repeat rewards. Notifications do not open menus or change simulation state.

## Reproduce the HUD diagnostic

```bash
source tools/env.sh
dotnet build game/Ashenwake.Client/Ashenwake.Client.csproj --no-restore
hud_output="$(mktemp -d "$AW_ROOT/artifacts/hud-check.XXXXXX")"
"$GODOT" --path game/Ashenwake.Client --quit-after 18000 \
  --log-file "$hud_output/smoke.log" -- --hud-smoke --capture-hud --output="$hud_output"
python3 tools/check-godot-log.py "$hud_output/smoke.log"
rg -q 'HudClientSmokePassed' "$hud_output/smoke.log"
```

The route earns opening-campaign rewards from a fresh character, then uses the existing immutable campaign-complete fixture for real Fracture combat and mastery. It writes exact replay segments, save/load checks and rendered screenshots. Add `--headless` before `--` to run without rendered captures. The package export also runs this route and the shipping-scene navigation diagnostic.
