# Ashenwake

An isometric action RPG about building power from the remains of dead gods. This repository currently implements **Phase 0: the architecture spike**, not the combat sandbox or campaign.

The spike contains an engine-independent C# simulation, a Godot C# room, structured content, a Go developer CLI, deterministic replay, logical saves with backup recovery, automated tests, and packaged-build checks.

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

## Play the architecture spike

| Action | Control |
|---|---|
| Move on the world X/Z plane | W / A / S / D |
| Strike the Ash Ghoul | Space or left click |
| Collect nearby loot | E |
| Reset the seeded encounter | R |
| Pause / advance one paused tick | P / period |
| Save / load | F5 / F9 |
| Save and verify recent replay | F6 |

Strike once while in range. Ember of Vael applies Burning, the status finishes the enemy, and the enemy drops an Ash Iron weapon with a deterministic damage roll. Move closer and collect it. The overlay shows authoritative ticks, live populations, allocations, and simulation-plus-replay CPU time.

F5/F9 use `user://phase0/character.json` and `.bak`; F6 writes `session.awr`. Pass `-- --output=/absolute/directory` to select another location. F6 verifies the current replay segment, which rolls over after 3,600 ticks (120 simulation seconds) to bound memory. Reset and load start new segments. Saves include pending abilities/statuses and all RNG streams, so continuation is exact within the supported build/content configuration.

## Developer commands

After verification builds `.tools/bin/aw`:

```bash
aw content validate
aw content compile
aw demo run
aw replay run artifacts/phase0/demo.awr
aw benchmark run
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
- [Architecture decisions](docs/decisions/0001-phase0-foundation.md), [simulation contract](docs/simulation_contract.md), and [Phase 0 verification](docs/phase0_verification.md).
- [Implementation plan](specs/Ashenwake_Implementation_Plan.md), [game design](specs/Ashenwake_Game_Design.md), and [technical architecture](specs/Ashenwake_Technical_Architecture.md).

Phase 1 adds actual combat feel work: dodge/potion, resources, a full six-ability starter kit, active enemy AI, broader effects, and richer fragment combinations. No backend, networking, campaign, or production assets are implemented yet.
