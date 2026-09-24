# Ashenwake

An isometric action RPG about building power from the remains of dead gods. The implementation includes an offline five-act greybox campaign, permanent builds, Fractures, God Hunts, release engineering, an optional authoritative two-player slice, and the permanent Borrowed Memory experiment. Phase verification documents record automated evidence, review status, and the remaining production acceptance gates.

Choose Vanguard, Veilwalker, Arcanist, Gravecaller, or Warden, each with six skills and its own resource loop. Travel through five regions, rescue Greyhaven's specialists, choose consequential alliances, confront five bosses, and investigate optional exploration encounters. Equipment, anatomy, mastery, Manifestations, crafting, and rewards persist. Godot renders an engine-independent C# simulation.

Heroes, Greyhaven specialists, and enemies now use articulated low-poly models with distinct heads, clothing, armor, weapons, and monster silhouettes. The [character art notes](docs/character_art.md) describe the character models and their reproducible preview. Greyhaven and Act I have stone streets, workshops, memorial ruins, a funeral cloister, and a chain-hung bell sanctuary; see the [opening environment art notes](docs/environment_art.md). [Act II's Verdant Maw](docs/verdant_maw.md) adds root-invaded temples, a quarantine settlement, a bone-tree hunting grove, physical tracking clues, and a Rootheart corpse-flower that opens after victory, with regional haze and synthesized forest ambience. [Act III's Cinder Reach](docs/cinder_reach.md) adds volcanic cities, extraction machinery, Burning Rain ash and a Furnace Spindle that exposes its core during actual combat windows. [Act IV’s Shattered Spine](docs/shattered_spine.md) adds bone cities, inscribed law halls, an animated Covenant Warden and a warmer Divine Memory with numbered, reversed fault warnings. [Act V’s Hollow Night](docs/hollow_night.md) completes the regional pass with repeating architecture, identity memories, timed echo warnings and a three-phase Breach Heart containment finale.

The [graphics polish pass](docs/graphics_polish.md) adds beveled character shapes, more detailed faces, textured stone/wood/metal/cloth, sky reflections, contact shading and smoother edges. Choose **Settings → Graphics → High / Performance**; Reduced Effects also suppresses bloom.

The [visual depth pass](docs/visual_depth.md) adds warm lantern and candle lighting, worn paving and ruined masonry, folded cloth and layered armor, and more distinctive opening monsters. Reduced Effects keeps the light pools steady; Performance uses fewer local lights.

The [graphics smoothing pass](docs/graphics_smoothing.md) adds rounder characters and monsters, smoother armor edges, 512-pixel material maps and sharper equipment previews. Settings → Graphics offers 100%/125%/150% 3D resolution, stronger edge smoothing and native fullscreen display.

The [weapon animation pass](docs/weapon_animation.md) adds equipment-specific attacks, Act I enemy anticipation, distance-driven footsteps, smoother recovery and effects that follow animated weapons.

The [Verdant creature animation pass](docs/verdant_animation.md) adds crawling swarms, rooted plant strikes, heavier carrier and Antler motion, and more expressive Rootheart root-loss and victory transitions.

The [Verdant depth pass](docs/verdant_depth.md) adds curved foliage, tapered trees and roots, connected mossy earth, village boardwalks and leaf roofs, gentle wind, fungal light, and a layered Rootheart bloom. High and Performance settings also control the new foliage and local lights.

The [Cinder depth pass](docs/cinder_depth.md) adds fractured basalt, worn metal paving, round pipes and reinforced machinery, forge and inspection lighting, moving pump wheels, and a more detailed Furnace Spindle with a cooling core light.

The [Spine depth pass](docs/spine_depth.md) adds curved bone arches, carved tablets, worn court masonry, warm hearth and reading lights, gently moving folded banners, a golden Divine Memory, and layered Covenant Warden shields and seals.

The [Hollow depth pass](docs/hollow_depth.md) adds curved sealed arches, recessed memorials, fractured obelisks, worn slate, cyan and violet inset lights, suspended fragments, and a more detailed Breach Heart with smooth final containment.

The opening environments now have distinct Greyhaven service courts, a winding Grey March road, a ruined monastery courtyard, layered cliffs and restrained chimney smoke and drifting leaves. The Grey March now has real road bends, monastery passages and sanctuary pillars. A side path off the secured road leads to the Widow’s Crypt, with elite guardians, rare armor and hidden testimony. Cleared opening rooms retain uncollected loot for backtracking; see [Grey March exploration](docs/grey_march_exploration.md).

Combat now includes discipline-specific attacks, enemy anticipation and recovery, dodge and hit reactions, and visible defeat clips. Short weapon/spell effects and twenty-one synthesized sound cues follow Core combat events. The Bell Saint sanctuary swings, sheds broken links, and settles after victory. See [combat feedback](docs/combat_feedback.md) for scope, accessibility behavior, and diagnostics.

Enemies have compact health bars. The selected or hovered enemy's exact health and conditions appear in one detail card, while required boss and seal instructions remain overhead. The objective card and a clickable **WAY FORWARD** marker after cleared campaign encounters offer the same contextual next action; remaining ground loot stays behind an explicit reward/travel review, and permanent story choices still require confirmation. See [mouse actions verification](docs/mouse_actions_verification.md).

Solo characters now display equipped weapons, off-hand items, helmets and chest armor, including Ashcleaver's awakened forms. The four Manifestations add burning fissures, spectral echoes, stone plates or living growth. Press **I** for a body-shaped equipment layout and inventory grid: item icons and rarity borders identify gear, hovering compares it with the equipped item, and search, filters and sorting organize the backpack. Drag gear onto a compatible slot to equip it, or back into inventory to unequip it. Blocked drops explain why beside the pointer. Clicking retains the rotating preview; changing equipment requires Torren. Select unwanted backpack gear near Torren and choose **Discard selected item…** to free space after confirming permanent removal. Ground drops have recognizable item shapes and six rarity markers. See [equipment controls](docs/gear_drag.md) and [visible progression](docs/visible_progression.md).

After rescuing Torren, visit the **Personal stash** chest beside his workshop. Click it or press **F** nearby to deposit and retrieve unequipped gear. Four renameable tabs hold 128 items each; drag items between backpack, storage and tabs, or select an item and use its transfer button. Search and filters include favorites, locks and saved build references. **Gear → Personal stash** lets you inspect storage anywhere and approach the chest from Greyhaven. Stored items retain their rolls and saved-build links; retrieve them before equipping or crafting. Storage belongs to the current character. See [personal stash controls](docs/personal_stash.md).

Divine Anatomy now has a clickable six-slot body map, fragment cards and a character preview. Inspect Resonance, combinations and Manifestation changes before applying an implant at Mara; the Bell Saint's Heart of Serath adds a visible chest crest immediately. Open **J → Anatomy**. See [Divine Anatomy](docs/anatomy.md) for the first-upgrade flow and controls.

Open **C → Craft** for the visual crafting workbench. Six service cards, a searchable equipment tray and a drag target replace the old item dropdown. Inspect exact before/after stats, material costs, catalyst balances and permanent outcomes before applying a craft at its specialist. Extraction and Divine Grafting require confirmation. See [crafting workbench controls](docs/crafting_workbench.md).

Every equipment piece has its own lore. Gear, crafting and ground-loot inspection explain special powers, engravings and Ashcleaver's awakening and evolutions. Legendary ground drops announce themselves with a distinctive chime and golden sparks; Godwrought ground-drop cues are also supported. See [equipment lore and discovery](docs/equipment_discovery.md) for behavior and accessibility.

Three [legendary items](docs/legendary_equipment.md) change combat: **Pyrebound Treads** leave burning ground when you dodge, **Oathkeeper’s Reprisal** stores absorbed barrier damage for a melee shockwave, and **Widow’s Last Echo** adds a spectral follow-up to your next projectile after dodging. Earn the boots in the opening Road encounter; later bosses and repeatable regional Fractures provide the other pieces. Their powers can be extracted and engraved onto compatible gear.

Three more [midgame legendaries](docs/midgame_legendaries.md) expand those choices: **Rotwake Signet** spreads poison from kills, **Mantle of the Mourning Choir** rallies your summons when you strike, and **Furnaceheart Cinch** rewards a costly skill followed by resource recovery. Earn them in Acts II–III or their repeatable Fracture rooms. The HUD shows their actual cooldowns, prepared charges and trigger results.

Three [late-game legendaries](docs/late_legendaries.md) reward deliberate skill sequences: **Crown of the Unsworn** grants barrier for successful interrupts, **Vow of the Last Witness** bursts after four distinct attacks against one foe, and **Greaves of the Stolen Hour** shorten three skill cooldowns after an ultimate. Earn them in Acts IV–V, regional Fractures or their corresponding God Hunts; extract spare copies to engrave their powers onto compatible gear.

Open **J → Relics** or **B → Relics** for the [legendary collection journal](docs/legendary_collection.md). Preview revealed items, inspect owned copies and learned powers, and track one item through the Journey map and expedition board. Discoveries survive salvage and extraction; unreached encounter names and secret hunts remain hidden.

Open **C → Skills** for ability cards, mastery progress and mutation comparisons. Preview Offense, Defense and Resource investments before spending points at Mara; passive refunds show the exact fee and effects removed. The combat bar uses the same ability icons with cooldown progress, resource-shortfall and Heat-cap feedback. See [Skills & Mastery](docs/skills_mastery.md).

The solo combat HUD groups health, barrier, discipline resource, potion charges, dodge readiness and six abilities in a responsive bottom dock. Status icons show remaining duration and stacks; the XP bar tracks the current level. Earned levels, mastery milestones, ability unlocks and collected equipment appear in a compact reward feed. See [combat HUD and rewards](docs/combat_hud.md).

Press **J** for the connected Journey map. Select Greyhaven or a campaign region to preview its route, unlock state, encounter progress and next objective, then choose a travel action. Leaving ground loot requires confirmation. The Journal organizes objectives, earned discoveries, reached choices and rescued residents; future revelations remain hidden. See [Journey map and journal](docs/journey_map.md).

Press **B** for the visual expedition board: inspect Sigil cards and connected routes, preview attunement, browse God Hunt emblems and unlock requirements, and track rooms, attempts and earned rewards. Sigil consumption, hunt entry, abandonment and leaving ground loot have explicit confirmation controls. See [Fractures & God Hunts controls](docs/expedition_board.md).

[Endgame combat cues](docs/endgame_combat_polish.md) identify the weak points or mechanisms that expose a boss, distinguish timed vulnerability from a permanently broken shield, and name incoming attacks. Fracture rules show live hazards, surge timing and inherited traits with counterplay. `aw balance endgame <new-directory> 3` measures fresh earned builds through Fracture tiers 1–3 across all five disciplines and seeds 42–44.

The [campaign pacing pass](docs/campaign_pacing.md) smooths level gains across all five acts, gives optional testaments useful craftable affixes, and adds **Walk to the Fracture gate** to the expedition board. Existing saves retain earned equipment and receive any earlier XP difference once.

The [Act I combat-depth pass](docs/opening_combat_depth.md) gives the opening enemy groups clearer roles, adds the monastery's interruptible Dirgebound ally ward, and guides the first Pyrebound Treads and Heart of Serath upgrades through the existing services.

The optional [First steps guide](docs/opening_guidance.md), available from Journey, connects those upgrades to training and unlocked Greyhaven services. Contextual control hints remember dismissals per character and can be disabled or revisited from the guide.

The [Acts II–III combat-depth pass](docs/midgame_combat_depth.md) gives Living Ruins, Plague Village, the Cinder fields and Extraction Floor deliberate enemy formations. Interrupt Bloom Carriers before they mend wounded allies and Heat Tenders before they empower forge sentinels. Fixed support rings distinguish these channels from damaging warnings; Rootheart and Furnace Spindle show their actual attack and recovery windows.

The [Acts IV–V combat-depth pass](docs/late_campaign_combat_depth.md) adds interruptible contract-keeper wards, coordinated shadow/archer formations, and numbered Covenant Warden and Breach Heart attacks. Protection, guard and recovery countdowns make the final bosses’ existing openings explicit.

The [opening audio pass](docs/opening_audio.md) adds five original regional themes, adaptive combat and Bell Saint layers, terrain footsteps, richer impacts and creature warnings. Music ducks for important cues and respects the existing Music and Effects controls.

Visit Greyhaven's **Training ground** to practice your current build against one target or a group and inspect damage, effect triggers, and resource usage. Practice grants no XP, mastery, or loot and leaves your character unchanged. At Torren, open **Gear → Equipment presets** to save and switch up to eight named outfits. See [training](docs/training_ground.md) and [equipment presets](docs/equipment_presets.md).

Open **C → Character → Build loadouts** or **Gear → Build loadouts** to save eight complete builds covering equipment, fragments, Manifestations, skill mutations and passives. Switch at Mara after reviewing costs and requirements; the transaction applies everything together or leaves your character unchanged. Training comparisons identify matching named loadouts. See [complete build loadouts](docs/build_loadouts.md).

Greyhaven’s **Regional hunts** board offers three repeatable contracts unlocked after Acts 1–3. Follow three clues, defeat the quarry, then return to claim a named legendary item and materials. See [regional hunts](docs/regional_hunts.md).

Three optional [roaming champions](docs/roaming_champions.md) inhabit secured side areas in Acts I–III: **The Bell-Torn Pilgrim**, **Widow of the Root**, and **The Cinder Tithekeeper**. Inspect their signs, enter a safe refuge and explicitly accept the challenge. Each guards named signature equipment, claimed once per character. **J → Champions** records discovered sightings; retreat preserves the source room, and victory preserves unclaimed treasure.

In **Gear**, favorite or lock an item to protect it from discard, extraction and salvage. Filter the backpack by favorites, locked gear, unused items or saved outfits. Item details and permanent-removal confirmations identify affected outfits. At Torren, **Salvage selected item…** previews an exact material return before permanently consuming eligible unequipped gear. See [loot management](docs/loot_management.md).

After a solo campaign or endgame defeat, the [death recap](docs/death_recap.md) pauses to show the killing blow, recent health loss, harmful conditions and recovery consequences. Continue from the restored campaign checkpoint, retry an expedition with remaining attempts, or return to Greyhaven after the final attempt. Loaded defeated expeditions show recovery controls without inventing missing damage history.

The training ground also offers **Melee**, **Ranged**, and **Mixed sparring**. Its breakdown shows actual health loss, barrier absorption, mitigation, immunity and defensive effects. Reset or leave to retain one previous attempt, change your equipment in Greyhaven, then return to compare the results. Practice and comparison history never change your saved character. See [defensive training](docs/training_ground.md).

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

Ordinary launch opens the [main menu and character browser](docs/front_menu.md). Preview a discipline under **New Character**, then choose **Begin** to create a separate save slot. **Continue** identifies the last played character and location; **Characters** lists original and Echoes saves with recovery information. During play, **P → Save & main menu** returns to character selection while preserving your progress.

[Settings & Controls](docs/settings_controls.md) provides tabbed key bindings, independent audio channels, accessibility explanations and loot preferences. Each tab has its own Restore defaults action; failed preference writes keep the chosen settings active for the session.

Choose a discipline and click Mara to approach and open her conversation and the Journey map. F still interacts when nearby. Select **The Grey March**, then **Travel to Act 1 · The Grey March** to leave Greyhaven. J or the **Journey map & anatomy [J]** button opens or closes the map; F interacts with nearby people and mechanisms. Mara's conversation also offers **Divine Anatomy** for implants. Defeat the actual encounter, collect its loot, and continue to the next objective. Resolve each region's choice before confronting its boss. In the Bell Saint's second phase, attack both ritual anchors to remove its protection. Return to Greyhaven for services and anatomy changes; deaths restore the current anchor while earned progression and choices persist. The map offers unlocked acts and the Story and Journal tabs expose choices, discoveries, and delayed consequences.

Act I’s Widow’s Crypt branches north from the secured road. Click its marker to approach and enter, defeat the guardians, then open the Widow’s Testament for rare armor and 25 materials. The western passage returns to the road. Click the return and revisit markers to backtrack between cleared opening rooms; their remaining drops and the claimed testament persist in your save.

Act II now has connected paths through the Living Ruins, Plague Village and Rootheart. A northern trail from the secured ruins leads to **Briarheart Shrine**, with elite guardians, hidden testimony, a rare **Orrun’s Oathseal** and 30 materials. The southern trail from the secured village leads to **the Antler Grove**: follow three clues to reveal its god. Click the world passages or use F nearby to travel; cleared rooms keep their remaining loot and map discovery. See [Verdant exploration](docs/verdant_exploration.md).

Act III connects the Cinder fields, Extraction Floor and Furnace Spindle through physical passages. The northern branch from secured fields leads to **the Sealed Foundry**, where elite guardians protect the **Foundry Testament**, a rare **Cinderwake Saber** and 35 materials. The southern branch from the secured Extraction Floor enters **Burning Rain**, a 30-second storm encounter. Victory stops its timer and preserves its loot; the western passage returns to the floor. Cleared rooms retain drops and map discovery. See [Cinder exploration](docs/cinder_exploration.md).

Act IV connects **Bone Causeway**, **Contract Hall** and **Covenant Warden** through bone-lined passages and tribunal courts. A northern branch from the secured causeway leads to **Oathkeeper’s Archive**, with elite guardians, lost testimony, a rare **Vowkeeper’s Carapace** and 40 materials. The southern branch from secured Contract Hall enters **The Promise Before Stone**, a Divine Memory with reversed fault warnings. Cleared areas keep their drops and map discovery, and the Memory remains accessible after victory. See [Shattered Spine exploration](docs/spine_exploration.md).

Act V connects **Repeating Rooms**, **Identity Memory** and **Breach Heart** through marked physical passages. Repeated doorways and shadow decoys surround the route; real exits carry anchor glyphs. The northern branch from secured Repeating Rooms leads to **The Unremembered Vault**, with elite echoes, lost testimony, a legendary **Choir of the Unburied** ring and 45 materials. Cleared rooms retain loot and map discovery, including the final boss chamber. See [Hollow Night exploration](docs/hollow_exploration.md).

After securing rooms in Acts I–III, watch for unusual details among the scenery. Click one to approach it, or press **F** nearby, then read its markings and choose a response. Three clues reveal an optional hidden passage; **J → Discoveries** keeps the observations you have earned. Entering leads to a safe foyer: the guardian only awakens after you explicitly accept its challenge. You can return through the passage at any time. Retreat or defeat resets an unfinished fight, while solved clues and victories persist; a won but unclaimed treasure remains available when you return.

Each hidden chamber grants one unique legendary per character through an explicit treasure claim. **Grief’s Reprieve** heals after a successful interrupt, **Widowthorn** snares nearby foes after a poison kill, and **Emberwake Mantle** rewards a narrowly dodged Fire attack with a short damage bonus. Leave one inventory slot free before challenging a guardian. Their powers can be extracted and engraved onto compatible gear. See [secret chamber verification](docs/secret-chambers-verification.md) for implementation scope and evidence.

The **local minimap** reveals nearby terrain as you explore. Press **M** or click its header for the expanded room map; discovered exits, services, treasure, and remaining loot are marked. Click revealed ground to move there. **M** or **Escape** closes the expanded map. Exploration is saved separately for each room and returns when you backtrack. See [local exploration map](docs/local_exploration_map.md).

Act II contains an ordered tracking hunt, Act III a timed Resonance Storm, and Act IV a Divine Memory with reversed fault warnings. These temporary contexts clean up on expiry, departure, victory, or death. Both final story outcomes unlock the Fracture gate in eastern Greyhaven. Press B to inspect Sigils, four-room routes, rules, hunt gates, and rewards. Sigils are consumed on entry; three attempts and explicit retry/abandon controls make failures recoverable. A free tier-one recovery Sigil is available when no unconsumed Sigils remain. Cleared tiers unlock four distinct God Hunts and an optional secret reconstruction. Their catalysts support permanent Godwrought choices and crafting.

| Action | Control |
|---|---|
| Move on the world X/Z plane | Left-click ground; W / A / S / D or left stick overrides |
| Primary / secondary attack | Left-click enemy; Shift + left-click to stand and attack / right-click |
| Six abilities / stop movement | 1–6 / X |
| Dodge / potion | Space / Q |
| Collect a selected drop / nearby loot | Click the drop / E |
| Reset the diagnostic arena | R in Sandbox |
| Pause / advance one paused tick | P / period |
| Save / load | F5 / F9 |
| Save and verify recent replay | F6 |
| Character and crafting / inventory and equipment / settings | C / I / Escape |
| Cycle target / camera zoom | Tab / mouse wheel |
| Interact / journey map, journal and services | Click a visible target or F nearby / J |
| Local exploration map / move to explored ground | M / click revealed ground on either map |
| Fractures, God Hunts, expedition progress | B |
| Echoes board / bind an offered nearby memory | H |
| Reveal all ground loot | Hold Alt |
| Consume a corpse / use captured elite echo | V / G |

After a fight, the objective explains any remaining ground loot and a **next step** button opens the relevant Map or Story tab. **Continue onward** appears at the top of the map. Settle the region's story choice before the boss; after victory, the map offers the next unlocked act. The Bell Saint's shield phase shows how many ritual anchors remain, with persistent labels on both anchors. Rescued specialists open their matching Gear or Craft screen when you interact.

Press **P** for a visible pause menu with Resume and Settings. Closing settings, loot inspection, or Echoes preserves a manual pause or a pause caused by focus loss; choose Resume when ready. Opening a modal cannot be used to bypass its pause with P.

Click-to-move routes around obstacles and living actors in solo and co-op. A mint ring marks the destination; menus, attacks, scene changes and interruptions cancel the route. In solo, click a visible person, clue or available mechanism to approach and interact; click a visible drop to approach and collect that item. Hover names the action. **F** and **E** still work nearby. See [mouse movement](docs/mouse_movement.md) for controls and verification.

Build Momentum, Exposure, Instability, Remains, or Adaptation through your discipline's actions; the skill bar shows costs and heat generation. Ultimates unlock at level 10; retraining unlocks at level 5 and costs five materials. Master skills to select mutations, and spend level-earned passive points near Mara. Equipment has twelve slots, with affixes and hand/discipline restrictions. Rescue the specialists through dungeon objectives and restore their workshops to unlock all six crafting services. Destructive extraction and permanent grafts require confirmation. The prototype grants an unawakened Ashcleaver; its canonical awakening requirement remains 1,000 burning-enemy kills.

Run `"$GODOT" --path game/Ashenwake.Client res://Sandbox.tscn` for the independent diagnostic arena, where all prototype fragments, equipment, and workload presets are unlocked. Settings include rebinding and reduced effects/shake; a controller has movement and combat bindings.

Pass `-- --output=/absolute/directory` to select the save/replay directory. F6 verifies the current endgame replay segment, which retains at most 1,800 operations and its checkpoint to bound memory. Saves join campaign choices, expedition manifests/attempts, permanent progression, reward receipts, pending abilities/statuses, item instances, cooldowns, and RNG streams. A separate local profile shares only validated discoveries and unlocks across characters. Corrupt primaries can recover from a valid backup; incompatible versions are preserved. Import a Phase 4 character from the board after returning it to Greyhaven in that build; import creates a new archive and preserves the source. `--pseudo-locale` expands extracted production labels for layout inspection. Run `res://Campaign.tscn` for the retained Phase 4 campaign, `res://Production.tscn` for Phase 3, or `res://Adventure.tscn` for Phase 2. Settings include saved rarity/discipline loot filters, an all-drops override, and base-stat inspection before collection.

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
aw endgame validate
aw endgame compile
aw endgame demo
aw endgame benchmark
aw endgame builds
aw endgame exhaustive
aw endgame replay /path/to/endgame.awendgame
aw endgame migrate-phase4 /path/to/campaign.save.json /new/directory/endgame.save.json
aw release audit
aw release fixtures
aw release projectile-ceiling
aw release endgame-soak artifacts/release/new-persistent-run 3
aw authoring validate
aw authoring templates artifacts/new-templates
aw authoring pseudo artifacts/text.qps-ploc.json
aw balance run
aw balance loot
aw balance campaign artifacts/balance/new-campaign 1 --managed-build
aw balance campaign artifacts/balance/new-main-route 1 --main-path --managed-build
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

Phase status and evidence are recorded in [Phase 1 verification](docs/phase1_verification.md), [Phase 2 verification](docs/phase2_verification.md), [Phase 3 verification](docs/phase3_verification.md), [Phase 4 verification](docs/phase4_verification.md), and [Phase 5 verification](docs/phase5_verification.md). See [production authoring](docs/content_production.md) and the [endgame contract](docs/endgame_contract.md) for scope and rules. The scenes use procedural prototype geometry and audio. Automated correctness and profiling do not replace external playtests, production art, or platform certification.

## Release validation

`bash tools/release-verify.sh` runs fixture/asset audits, repeated combat and continuing endgame soaks, and the actual release-settings smoke. `bash tools/release-package.sh <reviewed-commit-or-tree-hash>` exports and runs the application, collects pinned runtime notices, and creates a new local candidate with an immutable integrity manifest. It does not publish or sign a release. See [Phase 6 evidence](docs/phase6_verification.md), [release readiness](docs/release_readiness.md), and [third-party notices](docs/third_party_notices.md).

Focus loss or controller disconnect pauses and clears held inputs; reconnection requires explicit resume. Settings recover from a validated backup. The settings panel can export bounded local diagnostics with the exact loaded Core build/content identity. Replay is excluded by default and nothing uploads automatically.

## Optional local co-op

`bash tools/coop-verify.sh` verifies shared combat and private PostgreSQL persistence. `bash tools/coop-network-verify.sh` runs two authenticated peers with software input impairments, reconnect and dedicated-server restart, checking durable rewards and the replay chain. `bash tools/coop-package-verify.sh` exports the actual client and verifies its offline, release-check and two-player entry routes. The service scripts refuse occupied test ports and clean up only their own processes.

Launch the exported application with `-- --coop` for the connection screen. The prototype uses one-time allocation tickets from the local control service; it does not accept offline characters. See [co-op setup](docs/coop_network.md), [online services](docs/online_services.md), [combat contract](docs/coop_combat.md), and [Phase 7 verification](docs/phase7_verification.md). No public service is deployed.

## Echoes: Borrowed Memory

After unlocking Fractures, press **H** at Greyhaven to open the optional Echoes board. Entering with an owned Sigil creates a separate Echoes character and preserves the original. Keep your Mind for an ordinary run, or suppress its effect to bind one defeated elite's memory and borrow Echo Storm. Its cast warns of a hostile Storm field at your feet. Complete the Fracture after using it to earn a cosmetic record. The board supports save, continue, and safe return to the original character.

The [visual Echoes screen](docs/echoes_screen.md) compares both choices, shows the active memory and hostile Storm countdown, and provides Record and Character tabs for earned cosmetics, contract history and separate character navigation.

`bash tools/experiment-verify.sh` validates the content, completes the CLI and Godot routes, and checks completion, Keep Mind and Release replays. `aw experiment import-endgame <source> <new-destination>` explicitly imports a validated hub character without overwriting it. `tools/export.sh` includes the actual packaged Echoes smoke. See [experiment design](docs/permanent_experiment_design.md), [archive contract](docs/permanent_experiment_contract.md), and [Phase 8 verification](docs/phase8_verification.md).
