# Ashenwake

An isometric action RPG about building power from the remains of dead gods. The current working milestone is **Phase 4: a playable five-act greybox campaign**, built on the five-discipline progression foundation. Phases 3–4 have isolated test and macOS package verification plus Prism review. Review fixes and remaining production acceptance gates are recorded in their verification documents. Endgame integration and release hardening remain unfinished.

Choose Vanguard, Veilwalker, Arcanist, Gravecaller, or Warden, each with six skills and its own resource loop. Travel through five regions, rescue Greyhaven's specialists, choose consequential alliances, confront five bosses, and investigate three optional exploration encounters. Equipment, anatomy, mastery, Manifestations, crafting, and rewards persist. Godot renders an engine-independent C# simulation.

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

## Play the campaign

Choose a discipline, speak with Mara, and open the campaign map with J to enter Act I. Defeat the actual encounter, collect its loot, and continue to the next objective. Resolve each region's choice before confronting its boss. In the Bell Saint's second phase, attack both ritual anchors to remove its protection. Return to Greyhaven for services and anatomy changes; deaths restore the current anchor while earned progression and choices persist. The map offers unlocked acts and the Story and Journal tabs expose choices, discoveries, and delayed consequences.

Act II contains an ordered tracking hunt, Act III a timed Resonance Storm, and Act IV a Divine Memory with reversed fault warnings. These temporary contexts clean up on expiry, departure, victory, or death. Both final story outcomes reach a common endgame unlock; playable Fractures and God Hunts remain the next integration milestone.

| Action | Control |
|---|---|
| Move on the world X/Z plane | W / A / S / D |
| Six abilities | 1–6; left/right click for primary/secondary |
| Dodge / potion | Space / Q |
| Collect nearby loot | E |
| Reset the diagnostic arena | R in Sandbox |
| Pause / advance one paused tick | P / period |
| Save / load | F5 / F9 |
| Save and verify recent replay | F6 |
| Character, equipment, crafting / settings | C or I / Escape |
| Cycle target / camera zoom | Tab / mouse wheel |
| Interact / journey map, journal and services | F / J |
| Consume a corpse / use captured elite echo | V / G |

Build Momentum, Exposure, Instability, Remains, or Adaptation through your discipline's actions; the skill bar shows costs and heat generation. Ultimates unlock at level 10; retraining unlocks at level 5 and costs five materials. Master skills to select mutations, and spend level-earned passive points near Mara. Equipment has twelve slots, with affixes and hand/discipline restrictions. Rescue the specialists through dungeon objectives and restore their workshops to unlock all six crafting services. Destructive extraction and permanent grafts require confirmation. The prototype grants an unawakened Ashcleaver; its canonical awakening requirement remains 1,000 burning-enemy kills.

Run `"$GODOT" --path game/Ashenwake.Client res://Sandbox.tscn` for the independent diagnostic arena, where all prototype fragments, equipment, and workload presets are unlocked. Settings include rebinding and reduced effects/shake; a controller has movement and combat bindings.

Pass `-- --output=/absolute/directory` to select the save/replay directory. F6 verifies the current campaign replay segment, which retains at most 1,800 operations and its checkpoint to bound memory. Saves include campaign choices, scoped encounters, permanent progression, pending abilities/statuses, item instances, cooldowns, and RNG streams. A separate local profile shares only validated discoveries and unlocks across characters. Corrupt primaries can recover from a valid backup; incompatible versions are preserved. `--pseudo-locale` expands extracted production labels for layout inspection. Run `res://Production.tscn` for the retained Phase 3 dungeon and services, or `res://Adventure.tscn` for the Phase 2 client.

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
aw adventure validate
aw adventure compile
aw adventure demo
aw adventure benchmark
aw adventure replay artifacts/adventure/session.awe
aw item show item.ashcleaver
aw content refs fragment.heart_serath
aw production validate
aw production compile
aw production demo
aw production benchmark
aw production replay /path/to/production.awp
aw production migrate-phase2 /path/to/expedition.save.json /new/directory/production.save.json
aw campaign validate
aw campaign compile
aw campaign demo
aw campaign benchmark
aw campaign replay /path/to/campaign.awcampaign
aw campaign migrate-phase3 /path/to/production.save.json /new/directory/campaign.save.json
aw authoring validate
aw authoring templates artifacts/new-templates
aw authoring pseudo artifacts/text.qps-ploc.json
aw balance run
aw balance loot
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

Phase status and evidence are recorded in [Phase 1 verification](docs/phase1_verification.md), [Phase 2 verification](docs/phase2_verification.md), [Phase 3 verification](docs/phase3_verification.md), and [Phase 4 verification](docs/phase4_verification.md). See [production authoring](docs/content_production.md) for templates, localization, and inventory scope. The scenes use procedural prototype geometry and audio. Automated correctness and profiling do not replace external playtests, production art, or platform certification.
