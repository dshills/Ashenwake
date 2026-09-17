# ADR 0001 — Phase 0 foundation

Status: implemented, September 17, 2026. Scope: the architecture spike only.

## Toolchain and platform

Pin .NET SDK 8.0.425 (runtime 8.0.31), Godot 4.6.2 Mono / Godot.NET.Sdk 4.6.2, and Go 1.27.1. The Godot version matches the installed editor's version while adding C# support through a separate project-local editor. .NET 8 supports this Godot target; upgrading the runtime/toolchain is a deliberate follow-up before long-term production. Bootstrap verifies official release hashes and package lock hashes. No tool is installed system-wide.

The initial interactive reference platform is macOS Apple Silicon, at 1280×800 using the Compatibility renderer. The verified machine is an Apple M4 Pro with 14 logical processors. The macOS template contains a universal engine executable, so the export includes both macOS runtime architectures; only Apple Silicon execution is locally verified. Linux x86_64 is the CI target. Windows, consoles, mobile, controller play, and display-resolution certification are outside this spike.

Use keyboard movement on world X/Z axes, Space/left click for a targeted attack, and E for pickup. The first discipline remains the proposed Vanguard; Phase 0 proves a generic starter strike and fragment rather than implementing Momentum or claiming a complete discipline. Input remains a command boundary so controller mapping can follow without changing combat authority.

## Authority and geometry

Core has no Godot dependency. It owns entity identity, positions, movement acceptance, attack timing/range/line of sight, damage, status schedules, death, rewards, progression, RNG, and serialization. The client maps input to commands and immutable entity views/events to meshes and UI. Mutable internal state is exposed only through deep-copy snapshots.

Use integer millimeters on one flat X/Z plane. Obstacles are shared authored axis-aligned rectangles. Core performs swept movement, deterministic X-then-Z sliding, line queries, and stable-order overlap queries. World collision uses a conservative expanded rectangle; actor separation uses circular radii. No Godot physics result is needed to reproduce outcomes. Room presentation is derived from the exact collision content.

This is deliberately a small query implementation, not a physics engine or navigation system. Higher floors, slopes, navigation meshes, crowd pathfinding, and DCC geometry export are deferred until gameplay requires them. Any later engine query must have a documented reproducible input boundary before it can affect game truth.

## Time and randomness

Run at 30 authoritative ticks per second. The bridge accumulates frame deltas, permits at most five catch-up ticks per rendered frame, and drops excess elapsed wall time rather than skipping simulation ticks. Pause stops simulation time; a paused single step advances exactly one tick. Interpolation is cosmetic. Attack windup and hit resolution use simulation ticks, never animation callback timing.

SplitMix64 supplies separate combat, loot, AI, and encounter streams. Streams and original seed are serialized. AI is intentionally a dormant decision placeholder in this milestone; it consumes only its own stream. Rejection sampling avoids biased weighted loot selection. Loot generation is bounded and awards one item per enemy death.

Determinism is guaranteed only for the same rules version, content hash, initial state, commands, and supported pinned runtime configuration. There is no cross-platform bitwise guarantee. Replays check both authoritative state and semantic-event hashes and identify the first divergent tick. Benchmarks exclude replay hashing from pure Core timings; client diagnostics measure it separately.

## Content, persistence, and tooling

Author JSON with stable IDs and localization keys. Compile into an immutable compact JSON bundle. A typed strict deserializer, required fields, and semantic validation reject malformed/unknown fields, duplicate IDs, invalid ranges/effects, bad geometry, and missing references/localization. The JSON schema supports authoring; semantic validation in Core remains authoritative.

The initial save stores complete logical encounter state, inventory/rolled values, equipped identity, fragment IDs, level/XP stub, RNG, and compatibility/checksum fields. Write a flushed temporary file and atomically rename within the same directory. Retain one last valid backup. Recover a corrupt/missing primary from backup, but never silently downgrade an incompatible newer save. Concurrent writers and power-failure filesystem guarantees beyond atomic same-filesystem replacement are not supported in Phase 0.

There is one schema version and no fabricated migration. Future schema/content changes must add an explicit compatibility/migration policy and fixtures. A rules version is distinct from package version and content hash. No scenes or display names are persisted as identifiers.

The Go CLI finds the repository and delegates to the .NET tooling program. Pin dependencies with committed lockfiles; official packages are downloaded into a checked local feed. CI formats, builds, runs tests, validates/compiles content, checks replay/save, imports the Godot project, exports a package, and runs its smoke scenario.

## Deferred work

Defer a general ECS, native extensions, FMOD, backend/online services, procedural maps, advanced editor authoring, and production assets. The main spike limitations are a single stationary enemy, one ability and status/fragment interaction, no player damage/death mechanic, no configurable loadout UI, and a single-room collision model. These are Phase 1+ work rather than hidden implementations in Godot scenes.

References: [Godot 4.6 command-line interface](https://docs.godotengine.org/en/4.6/tutorials/editor/command_line_tutorial.html), [Godot 4.6.2 release](https://github.com/godotengine/godot/releases/tag/4.6.2-stable), and [official .NET 8 release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json).
