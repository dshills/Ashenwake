# Ashenwake

An isometric action RPG about building power from the remains of dead gods. The current playable milestone is **Phase 1: the combat sandbox**.

The sandbox has six Vanguard abilities, Momentum, dodge, potion, active enemy AI, loot, anatomy, contrasting mutations, and bounded fire/spirit/poison interactions. Godot renders an engine-independent C# simulation. Structured content, a Go developer CLI, deterministic replay, logical saves, tests, and packaged-build checks support development.

## Start here

Requires Git/Git LFS, Python 3, `curl`, `unzip`, `ripgrep`, and **Go 1.27.1**. The bootstrap installs **.NET SDK 8.0.425** and **Godot 4.6.2 Mono** inside ignored `.tools/`; it does not replace the system Godot. Supported bootstrap hosts are macOS Apple Silicon and Linux x86_64.

```bash
bash tools/bootstrap.sh --templates
source tools/env.sh
bash tools/verify.sh
"$GODOT" --path game/Ashenwake.Client
```

The template download is approximately 1.2 GB and is required only for exporting. Omit `--templates` when developing without an export. Bootstrap fetches packages from official Microsoft/NuGet/Godot endpoints and checks pinned archive hashes; locked restore also checks NuGet's normalized content hashes. Build and export restore use the resulting local feed, so they do not need NuGet networking. Deliberate dependency updates require updating the committed lockfiles, package archive manifest, and runtime pack inventory; vulnerability auditing belongs in that update review, since normal offline restores disable the network audit.

Run `source tools/env.sh` in Bash; it configures the local tools without changing shell startup files. `bash tools/verify.sh` sources it automatically. The test host uses a local socket, and Godot initializes its ordinary application-data directories; a restrictive sandbox must allow those operations.

## Play the combat sandbox

| Action | Control |
|---|---|
| Move on the world X/Z plane | W / A / S / D |
| Six abilities | 1–6; left/right click for primary/secondary |
| Dodge / potion | Space / Q |
| Collect nearby loot | E |
| Reset the seeded encounter | R |
| Pause / advance one paused tick | P / period |
| Save / load | F5 / F9 |
| Save and verify recent replay | F6 |
| Inventory/anatomy / settings | I / Escape |
| Cycle target / camera zoom | Tab / mouse wheel |

Build Momentum with Cleave or Breaker Charge, then spend it on Shield Breaker, Seismic Wave, Iron Guard, or Cataclysm. Enemy windup circles show when to move or dodge. Inventory compares equipment, equips fragments, and switches Shield Breaker between Avalanche and No Ground Given. The arena selector includes dense melee, projectile, summon, and chain-reaction workloads. Settings include rebinding and reduced effects/shake; a controller has movement and combat bindings.

Pass `-- --output=/absolute/directory` to select the save/replay directory. F6 verifies the current replay segment, which rolls over after 3,600 ticks (120 simulation seconds) to bound memory. Reset and load start new segments. Saves include pending abilities/statuses, item instances, cooldowns, and all RNG streams. Corrupt primaries can recover from a valid backup; incompatible versions are preserved for their matching build.

The original Phase 0 fixture remains available with `"$GODOT" --path game/Ashenwake.Client res://Main.tscn`. Its smoke, replay, and hash checks still run as regression coverage.

## Developer commands

After verification builds `.tools/bin/aw`:

```bash
aw content validate
aw content compile
aw demo run
aw replay run artifacts/phase0/demo.awr
aw benchmark run
aw sandbox validate
aw sandbox compile
aw sandbox demo
aw sandbox builds
aw sandbox replay artifacts/combat/session.awc
aw sandbox benchmark
bash tools/export.sh
```

Run `aw` within this checkout; source/output paths are relative to the repository root. Domain operations delegate to the C# tooling executable. The Go CLI contains no combat rules.

`tools/export.sh` exports the current host preset, launches the packaged game headlessly, and verifies its replay with Core. The macOS output is `artifacts/export/Ashenwake.zip`; Linux produces `artifacts/export/Ashenwake.x86_64`. macOS builds use ad-hoc signing for local development, not notarized distribution. Runtime libraries accompany the Linux executable; keep the export directory together.

## Structure and documentation

- `game/Ashenwake.Core`: content model/compiler, fixed simulation, collision, events, save/replay.
- `game/Ashenwake.Client`: Godot scene, input bridge, presentation, profiling HUD, smoke runner.
- `game/Ashenwake.Tooling`: validation, compilation, demo, replay, and benchmark commands.
- `game/Ashenwake.Tests`: rule, interaction, persistence, collision, and boundary tests.
- `content`: editable source data, JSON schema, and authoring notes.
- `tools`: Go `aw`, pinned bootstrap, verification, and export scripts.
- [Architecture decisions](docs/decisions/0001-phase0-foundation.md), [simulation contract](docs/simulation_contract.md), [combat rules](docs/combat_rules.md), and [Phase 0 verification](docs/phase0_verification.md).
- [Implementation plan](specs/Ashenwake_Implementation_Plan.md), [game design](specs/Ashenwake_Game_Design.md), and [technical architecture](specs/Ashenwake_Technical_Architecture.md).

Phase status and measured evidence are recorded in `docs/phase1_verification.md`. Automated correctness and profiling do not replace the implementation plan's external playtest and production-art acceptance gates.
