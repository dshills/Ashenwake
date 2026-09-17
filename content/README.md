# Phase 0 content

`phase0.json` is human-authored source. `phase0.schema.json` describes its JSON structure for editor assistance. The .NET compiler additionally enforces stable IDs, references, nonempty localization values, numeric limits, legal spawn geometry, and the Phase 0 effect vocabulary. `aw content validate` is the authoritative content check.

`aw content compile` writes a compact, immutable `content.bundle.json` into the Godot project. The runtime verifies its schema version and SHA-256 hash before use. Whitespace differences in source do not change the hash; array order does. Never hand-edit the generated bundle. It is ignored by Git and included by the export presets.

One Ember Strike, Ash Ghoul, Ash Iron item, Ember of Vael Arms fragment, Burning status, and loot table prove the architecture. This prototype fragment triggers on direct hits. It is not the later Eye of Vael critical-hit mechanic. All displayed content names have localization keys; the prototype HUD itself is English.

Only direct ability hits can trigger the prototype fragment. Burning does not recursively trigger fragments. Reapplication replaces the existing Burning timer and attribution. Its last scheduled tick at expiration is included. Death is processed once after statuses, then awards one item and 10 experience.
