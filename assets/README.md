# Asset conventions for the architecture spike

Phase 0 uses generated Godot primitives, so it requires no downloaded art. Geometry is authored in `content/phase0.json` and rendered directly by the client; it is also the authoritative Core collision input.

Future editable source files belong under `assets/source/`, with exported assets under `assets/exported/` and only runtime assets imported by the client. Godot's `.godot/` directory is generated and ignored. Large binary formats use the repository's Git LFS rules.

Use meters in presentation and integer millimeters in Core, Y-up, with gameplay on the X/Z plane. Place actor origins at their feet. The current collision footprint is a conservative 700 mm square around each actor for world obstacles; actor separation uses a 350 mm radius. Skeleton, LOD, material, and production naming standards remain Phase 2 work.
