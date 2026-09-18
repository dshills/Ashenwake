# Ashenwake — Phased Implementation Plan

**Status:** Phases 0–2 have committed, playable engineering implementations. Phase 3 integrates five disciplines, permanent progression, crafting, content tools, and a complete client. Phase 4 connects a playable five-act greybox campaign, actual bosses/exploration, choices, permanent rewards, saves, and the client. Both isolated snapshots pass local tests and macOS package verification. Phases 3–4 are Prism-reviewed with fixes and measured limitations documented, and each has its own commit. Phase 5 integrates playable Fractures, all five God Hunts, canonical rewards/catalysts, archives, and the client; its isolated tests, actual macOS package and Prism review are complete. Phase 6 implements recovery, privacy-bounded diagnostics, maintained upgrades, measured serialization improvements, soaks and candidate packaging; Prism review and the exact-source local package verification are complete. Optional Phases 7/8 are in progress. Production-art, external-playtest, and release acceptance gates remain open. See [Phase 0](../docs/phase0_verification.md), [Phase 1](../docs/phase1_verification.md), [Phase 2](../docs/phase2_verification.md), [Phase 3 evidence](../docs/phase3_verification.md), [Phase 4 evidence](../docs/phase4_verification.md), [Phase 5 evidence](../docs/phase5_verification.md), and [Phase 6 evidence](../docs/phase6_verification.md).

**Planning baseline:** September 17, 2026

**Sources:** [Game Design Foundation](Ashenwake_Game_Design.md) and [Technical Architecture](Ashenwake_Technical_Architecture.md)

Build a small, satisfying, replayable single-player game loop first. Prove that Divine Anatomy creates surprising build interactions, then establish a repeatable content pipeline, produce the campaign and endgame, and release a reliable single-player game. Cooperative play follows as a separately gated expansion.

At the planning baseline, the repository contained the two source specifications and no implementation. This plan therefore starts from project setup. It preserves the architecture's Phases 0–4, adds explicit endgame and release phases, and moves its optional online prototype from Phase 5 to Phase 7. Phase numbers describe dependencies, not calendar commitments.

## 1. Scope, assumptions, and planning rules

### 1.1 Release boundaries

| Milestone | Required scope | Intentionally later |
|---|---|---|
| Architecture proof | Engine-independent Core, one complete combat interaction, deterministic loot, replay, minimal save/load, content validation | Production art, extensive progression, backend |
| Combat sandbox | One discipline, six active abilities, responsive combat, reusable effects, fragment synergy, loot, instrumentation | Campaign and polished hub |
| Vertical slice | Small Greyhaven, one dungeon, Bell Saint, meaningful build choices, one Godwrought item, polished audiovisual feedback | Other disciplines, other acts, endgame breadth |
| Single-player launch | Five disciplines, five campaign acts, Divine Anatomy and Manifestations, Godwrought progression, Greyhaven, crafting, exploration, Fractures and God Hunts, reliable saves and accessible controls | Mandatory accounts, dedicated servers, seasonal service operations |
| Optional cooperative expansion | Authoritative two-player prototype, then separately approved production scope | Unproven player counts, trading, competitive economies, console support |
| Optional seasons | Experimental mechanics that can become permanent content | Daily chores or a required seasonal reset model |

The single-player launch scope above is the planning interpretation of the game design, including both endgame systems. Exact content quantities, supported platforms, languages, hardware tiers, and staffing remain decisions. Do not silently remove a discipline, act, or defining progression system to meet a date; revise the release scope explicitly if capacity cannot support it.

### 1.2 Working assumptions

- Develop an offline-capable single-player game first. Use local profile discovery unlocks initially; “account-wide” progression does not require an online account.
- Use Godot 4.x with a compatible C#/.NET toolchain, pinned during Phase 0. Do not assume a particular patch release or platform export capability until the spike verifies it.
- Start with authored rooms and landmarks, plus seeded encounter variation. Full procedural world generation is unnecessary for the described game.
- Propose Vanguard as the first discipline because melee timing, stagger, defense, dodge, and enemy reactions directly test combat feel. Confirm in Phase 0; its slice fragments must still demonstrate cross-lineage behavior.
- Treat named mechanics as requirements or exemplars according to their wording in the design. For example, Bell Saint is required for the slice; the exact set of additional launch God Hunts must be budgeted.
- Build the `aw` command incrementally. Prefer a thin Go CLI that invokes .NET tooling for domain operations; never duplicate combat or content semantics in Go.
- Use placeholder assets through the sandbox, then establish the slice's final visual and audio standard before commissioning campaign-scale assets.
- Use phase gates rather than speculative dates. Estimate the first backlog after Phase 0; use slice production measurements to estimate the campaign.

### 1.3 Decisions that must become explicit

Record each decision in `docs/decisions/`, including the choice, alternatives, consequences, and owner.

| Decision | Resolve by | Proposed starting position / reason |
|---|---|---|
| Target platforms, input modes, reference hardware | Phase 0 exit | Select one desktop development/export target and a reference machine; decide keyboard/mouse movement and controller policy before implementing input |
| Engine, .NET, Go, build/test tool versions | Phase 0 exit | Pin a verified compatible set and document clean-machine setup |
| World geometry and authoritative queries | Phase 0 exit | Shared Core-compatible geometry/query implementation; Godot authoring exports collision data; headless tests need no engine |
| Determinism guarantee | Phase 0 exit | Same code/content versions, seed, commands, and supported runtime configuration; do not promise cross-platform bitwise identity |
| Combat formulas and event ordering | Phase 1 implementation | Specify damage/defense formulas, conversion, barriers, status stacking, criticals, reflection, attribution, proc limits, and death behavior |
| Ability loadout and cross-discipline access | Phase 1 exit | Six active slots mean primary, secondary, three skills, and ultimate; dodge and potion are additional actions; clarify how starting identity broadens |
| Level cap, XP curve, mastery, respec costs | Phase 3 exit | Prototype short progression in the slice; set launch pacing using measured encounter and reward cadence |
| Fragment removal, Resonance thresholds, Manifestation persistence | Phase 2 exit | Support affordable experimentation and explicit tradeoffs; define whether thresholds are reversible before saving them |
| Campaign save checkpoints and resumable expeditions | Phase 2 exit | Campaign recovery at anchors; decide exactly which combat and expedition state survives quitting |
| Weapon restrictions and main/off-hand compatibility | Phase 3 exit | Define two-handed, shield, focus, dual-wield, and discipline interactions before expanding item content |
| Content inventory and narrative branch budget | Phase 3 exit | Name required regions, bosses, quests, skills, items, languages, and asset variants; distinguish committed content from examples |
| Difficulty levels and build Responses | Phase 4 implementation | Mechanical pressure and fair counters; no mandatory blanket invalidation of a build |
| Online character provenance and rewards | Before Phase 7 persistence | Decide local-only versus separate server-owned characters; locally editable saves cannot directly certify valuable online outcomes |

## 2. Delivery sequence and ownership

### 2.1 Milestone map

| Phase | Player/developer outcome | Primary dependencies | Accountable disciplines |
|---|---|---|---|
| 0 — Architecture spike | Input produces a tested, replayable combat outcome | Initial decisions | Engineering, technical design |
| 1 — Combat sandbox | Level-one combat and a fragment combo are enjoyable | Phase 0 gate | Gameplay engineering, combat design, animation/audio |
| 2 — Vertical slice | Kill Bell Saint, acquire a new build option, want to replay | Phase 1 gate | All disciplines, with a single slice owner |
| 3 — Production foundation | All five disciplines and content workflows scale reliably | Phase 2 fun and feasibility gate | Engineering, systems design, technical art, QA |
| 4 — Campaign production | Complete five-act campaign and reactive Greyhaven | Phase 3 schemas, tools, content budget | Level, encounter, narrative, art/audio, systems teams |
| 5 — Endgame and balance | Replayable Fractures, God Hunts, long-term build progression | Phase 3 systems and Phase 4 progression/ending contracts | Encounter and systems design, engineering, QA |
| 6 — Release hardening | Supported single-player release with tested upgrades | Phases 4 and 5 content complete | QA, engineering, production, accessibility/localization |
| 7 — Optional cooperative expansion | Same Core runs authoritative two-player encounters | Phase 6 baseline and explicit expansion decision | Network/backend engineering, design, operations, QA |
| 8 — Optional seasonal content | A bounded experiment can join the permanent game | Stable release, proven content cadence | Design, content, release/operations |

The critical path is architecture proof → combat feel → replayable slice → production tools → campaign/endgame completion → release. Art exploration, audio studies, and narrative outlining can proceed alongside early engineering. Bulk asset and quest production should wait for the slice and production contracts to stabilize.

Phase 5 prototyping can overlap late Phase 4 after progression and reward contracts stabilize; its completion gate still requires the final campaign-to-endgame transition. Online work must not displace fixing weak single-player combat or build discovery.

### 2.2 Definition of done for every work package

- Player-facing behavior and failure behavior are described, with stable content IDs where applicable.
- Core rules remain testable without Godot; presentation consumes commands, state, and events through the bridge.
- Meaningful rule changes have unit, interaction, or replay coverage appropriate to their risk. Content-only changes pass validators and receive in-game review.
- Persistent state includes a schema/migration decision; new content references resolve and have localization keys.
- Performance impact is measured in the relevant encounter or stress scene, and diagnostic output identifies the build/content version.
- The feature works in a packaged development build, not only inside the editor.
- The responsible designer or discipline lead reviews its feel, readability, or narrative effect; automated tests cannot establish those qualities.

## 3. Phase 0 — Architecture spike

**Objective:** Prove the smallest complete loop from the architecture's first milestone before building substantial gameplay.

**Entry:** The two foundation specifications; no application code is assumed.

### Work packages

**P0.1 — Establish repository and development environment**

- Create `game/Ashenwake.Core`, `game/Ashenwake.Client`, `game/Ashenwake.Tests`, `content`, `tools`, `assets`, `docs`, and `.github`.
- Add a small .NET headless tooling executable for validation, replay, and simulation. Reserve `Ashenwake.Server`, `services`, and `infra` for the online phase rather than populating unused systems.
- Pin tool versions, editor settings, formatting, build commands, ignore rules, and Git LFS patterns for large binary source assets. Separate editable assets from generated Godot imports and exported builds.
- Add a minimal GitHub Actions pipeline: formatting, C# compilation, Core tests, content validation, and a Godot import/export smoke check on the selected platform. Compile/test Go once `aw` exists.
- Document how a fresh checkout builds, runs headless, and launches the scene.

**P0.2 — Define simulation contracts**

- Introduce stable `EntityId`, tick number, simulation clock, commands, state snapshots, and semantic events. Use ordinary data structures and focused systems, not a general-purpose ECS framework.
- Start at 30 simulation ticks/second. Define pause, single-step, bounded catch-up, and render interpolation behavior. Simulation time stops during offline pause; it never reads wall-clock time to determine gameplay.
- Define the update order from command validation through movement, abilities, spatial queries, damage, effects, AI, death, loot, and event publication. State when commands generated by AI become eligible to execute.
- Establish seeded, separate RNG streams for combat, loot, AI, and encounter generation. Persist stream state and stable iteration order where outcomes depend on order.
- Choose numeric conventions, units, stable serialization, and a canonical simulation-state hash for reproducibility checks.

**P0.3 — Resolve engine and spatial boundaries**

- Implement a minimal shared world representation: traversable floor and simple static obstacles, positions, collision shapes, and query results with stable entity ordering.
- Keep canonical movement, collision acceptance, range checks, and hit resolution in Core-compatible code. Godot renders the result and provides authoring tools; its scene state is not the only source of collision truth.
- Define interfaces for overlap, ray/shape tests, and navigation requests. Begin with the smallest movement plane/height model the room needs; document how floors and elevation will expand.
- If an engine query is experimentally used, record its outputs as replay inputs and identify that limitation. The milestone gate still requires a fully headless implementation of the tested interaction.
- Establish animation timing from Core's ability phases. Cosmetic anticipation may reduce perceived input delay, but animation markers and root motion cannot independently decide damage or final positions.

**P0.4 — Build one data-driven interaction**

- Create schemas and stable IDs for one ability, enemy, weapon, fragment, status, and loot table, with localization keys.
- Implement the smallest damage path, health, death, and deterministic loot drop. Use a fragment-triggered Burning application as the initial interaction.
- Compile validated YAML/JSON into a versioned runtime content bundle with a source/content hash. Core consumes the runtime model, not authoring files scattered through scenes.
- Add `aw content validate`, delegating domain validation to the .NET tooling executable. Initially validate IDs, references, ranges, effect names, and required localization keys.

**P0.5 — Connect Godot, replay, and persistence**

- Create one room, one controllable character, one enemy, and an `EntityId`-to-presentation bridge.
- Convert input into commands; render state with interpolation; consume damage, status, death, and loot events for basic feedback.
- Record an initial logical snapshot, content/build identity, RNG state, and commands indexed by simulation tick in a versioned replay format. Compare canonical state hashes and report the first divergent tick.
- Implement a minimal versioned logical save/load path with atomic replacement and one backup. It must preserve the item, fragment, progression stub, and RNG state required by the milestone.
- Add structured diagnostics with session, tick, entity, and content/build identifiers.

### Deliverables and exit gate

- A clean checkout builds Core without Godot assemblies and exports a runnable client.
- Godot input → Core command → damage/status/fragment interaction → death/loot → semantic events → visible feedback works end to end.
- The same recorded sequence runs headlessly and reproduces state hashes and loot for the supported pinned configuration. Changing only AI randomness does not change the loot stream in an isolated test.
- A save/load round trip preserves the milestone's logical state; malformed content fails with actionable errors.
- A performance baseline captures simulation tick cost, frame cost, allocations, and entity/effect counts on the selected reference hardware.
- The team has documented the authority, timing, spatial, determinism, and content contracts. If this loop is awkward, revise it before Phase 1.

## 4. Phase 1 — Combat sandbox

**Objective:** Make a level-one character fighting basic enemies satisfying, then demonstrate behavior-changing build interactions.

**Entry:** Phase 0 contracts and reproducibility gate pass.

### Work packages

**P1.1 — Movement, targeting, and combat timing**

- Implement the chosen keyboard/mouse movement model, facing, targeting, attack move/stop behavior, dodge, potion, and input buffering. Introduce input actions and rebinding rather than device-specific calls throughout gameplay.
- Define windup, active, recovery, cancellation, interruption, cooldown, and resource timing. Implement stagger, invulnerability windows if selected, hit reactions, and movement restrictions explicitly in Core.
- Tune camera angle, zoom, occlusion handling, aim assistance, contact spacing, and enemy readability. Prototype controller navigation/targeting early enough to reveal structural problems.
- Implement authoritative projectiles and ground areas with lifetimes and ownership. Visual trails and debris remain presentation-only.

**P1.2 — Specify and implement reusable combat rules**

- Write `docs/combat_rules.md` before broad effect authoring. Follow the documented pipeline: base damage → skill and attacker modifiers → criticals → conversion → defense/resistance/vulnerability → barrier → health → on-hit/on-damage triggers → death check.
- Define additive versus multiplicative modifiers, caps, rounding, immunities, resistance penetration, damage-over-time critical policy, zero-damage hits, and multi-hit attribution.
- Establish all damage families in the type/content model: Physical with Slash/Pierce/Crush, Fire, Frost, Storm, Decay, Venom, and Void. Activate only the subset needed for the sandbox; complete behavior in Phase 3.
- Build status rules with duration, tick interval, stack/refresh policy, source ownership, cleansing, and consumption. Start with Burning, Poisoned, Staggered, and a representative vulnerability/control effect.
- Implement the reusable effect vocabulary as needed: damage, status, attributes, barriers, healing, resources, entity/projectile/area spawning, teleportation, knockback, conversion, and ability triggers.
- Queue chained effects in deterministic order. Track originating action, owner, source, chain depth, and trigger identity; make death and corpse creation happen exactly once.
- Define explicit per-effect re-entry/cooldown rules and total trigger/entity budgets to prevent infinite feedback. Validation catches statically detectable cycles; runtime safeguards surface diagnostics rather than silently hiding a broken build.

**P1.3 — Six abilities and first discipline resource**

- Implement Vanguard's Momentum generation, spending, persistence/decay, and UI. Confirm that resource rules encourage aggression without forcing one rotation.
- Fill the six active slots with a coherent starter kit covering primary, secondary, area control, mobility/utility, defense, and an ultimate. Dodge and potion remain separate.
- Include Shield Breaker and prototype contrasting Mutations such as Avalanche and No Ground Given. Define compatible mutation combinations, unlock conditions, and respec behavior.
- Keep ability definitions and effect composition reusable for later disciplines; do not build five incomplete classes at this stage.

**P1.4 — Enemies and encounter behavior**

- Implement the AI pipeline: perception → intent → state → ability selection → action command. Begin with melee pressure, ranged/support, and slow armored threat roles.
- Support acquire, approach, attack, reposition, recover, special, and dead states; add flee behavior when an enemy design uses it.
- Give each dangerous attack a clear tell, avoidance/mitigation response, and recovery opportunity. Schedule expensive decisions across ticks and expose target/state debugging.
- Use authored spawn points and seeded pack definitions in a resettable arena. Add an elite modifier prototype and basic invalid-combination rules.

**P1.5 — Divine Anatomy and loot prototype**

- Represent all six anatomy slots: Mind, Eyes, Heart, Spine, Arms, and Legs. Initially populate only the slots needed for the interaction; enforce slot compatibility and clear equip/unequip cleanup.
- Implement Eye of Vael → critical ignition, Heart of Serath → damage-over-time deaths spawn spirits, and Nerve of Ilyra → summon attacks apply poison. Clarify corpse use, kill credit, summon ownership, and whether summon-caused deaths can continue the chain.
- Make each stage observable in a combat inspector. The interaction must emerge from common effect rules, not a script that checks for one named set of items.
- Add basic equipment, seeded affix rolls, rarity presentation, inventory, pickup, equip, comparison, and a debug loot table. Preserve item-instance identity and rolled values in saves.
- Add Resonance values and lineage tags to content, reserving full Manifestations and Concordances for the slice/production work.

**P1.6 — Combat feedback and profiling**

- Add readable attack anticipation, impacts, hit reactions, death feedback, audio signatures, floating text, and configurable camera shake.
- Expose frame/tick timing, active entities, projectiles, summons, statuses, trigger queue size, and allocation/GC activity in a development overlay.
- Add arena presets for dense melee packs, projectile-heavy builds, summon-heavy builds, and the fragment chain reaction.
- Add headless unit/interaction tests for combat ordering, resource/cooldown rejection, damage-over-time death, barrier/reflection behavior as introduced, and equip/unequip side effects. Turn discovered combat failures into replay fixtures.

### Deliverables and exit gate

- The arena supports repeated sessions with the complete six-slot starter loadout, dodge, potion, enemies, loot, and fragment swapping.
- Combat remains satisfying with progression bonuses disabled. Record playtest observations on responsiveness, target clarity, dodge readability, and hit feedback; resolve recurring problems before expanding content.
- At least two mechanically different loadouts can complete the same encounter without a mandatory fragment combination.
- The fire/spirit/poison interaction is discoverable and explainable from game feedback and item text; it remains bounded during a long stress encounter.
- Invalid actions cannot consume resources inconsistently or fire while dead/stunned/on cooldown. Deterministic replay and seed-based reproduction remain functional.
- Profiled results meet the Phase 0 reference budget, or the scope/budget is explicitly revised with measured evidence.

## 5. Phase 2 — Complete vertical slice

**Objective:** Deliver the complete Greyhaven → dungeon → Bell Saint → new fragment → replay loop described in the game design.

**Entry:** Combat and build interaction are strong enough to justify a polished environment and boss.

### Work packages

**P2.1 — Greyhaven, onboarding, and dungeon traversal**

- Build a compact Greyhaven with Mara Vey, an equipment service, an anchor, a dungeon entrance, and a clear return path. Introduce the opening injury/implant premise at slice scope without revealing Nhal's identity twist early.
- Teach movement, attack, dodge, potion, equipment, implantation, and mutations through short encounters and interactions.
- Create one authored dungeon leading to the Basilica of Last Mercy, with landmarks, variable pack placement, an optional discovery, and a shortcut/checkpoint structure.
- Add interaction prompts, objectives, quest state, a map/minimap sufficient for navigation, and a journal/lore entry path.
- Implement campaign death recovery: return to the latest anchor, reset nearby encounters and bosses, retain progression according to the documented checkpoint policy, and avoid default corpse runs.

**P2.2 — Match the slice's content inventory**

- Finish one playable discipline, approximately six active abilities, and several meaningful Mutations.
- Ship three normal enemy types and two specialist types with distinct encounter roles; reused visuals do not count as distinct behavior by themselves.
- Build one composable elite system with a small curated modifier set and incompatible-pair validation.
- Curate roughly twenty meaningful equipment combinations across offense, defense, resources, and fragment synergy. Maintain a reviewed loadout matrix; twenty randomly rolled items alone does not satisfy this requirement.
- Provide enough fragments to support at least two build directions, the systemic signature interaction, and a compelling Bell Saint reward.

**P2.3 — Bell Saint encounter**

- Implement an explicit encounter state machine, arena boundaries, phase transitions, reset, reward, and replay support.
- Phase one uses chains and sonic shockwaves. Phase two resurrects corpses while ritual anchors provide an actionable objective. Phase three releases the creature while bell fragments ring independently.
- Specify corpse ownership, anchor destruction, interrupt windows, transition immunity, safe space, and behavior when the player dies or leaves.
- Ensure melee, ranged effects, low-mobility defenses, and summon/fragment interactions have viable responses. Avoid dependence on one required skill or damage family.
- Award a unique Serath fragment that visibly opens a new build option; preserve the chance for a Godwrought relic with reproducible reward rolls.

**P2.4 — First Godwrought item and crafting path**

- Implement Ashcleaver with a per-instance progression record, burning-enemy kill tracking, attack-speed stacks, an awakening threshold, and awakened flame waves.
- Keep the design's 1,000-burning-kill requirement in the canonical definition unless design deliberately revises it. Use a clearly marked development override to exercise awakening within slice testing.
- Model the later Serath/Orrun evolution as explicit, persistent choices; at minimum prove one branch technically now and schedule both intended launch branches in Phase 3/5.
- Provide a small crafting flow, such as Tempering plus the relevant awakening/grafting service, with transaction checks, material costs, confirmation of permanent choices, and clear resulting behavior.

**P2.5 — Resonance, Manifestations, and build UI**

- Implement threshold selection and at least two contrasting Manifestations with both benefits and complications, such as Burning Blood and Stone Memory.
- Show a visible transformation and an NPC reaction tied to Resonance. Preserve a viable low-Resonance route rather than treating transformation as an obligatory upgrade.
- Implement inventory/equipment, all anatomy-slot UI, skill mutation selection, useful comparisons, stat explanations, loadout inspection, and clear unsupported combinations.
- Add a representative Concordance to prove tag-combination discovery and persistence without turning all fragment interactions into predefined sets.
- Give combat text, hazards, status indicators, cooldowns, and valuable drops distinct readable visual/audio treatment. Add text scaling, subtitles, remapping, and VFX/shake reduction foundations.

**P2.6 — Production-quality slice pipeline and saves**

- Establish one environment kit, one character/enemy rig workflow, modular equipment attachments, and manifestation presentation suitable for further content production.
- Document asset scale, axes, pivots, naming, collision, LOD/material limits, skeleton compatibility, and source-to-export-to-Godot import steps. Validate a representative asset through the full process.
- Author reusable VFX for telegraphs, projectiles, impacts, statuses, loot, and transformations. Use Godot audio with buses, priority, ducking, and recognizable critical combat cues.
- Expand saves to identity, progression, loadout, rolled items, fragments, Godwrought progress, quests, discoveries, Greyhaven, content/schema versions, and checkpoint state.
- Add backup recovery, corrupt/truncated save handling, one explicit migration fixture, unknown/removed content-ID policy, and clear incompatible-newer-save messaging.
- Extend content validation to loot reachability, invalid effect combinations, reference graphs, localization/asset references, and prohibited cycles. Do not blanket-ban legitimate finite recursive content.
- Add `aw item show`, `aw content refs`, `aw replay run`, and initial loot/combat simulation commands. Every command reports the content version used.

### Deliverables and exit gate

- A packaged build contains the complete slice inventory above and can be played from Greyhaven through all three Bell Saint phases, reward acquisition, return, rebuild, and dungeon replay.
- Equipment, mutations, fragment interactions, Resonance choice, and the Godwrought progression demonstration are visible to players, not only accessible through debug controls.
- Multiple external playtesters can understand telegraphs, defeat the boss with distinct loadouts, and identify what the reward changes. Record whether they voluntarily want another run; qualitative fun findings can block the gate even when tests pass.
- Saves restore before/after the boss, after death, after equipment changes, and after Godwrought/Manifestation choices without duplicated rewards or lost progression.
- The slice meets reference performance targets during the boss and strongest available chain reaction, including readability under reduced-effects settings.
- Production leads can estimate the effort to create another enemy, room, skill mutation, fragment, boss phase, and quest using the established process.

**Decision at the gate:** Proceed to full production only if both the combat and the build/replay loop work. Otherwise return to the relevant Phase 1 or 2 work package; more acts and loot will not resolve that failure.

## 6. Phase 3 — Production foundation

**Objective:** Turn the slice into a maintainable game framework and prove that the remaining disciplines and content can be authored without continual engine changes.

**Entry:** Slice fun, production cost, and technical feasibility are accepted.

### Work packages

**P3.1 — Complete discipline and skill foundations**

- Bring all five disciplines to a representative playable baseline. Vanguard uses Momentum; Veilwalker uses Exposure; Arcanist uses Instability; Gravecaller uses Remains; Warden uses Adaptation.
- Define generation, spending, decay/reset, cap behavior, UI, persistence, and anti-exploit rules for each resource. Include minion commands/limits, corpse consumption arbitration, companion behavior, and threat adaptation where applicable.
- Implement cross-discipline unlock/retraining rules consistent with disciplines establishing early identity without permanent confinement.
- Complete mastery, Mutation unlocks, passive choices, ultimate gating, and affordable respec. Validate weapon/loadout compatibility and mutation exclusions.
- Implement Fire Lance with Forking Flame, Furnace, Cauterize, and Living Flame, and complete Shield Breaker's planned variations. Use these to verify composition supports projectile branching, healing from damage, summons, charge, counters, and cooldown resets.

**P3.2 — Complete the shared systems vocabulary**

- Finish semantics and representative content for all damage families and statuses: Burning, Bleeding, Poisoned, Chilled, Frozen, Shocked, Staggered, Cursed, Terrified, Marked, Vulnerable, and Rooted.
- Support spreading, consuming, transforming, detonating, and benefiting from statuses. Test resistance/control behavior on bosses, attribution across summons/areas, conversion chains, immunities, and reflection/life-steal interactions.
- Complete fragment slot restrictions, lineage/Resonance tags, tag-based Concordances, suppression/restoration, and correct removal of derived attributes/triggers.
- Implement all four named Manifestations, including Whispering Shadow and Voracious Renewal, with accessible presentation and documented complication semantics. False enemies must not obscure required real telegraphs.
- Add representative mechanics for Heart of Vael, Serath's Last Memory, and Orrun's Knuckle. Elite ability capture requires a curated, validated player-usable ability contract rather than arbitrary enemy scripts.
- Make all twelve equipment slots functional: Head, Shoulders, Chest, Gloves, Belt, Legs, Boots, Amulet, two Rings, Main Hand, and Off Hand. Distinguish equipment Legs from anatomy Legs in data/UI.
- Support Common, Tempered, Rare, Relic, Legendary, and Godwrought rarity rules; conventional affixes, advanced behavior-changing affixes, eligibility, weights, and incompatibilities.
- Complete all crafting services: Tempering, Rebinding, Engraving, Extraction, Divine Grafting, and Purification. Validate cost/payment/result as one transaction; specify what is consumed and what persists.

**P3.3 — Progression and world-state framework**

- Finalize the level/XP curve and the role of character level, mastery, anatomy, equipment, Godwrought evolution, Greyhaven, and profile-wide discoveries. Each must have a distinct purpose and bounded currency requirements.
- Build declarative quest objectives, dialogue conditions, delayed consequences, faction/world flags, service unlocks, discoveries, and journal entries with stable IDs.
- Support Greyhaven rebuild stages and rescued/invested specialist unlocks through shared state rather than scene-specific conditions.
- Separate character state, shared local profile unlocks, preferences, and ephemeral encounter state. Specify what happens when profiles or saves reference missing content.
- Add difficulty configuration for aggression, composition, patterns, hazards, elite combinations, rewards, and Responses, with explicit build fairness rules.

**P3.4 — Scalable content and asset production**

- Add templates and preview scenes for skills, fragments, affixes, enemies, bosses, quests, dialogue, encounters, loot tables, and crafting recipes.
- Add reference browsing, dependency graphs, localization key checks, missing-asset reports, and safe development content reload where possible. Production runs use immutable bundles.
- Any future AI-assisted authoring must produce the same structured content and pass the same validation as human-authored content; it does not gain a separate route into runtime rules.
- Establish biome/environment kits, character silhouettes, manifestation attachment/layer rules, animation retargeting conventions, collision/LOD validation, and VFX/audio budgets.
- Keep localization strings outside logic; support pluralization/formatting, text expansion, font coverage, and pseudolocalization. Set actual language and voice scope from the content budget.
- Create a per-act content inventory and asset dependency plan, including optional areas, unique enemy behaviors, bosses, lore delivery, regional sound palettes, and cinematic needs.
- Measure production throughput and revise estimates; use reusable kits without making every region feel interchangeable.

**P3.5 — Robustness, balance tools, and release automation**

- Expand save migrations into a tested chain with historic fixtures, interrupted-write recovery, backup rotation, and reward/crafting idempotency at save boundaries.
- Add randomized/property tests for valid items, terminating loot generation, legal values, valid references, and bounded proc execution. Add headless encounter fixtures and named replay regressions.
- Expand `aw` with enemy benchmarks, build simulation, boss simulation, loot distribution reports, reference queries, and balance reports. Go orchestrates; .NET executes the shared game rules.
- Measure time-to-kill, damage taken, deaths, resource starvation, skill use, status/proc uptime, damage-source distribution, summon counts, and reward distributions. Track configuration and seed with every result.
- Add immutable build/content/asset manifests, platform export jobs, package smoke tests, crash context, and a diagnostic bundle containing recent events plus an optional replay.
- Use local structured diagnostics now; add privacy-conscious opt-in crash/telemetry upload only if needed. Introduce OpenTelemetry for relevant service/infrastructure work when it exists.

### Deliverables and exit gate

- Each discipline has a distinct playable resource loop and representative build, and all five can complete the slice encounter set.
- Designers can add a routine skill mutation, fragment, enemy pack, loot table, and quest through data/assets without changing Core. New mechanics can extend the effect vocabulary deliberately.
- All launch progression axes, equipment slots, rarity levels, crafting services, statuses, and anatomy rules have tested implementations or explicitly tracked content-only completion work.
- Content validation, migrations, deterministic replays, asset checks, and packaged build smoke tests run in CI. Expensive simulations and soak tests run on a scheduled validation job as appropriate.
- The team has a counted launch inventory and measured production rate. Campaign commitments fit an explicit capacity/budget plan before Phase 4 scales up.

## 7. Phase 4 — Campaign production

**Objective:** Deliver a cohesive five-act campaign with evolving build choices, exploration, narrative consequences, and a visibly changing Greyhaven.

**Entry:** Production workflows and the launch content inventory are stable enough to support multiple regions.

### Work packages

**P4.1 — Build a complete campaign path, then finish acts**

- First connect all five acts in greybox form with travel, anchors, primary objectives, bosses, rewards, and the ending. This exposes pacing and dependency problems before final art.
- Produce each act through the same stages: narrative/encounter brief → layout and objectives → playable enemies/bosses → progression/rewards → art/audio → accessibility/performance → save/quest regression review.
- Track acts as playable, content complete, and polished separately. An art-complete region with broken progression does not count as complete.

| Act | Required design payoff | Implementation focus |
|---|---|---|
| I — The Grey March | Opening attack, Mara's intervention, implantation, evidence against official history | Onboarding, Greyhaven services, basic exploration, anchor/death loop, early faction context |
| II — The Verdant Maw | Ilyra's ecosystem and attempted reconstruction | Biological enemies/hazards, poison/adaptation builds, Children of Ilyra choices, living environment kit |
| III — The Cinder Reach | Industrial extraction and its human benefits/costs | Vael effects, machinery/forge encounters, Cinder Compact, extraction consequences |
| IV — The Shattered Spine | Orrun's remains and the revelation that divine bodies are seals | Oathbound decisions, contracts, giants, defensive/seismic play, delayed world consequences |
| V — The Hollow Night | Nhal beyond the breach and the identity fragment revelation | Readable impossible spaces, memory/shadow mechanics, final encounters, varied endings with a shared endgame state |

**P4.2 — Populate encounters and exploration**

- Expand regional enemy families with pressure, support, control, ranged, armored, and disruptive roles. Include the Cinder Pack's Ash Ghoul, Cinder Priest, Furnace Brute, and Emberling in the appropriate regional inventory.
- Complete the elite modifier set: Mirrorborn, Gravewake, Stormbound, Devourer, Null, Hunter, Martyr, and Riftborn. Validate combinations against unavoidable damage, unreadable telegraphs, repeated resurrection, fragment suppression, and runaway entity creation.
- Build authored landmarks with seeded encounter variation, optional chambers, mini-dungeons, shrines/tradeoffs, rare merchants, trapped travelers, faction conflicts, and lore discoveries.
- Implement Resonance Storms as scoped changes to population, hazards, rewards, and fragments, with correct cleanup when they expire or the player leaves.
- Implement Divine Memories as bounded encounter contexts with special rules and safe return to normal world state. Implement Wake Hunts with tracking, named behavior, and unique rewards.
- Add high-difficulty Responses to common tactics; telegraph their counters and verify that a player can adapt without replacing the entire build.

**P4.3 — Greyhaven, factions, and narrative consequences**

- Deliver the planned hub progression for Mara, Torren, Sister Cael, Oris, Kesh, and optional Pale Child content according to the launch inventory.
- Represent the Reliquary Church, Anatomists, Cinder Compact, Children of Ilyra, Oathbound, and Quiet through dialogue, quests, encounters, and consequential choices.
- Change dialogue and visible hub state based on campaign events, rescues, investments, choices, and visible Resonance. Avoid a single morality score.
- Implement the designed extraction, fragment ownership, oath, and transformation dilemmas with explicit immediate and delayed consequences.
- Cover ending variations in alliances, surviving leaders, Greyhaven condition, and relationship to Resonance while guaranteeing a valid transition into the same endgame foundation.
- Deliver lore through spaces, NPC interaction, events, and Divine Memories as well as journal text. Retain friendship, humor, and ordinary life alongside horror.

**P4.4 — Progression, rewards, and presentation**

- Pace XP, mastery, mutation access, fragment lineages, rare affixes, Godwrought milestones, crafting services, and material sources across acts.
- Establish build checkpoints at each act boundary for all five disciplines and both low-/high-Resonance paths. Check recovery from unlucky drops and affordable respec.
- Make rare materials primarily rewards for bosses, hunts, exploration, valuable-item dismantling, and difficult encounters; audit currency and chore growth.
- Apply each god's art and sound language while keeping enemy silhouettes, navigation, hazards, and telegraphs readable. Separate decorative impossible geometry from unambiguous combat collision.
- Complete controller inventory/skill/anatomy/crafting flows, map/journal navigation, subtitles, remapping, text scale, contrast, color-independent cues, hold/toggle options, and VFX/flash/camera controls within the supported accessibility scope.

### Deliverables and exit gate

- A new character in each discipline can complete all five acts, reach a valid ending, and enter the endgame; critical quests, travel, rewards, and hub services do not softlock.
- All committed campaign content is present, including optional content needed for promised services, builds, and narrative consequences.
- Difficulty escalates through mechanics and encounter composition. Bosses have readable responses and no unintended mandatory build solution.
- Save/load, death, revisiting earlier regions, choice permutations, and all supported input methods work at act boundaries and major state changes.
- Greyhaven and NPC reactions reflect the tested story/Resonance state, including delayed consequences and ending variations.
- Representative players can follow the central mystery and distinguish the regions without relying solely on collectible exposition.

## 8. Phase 5 — Endgame and systemic balance

**Objective:** Make continued play rewarding through controlled variability, mastery, and evolving builds, without mandatory chores.

**Entry:** The production systems exist; campaign progression, ending, and reward contracts are sufficiently stable. Prototype work may overlap late Phase 4.

### Work packages

**P5.1 — Fracture expeditions**

- Define Fracture Sigils with region, difficulty, modifiers, boss family, reward tendencies, and generation seed.
- Generate expeditions from authored spaces and validated encounter/rule pools. Preserve a reproducible manifest so failures and reward rolls can be diagnosed.
- Implement representative rules from the design: burning enemies gain speed, healing creates hostile echoes, dead elites leave hazards, resistance tradeoffs, fragment overcharge, and bosses inheriting prior elite modifiers.
- Define modifier stacking/exclusions, entry costs, limited attempts/reward penalties, success/failure, quitting, resuming, and save boundaries. Avoid reroll/reload reward duplication.
- Expose the rules and reward tendencies before commitment; track progress and outcome clearly throughout the run.

**P5.2 — God Hunts and Godwrought completion**

- Build one complete God Hunt to validate the encounter format, material reward, and Godwrought evolution loop before producing the remaining budgeted hunts.
- Use The False Vael, Ilyra Reborn in Teeth, The Thousand Memories of Serath, and Orrun Without an Oath as the named launch candidates; Phase 3's content inventory decides the committed set. Keep the secret Nhal reconstruction hunt explicitly optional unless committed.
- Give hunts distinct mechanics, phases, and counterplay rather than only larger statistics. Validate performance under large-boss VFX and advanced player builds.
- Complete intended Godwrought evolution branches, including Ashcleaver's Serath/Orrun choice if retained, their material sinks, permanent-choice UI, save migrations, and behavior-changing rewards.

**P5.3 — Build, loot, and difficulty balance**

- Maintain a reference matrix across five disciplines, multiple damage families, melee/ranged/summon builds, status/conversion builds, and low/high Resonance.
- Run headless encounter/boss/loot simulations over fixed seed sets; inspect damage-source contributions, survival, resource uptime, proc fan-out, reward distributions, and outliers.
- Use human playtests to assess agency, readability, fatigue, discovery, and whether stronger builds remain actively operated. Automated win rate alone cannot determine balance.
- Audit item usefulness, compare meaningful upgrade choices, reduce meaningless drops, and provide loot filtering/inspection options suited to endgame volume.
- Tune material/recipe access, crafting costs, awakening goals, and optional-content attempts so good equipment is attainable while perfection remains long-term.
- Complete campaign-finish profile unlocks for eligible recipes, lore, cosmetics, and conveniences without granting new characters enough power to erase early progression.

### Deliverables and exit gate

- Campaign completion unlocks Fractures and the committed God Hunt progression without a mandatory account or backend.
- Sigil generation is valid, bounded, reproducible, and free of prohibited modifier combinations; every expedition terminates in a defined recoverable outcome.
- Multiple build archetypes and low/high Resonance choices can complete their intended difficulty tiers, with known balance exceptions documented and addressed.
- Godwrought evolution, profile unlocks, expedition attempts, and rewards persist without duplication or accidental loss.
- Worst-case chain, summon, projectile, and boss combinations stay within agreed simulation and presentation budgets.
- The endgame offers meaningful new interactions and goals without requiring daily attendance or seasonal resets.

## 9. Phase 6 — Release hardening and single-player launch

**Objective:** Ship a complete, supportable single-player game and a safe update path.

**Entry:** Campaign and endgame content are complete. Remaining work is balance, usability, defects, compatibility, performance, and release preparation.

### Work packages

**P6.1 — Compatibility, recovery, and quality**

- Test the declared OS/GPU/input matrix using exported release builds. Cover fullscreen/windowed modes, resolution changes, focus loss, pause/resume, controller reconnect, and graphics/accessibility settings.
- Exercise every supported save migration from maintained fixtures; interrupted writes, full disk, malformed files, missing content, profile conflicts, and backup restoration must have clear outcomes.
- Run fresh-character and migrated-character campaign completions, ending/endgame transitions, all discipline resource loops, Godwrought branches, and crafting/equip stress checks.
- Finish localization and accessibility reviews, including expanded text, subtitles, UI focus, color-independent telegraphs, and reduced-effects legibility.
- Audit external asset licenses, credits, packaging requirements, install/update behavior, and platform integration required by the selected launch targets.

**P6.2 — Performance and stability**

- Profile target hardware on dense packs, worst VFX, projectiles, summons, chained effects, hub transitions, loading, and long play sessions.
- Fix measured CPU/GPU/memory/loading bottlenecks; verify bounded allocations, entity cleanup, effect lifetimes, and no persistent tick backlog.
- Add pooling, spatial indexing, packed populations, LOD/culling, or native extensions only for demonstrated bottlenecks. Preserve correctness and replay checks through optimization.
- Run extended soak/replay suites and stress saves while collecting build/content identity and actionable crash context.

**P6.3 — Release pipeline and support readiness**

- Produce immutable, reproducible release artifacts tied to exact code/content/assets. Verify signing and distribution requirements for chosen platforms.
- Maintain a release checklist, known-issues list, crash triage process, backup/migration support instructions, and a patch validation process.
- Test updating from the prior supported build without losing saves. Document that executable rollback does not imply safe save downgrading; retain recoverable backups and declare compatibility boundaries.
- Run a release-candidate playthrough and smoke suite on the actual distribution artifact, then freeze and tag the accepted build.

### Deliverables and exit gate

- No unresolved release-blocking crashes, save loss/corruption, quest softlocks, reward duplication, or unsupported required control paths remain in the tested matrix.
- Campaign, endgame, discovery, crafting, and build progression meet the accepted scope and quality bar.
- Performance meets the agreed hardware-specific targets; stability, migration, localization, accessibility, and package checks pass.
- A release package, recoverable saves, diagnostics, patch procedure, and support documentation are ready. Single-player launch is complete independently of Phase 7.

## 10. Phase 7 — Optional cooperative expansion

**Objective:** Reuse the proven simulation in authoritative co-op without moving combat authority into Go or weakening single-player play.

**Entry:** The single-player release is strong, and cooperative development has its own scope/capacity approval. This phase is not a prerequisite for launch.

### Work packages

**P7.1 — Two-player design and authority contract**

- Decide party entry/exit, host/server ownership, character provenance, loot ownership, quest/choice ownership, difficulty scaling, pause behavior, death/revival, disconnects, reconnects, and expedition rewards.
- Create `Ashenwake.Server` using the same C# Core, content bundles, and authoritative spatial implementation. Clients send commands/intent; the server validates movement, resources, cooldowns, target legality, damage, loot, and RNG.
- Version network DTOs independently from domain objects; define protocol/content compatibility and reject incompatible sessions before gameplay.
- Select transport after measuring requirements for latency, reliability, ordering, bandwidth, and deployment, rather than deciding it in Phase 0.

**P7.2 — Networking prototype**

- Run two clients through the slice dungeon and Bell Saint on a dedicated server, initially on a local network.
- Add snapshots/deltas, interpolation, prediction/reconciliation, command sequence/tick handling, reconnect state, and resynchronization diagnostics.
- Test latency, jitter, loss, duplication, reordering, reconnects, and server restarts. Validate that cosmetic prediction cannot grant authoritative rewards or bypass cooldowns.
- Extend determinism/replay tooling with server-side command logs and state snapshots to reproduce online failures.

**P7.3 — Minimal persistent services**

- Build a Go modular monolith for only the prototype's necessary account/session integration, character metadata/inventory persistence, parties/lobbies, matchmaking, allocation, and telemetry/admin support.
- Use PostgreSQL migrations, transactional persistence, idempotent reward/character writes, ownership checks, secure sessions, rate limits, and structured audit diagnostics.
- Use HTTP/JSON for control-plane APIs initially. Keep real-time combat in the C# server. Introduce Redis only for a measured coordination/cache/session need.
- Preserve offline character support. If online progression must be trusted, keep its ownership and import policy separate from locally editable saves.

**P7.4 — Operations and production decision**

- Containerize the server and services, add health/readiness checks, graceful draining, crash recovery, load/soak tests, and backup/restore exercises.
- Introduce AWS infrastructure only when remote testing requires it; use Terraform/OpenTofu and start with an operationally modest deployment such as ECS if appropriate.
- Add OpenTelemetry and metrics for server tick overruns, latency, reconnects, API errors, database latency, session allocation, and reward persistence failures.
- Estimate operating cost and support burden from measured session density and load. Do not introduce Kubernetes, microservices, a commercial anti-cheat system, or an online economy without a demonstrated requirement.

### Deliverables and exit gate

- Two clients complete the slice under the accepted network impairment profile with consistent combat, loot, quest state, and recovery.
- Manipulated client commands cannot award damage, items, or resources or bypass movement/cooldown rules.
- Disconnect/reconnect and server failures have tested persistence and recovery behavior, without duplicate valuable outcomes.
- Load/soak results, operating cost, privacy/security requirements, and cooperative design changes support a separate decision on production expansion. A successful prototype alone is not a production co-op release.

## 11. Phase 8 — Optional seasons and permanent experiments

**Objective:** Add new interactions sustainably, without turning the game into a schedule of obligations.

- Select one bounded experiment, such as Echoes, Broken Oaths, or Hunger, and define how it interacts with existing builds, saves, rewards, and accessibility settings.
- Prefer the existing content/effect pipeline; add feature flags and migrations only where needed. Avoid building a live-service platform in anticipation of demand.
- Playtest novelty, balance, participation burden, and the path into permanent content. Define retirement or migration behavior before distributing temporary content IDs.
- Release only after the base game and, if relevant, online services can be maintained at the required cadence. Multiplayer is not a prerequisite for a single-player content expansion.

**Gate:** The experiment creates worthwhile build decisions, preserves existing characters, and has a sustainable support/content plan. Seasonal monetization, competitive resets, and mandatory daily systems are not assumed by this plan.

## 12. Cross-phase validation and performance plan

### 12.1 Test layers

| Layer | Required coverage | Introduced / expanded |
|---|---|---|
| Core unit tests | Damage ordering, resources, cooldowns, resistance, status semantics, progression, item eligibility | P0 / P1–P3 |
| Interaction tests | Fragment chains, summons and poison, corpse arbitration, barriers/reflection, death-once, equip cleanup, Manifestations | P1 / P2–P5 |
| Content/property checks | Stable IDs, valid references, ranges, localization/assets, loot termination, effect compatibility, bounded generation | P0 / P2–P3 |
| Headless encounter tests | Repeatable builds, boss phases, reset/rewards, difficulty, Fracture modifier sets | P1 / P2–P5 |
| Replay regressions | Seeded outcome/state hashes, first-divergence diagnosis, preserved historical bug cases | P0 onward |
| Save/migration tests | Round trips, backups, interrupted writes, version changes, removed IDs, progression and reward integrity | P0 / P2–P6 |
| Presentation/package checks | Input, animations/events, navigation/UI, audio/VFX readability, imports/exports, actual release artifacts | P0 onward |
| Playtests | Combat feel, discovery, reward value, narrative comprehension, fairness, accessibility, replay interest | P1 onward |
| Online tests | Protocol compatibility, authority, adverse network conditions, persistence, recovery, load, abuse | P7 only |

Keep fast deterministic checks in pull requests. Run expensive simulations, large asset checks, long replays, and soak tests in a scheduled or release pipeline. Simulations compare against declared seeds/content versions and report distributions; do not approve balance changes from a single average.

### 12.2 Proposed initial performance targets

These are starting engineering targets, not requirements already established by the source documents. Phase 0 must record reference hardware, resolution, build configuration, capture method, and representative population sizes before treating them as acceptance limits.

| Area | Starting target / measurement policy |
|---|---|
| Rendering | Target 60 FPS on the selected baseline; track CPU and GPU frame-time distributions against a 16.7 ms budget, including p95/p99 spikes |
| Simulation | Fixed 30 Hz gives 33.3 ms per tick; target p99 simulation work below 25 ms in the agreed stress population, with no sustained backlog |
| Input responsiveness | Capture command-to-visible-response and command-to-authoritative-action timing; ability windup must be intentional and device/controller results reviewed |
| Population limits | Set explicit tested limits for enemies, projectiles, summons, areas, active statuses, proc events, and navigation requests; measure both plausible peak and pathological chains |
| Visual load | Budget particles, transparent overdraw, dynamic lights, decals, draw calls, and telegraph priority per encounter; reduced-effects mode preserves mechanics |
| Memory and loading | Establish RAM/VRAM, loading, save-size, and main-thread save-stall budgets on the reference platform during the slice; long runs must not grow without bound |
| Reproducibility | Identical commands/seeds/content reproduce tested outcomes within the documented platform/runtime envelope; incompatible replays are rejected explicitly |
| Online | Set RTT/loss/jitter, per-client bandwidth, server density, and service recovery targets during P7 from the accepted cooperative experience |

Retain benchmark scenes and machine/build metadata with results. Lowering content density, simplifying VFX, and fixing algorithms are valid responses to measured limits; adding C++ is one option only after identifying a suitable hotspot.

## 13. Main risks and concrete mitigations

| Risk | Early evidence | Mitigation / decision point |
|---|---|---|
| Interactions become infinite or impossible to explain | Proc queue growth, excessive summons, inconsistent kill attribution | Causal trigger records, per-effect policies, runtime budgets, stress replays; block P1/P2 exit until bounded and legible |
| Godot hides authoritative state | Headless replay differs or cannot run without scene physics | Shared spatial model, Core-owned timing/movement, exported geometry, reproducibility gate in P0 |
| Fixed ticks make input feel sluggish | Measured input delay and repeated playtest complaints | Input buffering and immediate cosmetic response; tune simulation frequency only with measurements and unchanged authority rules |
| Scope overwhelms production capacity | Slice assets/bosses take longer than the content budget supports | Count content after P2, measure throughput, reuse kits, revise scope before committing to full campaign production |
| Five disciplines demand incompatible special cases | New resource/minion systems bypass common effects | Prove representative loops in P3, extend shared contracts deliberately, keep custom exceptions isolated |
| Visual transformations explode asset cost | Armor clips or requires bespoke variants for every fragment | Modular attachment/layer rules and silhouette tests in the slice; budget supported transformations before mass asset work |
| Save/content evolution loses progression | Missing IDs, broken migrations, duplicated rewards | Stable IDs, redirect/tombstone policy, historical fixtures, atomic writes/backups, transactional reward state |
| Powerful builds destroy readability or performance | Players cannot see telegraphs; frame/tick spikes | Telegraph priority, effect caps/LOD, reduced-effects modes, chain/summon/projectile stress suites |
| Resonance becomes mandatory or punitive | Low-Resonance builds fail or tradeoffs feel arbitrary | Compare low/high paths at act and endgame checkpoints; retune complications and alternative progression |
| Narrative choices multiply untestable states | Softlocks or contradictory hub/ending conditions | Declarative state, bounded branch budgets, automated critical-path coverage plus narrative review |
| Loot/crafting becomes busywork | Most drops ignored; too many currencies; excessive inventory time | Curated useful affixes, reward distribution analysis, loot filtering, currency audit, material rewards from active play |
| Online rewrites or delays the game | Backend dependencies appear in single-player progression | Keep Core reusable but offline-first; enforce P7 entry gate and separate trusted-online persistence policy |

## 14. Requirements coverage

The table maps the source specifications to implementation work so major design promises do not disappear between milestones. Section numbers refer to the two linked source documents.

| Source requirements | Primary delivery |
|---|---|
| Game §§1–4: fantasy, pillars, pantheon, central mystery | P1 combat/build gate; P2 onboarding; P4 regional/narrative production |
| Game §5: five starting disciplines | P1 first discipline; P3 all five resource/skill foundations; P4/P5 progression and balance |
| Game §6: loadout and behavioral Mutations | P1 six-slot loadout; P2 slice mutations; P3 complete skill/mutation contracts |
| Game §§7–8: anatomy, systemic fragments, Concordances, Resonance/Manifestations | P1 interaction proof; P2 choice/UI/visual proof; P3 breadth; P4/P5 viability |
| Game §9: equipment, six rarities, Godwrought evolution | P1 loot; P2 Ashcleaver; P3 full equipment/affix rules; P5 evolution completion |
| Game §§10–11: damage/statuses, enemies/elites/bosses | P1 rules and roles; P2 Bell Saint; P3 complete vocabulary; P4/P5 encounter breadth |
| Game §§12–14: five acts, factions, Greyhaven | P2 hub/quest slice; P3 state framework; P4 campaign and reactions |
| Game §15: six crafting/modification services | P2 first service; P3 complete mechanics; P4/P5 economy and material sources |
| Game §16: exploration, Storms, Memories, Wake Hunts | P2 discovery prototype; P4 production |
| Game §§17–19: progression, difficulty, death/recovery | P2 anchors/death; P3 framework; P4 pacing/Responses; P5 profile unlocks and attempts |
| Game §§20–21: Fractures, Sigils, God Hunts | P5 expedition, hunt, reward, and balance work |
| Game §22: optional seasons | P8, after an explicit sustainability decision |
| Game §23: narrative choices and ending variations | P3 world-state framework; P4 consequence coverage and ending validation |
| Game §§24–25: mythic decay, regional art/music, readable feedback | P1 combat feedback; P2 production slice; P4 regional execution; P6 QA |
| Game §26: exact first playable slice | P2 content inventory and replay gate |
| Game §§27–29: avoid chores/noise, desired player story, identity | P1/P2 fun gates; P4/P5 loot, fairness, agency, and discovery reviews |
| Architecture §§1–6, 11–12: stack, boundaries, ticks, data model, events, bridge, AI | P0 contracts; P1 complete sandbox systems; P3 scaling |
| Architecture §§7–10: combat rules, content, `aw`, tests/balance | P0 validation; P1 rules; P2/P3 authoring/tooling; P5 balance |
| Architecture §13: logical saves and migrations | P0 round trip; P2 complete slice state; P3 robustness; P6 compatibility |
| Architecture §§14–17, 21: multiplayer, Go, storage/protocols, security, AWS | P7 only, except reusable Core/command boundaries established in P0 |
| Architecture §§18–19: assets, animation, VFX, audio | P1 prototypes; P2 conventions; P3 scalable pipeline; P4 production |
| Architecture §§20, 22: observability, CI/release, performance | P0 baseline; P1 profiling; P3 automation; P6 hardening; P7 service telemetry |
| Architecture §§23–27: development phases, deferred decisions, non-negotiables, first milestone, final architecture | This roadmap's gates, especially P0, P2, P6, and optional P7 |

## 15. First executable backlog

Create these work items first; defer bulk campaign tickets until the slice has produced reliable estimates.

| Order / ID | Concrete result | Depends on | Acceptance evidence |
|---|---|---|---|
| 1 / P0-A | Decision records for initial platform, input, toolchain, authority, and determinism | None | Reviewed choices and reproducible environment notes |
| 2 / P0-B | Core/client/tests/tooling projects and minimal CI | P0-A | Clean build, Core has no Godot references, blank client exports |
| 3 / P0-C | Tick loop, entity IDs, commands, state/events, RNG streams | P0-B | Fixed-seed headless test and tick/state inspection |
| 4 / P0-D | Minimal shared geometry, movement, and hit query | P0-C | Headless movement/hit tests and stable query ordering |
| 5 / P0-E | Schemas, runtime bundle, first content, `aw content validate` | P0-B | Valid bundle builds; bad IDs/ranges/references fail clearly |
| 6 / P0-F | Ability → damage → fragment/status → death → loot | P0-C, P0-D, P0-E | Expected state/events and reproducible loot |
| 7 / P0-G | Godot input/event/interpolation bridge and one-room scene | P0-D, P0-F | Playable interaction without scene-owned damage rules |
| 8 / P0-H | Replay capture, state hashing, and divergence report | P0-C, P0-F, P0-G | Same replay agrees headlessly and through client-driven simulation |
| 9 / P0-I | Versioned minimal save, atomic replacement, backup | P0-E, P0-F | Round-trip identity/item/fragment/RNG state and recovery check |
| 10 / P0-J | Packaged milestone build, profile baseline, gate review | P0-G, P0-H, P0-I | Demonstrated full loop and a measured, estimated Phase 1 backlog |

After this backlog passes, prioritize the Phase 1 movement/attack/dodge loop and combat-rule document, followed by enemy tells, six abilities, and the fragment chain. The next investment decision is whether those systems make the small arena worth playing repeatedly.
