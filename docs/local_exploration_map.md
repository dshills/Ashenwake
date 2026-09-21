# Local exploration map

Solo campaign, Fracture, and God Hunt rooms now have a compact minimap. Press **M** or click its header for the expanded room map. **M**, **Escape**, or Close returns to play. The Journey map remains on **J** for travel, story decisions, and services.

The player marker stays visible. Nearby terrain reveals within 3.6 world units, with walls blocking sight; only discovered wall fragments and markers are drawn. Symbols identify available exits, services, treasure, remaining ground loot, and the current movement destination. Loot markers respect the existing rarity and compatibility filters, including holding Alt to show all discovered drops while the expanded map is paused. Markers update when an interaction disappears or loot is collected.

Click explored, walkable ground on either map to use ordinary mouse movement. Existing obstacle routing, actor avoidance, and cancellation controls apply. Clicking a symbol requests movement to its ground position; interaction and item collection still use the world controls. The expanded map pauses solo play. Closing it removes its own pause only; manual pause and focus-loss recovery still require Resume. Save, Load, and replay verification work from the expanded map and close it first. Scene changes cancel old destinations and close the expanded map.

Exploration belongs to the character save. Campaign and hub rooms retain their discoveries when revisited. Generated expedition discovery is scoped to a run and room, retained through retries, and discarded when the run is left. Echoes saves carry the same maps through their endgame runtime. The independent Sandbox and co-op scenes do not expose this solo map.

## Persistence and implementation

`LocalMapView` exposes an immutable projection with bounded grid dimensions and sorted discovered cell IDs. Shipped rooms use 500-unit cells; unusually large custom layouts increase cell size to remain within 96 cells per axis. The atlas accepts at most 96 room identities and validates its schema, rules version, geometry hash, dimensions, and cell bounds.

Maps start through an explicit recorded `EnableExplorationMap` command when the player starts or loads a character in the interactive client. Old archives lacking the optional field remain byte-shape and hash compatible until that command is issued. Reading saves, front-menu previews, and replay verification do not silently initialize discovery. Future nested map schemas are rejected before backup fallback or overwrite. Existing supported catalog migration retains unchanged geometry and resets only changed room layouts.

Map clicks remain client input intent. The authoritative map projection, discovered destination, and combat collision radius are checked before the shared click planner receives the point. The planner emits ordinary combat commands; it introduces no alternate movement or travel rules. Rendering caches discovered terrain separately from changing player and marker symbols.

## Verification

`LocalExplorationMapTests` covers old archive compatibility, deterministic reveal, wall occlusion, save/replay persistence, rejection and rollback, generated run isolation, malformed data, and catalog migration. The native `--local-map-smoke --discipline=Vanguard --output=<fresh-directory>` route uses the shipping director and actual viewport input. Add `--capture-local-map` for screenshots. It checks map controls, independent pauses, real movement and combat, save/load, older fieldless saves, marker visibility, and scene transitions.

Both `tools/export.sh` and `tools/release-verify.sh` include this diagnostic. Milestone results and Prism review are recorded below.

## Prism review disposition

Prism reviewed the staged implementation with Gemini (`gemini-3-flash-preview`); the raw report is `artifacts/local-map/prism.json`. Each finding was checked against the actual execution path:

- **Room capacity:** campaign combat catalogs allow at most 64 encounters, plus the hub and five cleared-act identities: 70 maps fit below the 96-room limit. Generated atlases contain only the current run's three or four rooms. The capacity guard stays; evicting discovered rooms would discard saved progress unnecessarily.
- **SpatialWorld construction:** its constructor stores the room reference. It builds no index or occupancy structure, contrary to the review's assumption.
- **Terrain rendering:** Godot retains the terrain draw commands until discovery, room, or layout changes. Player movement redraws the separate symbol layer; the review's assertion that full terrain is rebuilt every frame is incorrect. Grid and obstacle counts remain bounded.
- **Marker allocations:** current interaction and loot sets are projected to capture movement, discovery, filter changes, collection, and mechanism availability. Caching solely on room or collected loot, as suggested, would leave other changes stale. Further allocation tuning needs profiling evidence.
- **Marker types:** current exit, return, revisit, treasure, and service IDs are covered. The fallback is Service, not Loot as reported. Explicit content marker metadata can accompany new interaction types when required.
- **Cell storage:** even 70 fully explored maximum-size custom grids occupy approximately 3.15 MB of JSON cell data, within the 64 MiB archive bound; shipped room grids are smaller. Compression is optional optimization rather than a correctness fix.
- **Colors:** the map follows the existing programmatic HUD styling. A shared theme migration is outside this change.
- **Grid edges:** the 500-unit fog resolution is deliberate. Visible wall-face cells receive dedicated sampling; clicking still checks combat collision and discovered ground.

Independent integration review fixed a combat-radius mismatch, keyboard focus escaping behind the map, H binding a memory during a map-to-Echoes transfer, and modal transfers releasing the destination menu's focus. Native checks cover the resulting controls and pause behavior.

A second Prism pass (`artifacts/local-map/prism-final.json`) reported no high-severity findings. Additional suggestions were checked: encounter IDs must begin with `campaign.` or `exploration.`, so they cannot collide with `hub` or `clear.act.*`; every validated combat snapshot contains exactly one player actor; changed catalog geometry requires the existing explicit migration path; and discovered cells are already sorted only when discovery changes. No silent fog eviction or unversioned geometry reset was introduced.

Packaged character-menu regression testing additionally caught an initialization-order issue. New characters and campaign imports now enable exploration before their first durable save, so the selected slot already matches the character entering play. Older existing saves still initialize only through the explicit load-time command. The settings smoke compares the complete before/after replay instead of assuming that initialization creates zero frames.

The final Prism pass (`artifacts/local-map/prism-verified.json`) retained two suggestions: terrain redraw optimization for very large custom rooms, and guarding a missing player actor. The renderer already separates cached terrain and checks obstacle intersections; Core validates a unique player before any view is published. No actionable correctness defect remained.

The broader HUD regression also exposed an existing test isolation issue: opening and historical-import routes shared a profile file, legitimately merging `discovery.widow_crypt`. The diagnostic now gives each route its own save/profile directory and retains exact hash equality; production profile merging is unchanged.

The completion pass (`artifacts/local-map/prism-complete.json`) repeated the capacity warning and suggested reducing marker allocations. The capacity proof above still applies. Discovery comparisons already short-circuit on the immutable view reference, so the claimed full-cell comparison every frame is incorrect. Small marker-list allocations remain a profiling-driven optimization candidate; the review supplied no measured frame regression.

## Completed validation

- Solution build: no warnings or errors; repository formatting and shell syntax checks passed.
- Core: **580 tests passed**, including **22 exploration-map cases**.
- Packaged native application: **1,119 checks passed** across local map (74), mouse actions (186), settings (140), character menu (119), journey (347), Echoes screen (163), and combat HUD (90).
- Packaged headless map route: **65 checks passed**, including paused Alt filtering.
- Local map evidence includes **nine rendered screenshots** at 1280×800 and 780×720, and **five replay branches**. Screenshots were inspected for map placement, fog, controls, and the responsive combat dock.
- Export and final selected Godot run logs passed `tools/check-godot-log.py`.

Final native report selection: `artifacts/local-map/package.274w844i/summary.json`. Final headless evidence: `artifacts/local-map/headless-final.xGEPnX/local-map-review.json`. Full tests and formatting: `artifacts/local-map/tests-full.log` and `artifacts/local-map/format-verified.log`. Updated playable app: `artifacts/export/macos/Ashenwake.app`.
