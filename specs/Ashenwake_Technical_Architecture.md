# Ashenwake — Technical Architecture and Recommended Stack

**Purpose:** Define a practical technical foundation for a systems-heavy isometric ARPG.

> **Architectural rule:** `Ashenwake.Core` owns game truth. Godot presents it. Content describes it. Services persist and coordinate it.

## 1. Executive Stack

| Area | Choice |
|---|---|
| Engine | Godot 4.x |
| Game/client language | C# / .NET supported by Godot |
| Native optimization | C++ via GDExtension, only after profiling |
| Core simulation | Engine-independent C# library |
| Rendering | Godot scenes, animation, particles, shaders |
| Content | Versioned YAML/JSON; validated/compiled runtime data |
| 3D | Blender |
| Materials | Substance Painter/Designer or equivalent |
| Audio | Godot initially; FMOD when justified |
| Backend | Go |
| Database | PostgreSQL |
| Cache | Redis only where justified |
| Multiplayer | Authoritative dedicated simulation server |
| Source control | Git + Git LFS |
| CI/CD | GitHub Actions |
| Containers / cloud | Docker / AWS |
| Observability | OpenTelemetry, structured logs, metrics |
| Infrastructure | Terraform/OpenTofu |
| Developer CLI | `aw`, preferably Go |

The important decision is keeping game-domain rules independent of presentation and infrastructure.

## 2. Godot + C#

Godot should own scenes, models, cameras, lights, animation, particles, audio, UI, input, navigation, rendering, and platform integration. It should generally **not** own canonical character stats, item rules, damage calculations, status semantics, loot, skills, progression, or authoritative combat state.

Use C# for production game systems. Ashenwake will have a large interacting domain: abilities, effects, affixes, loot, Divine Fragments, AI, progression, serialization, replay, and networking. Static typing and mature refactoring/testing are worth it.

Use GDScript selectively for prototypes, editor helpers, and small presentation scripts.

## 3. Repository

```text
ashenwake/
├── game/
│   ├── Ashenwake.Core/
│   │   ├── Combat/ Entities/ Effects/ Items/ Loot/
│   │   ├── Skills/ Fragments/ AI/ Progression/
│   │   └── Simulation/ Serialization/ Events/
│   ├── Ashenwake.Client/
│   ├── Ashenwake.Server/
│   └── Ashenwake.Tests/
├── content/
├── services/
├── tools/
├── assets/
├── docs/
├── infra/
└── .github/
```

`Ashenwake.Core` must not depend on Godot. That enables headless tests, server execution, balance simulation, replay, benchmarking, fuzzing, and tooling.

## 4. Fixed-Timestep Simulation

Start near **30 simulation ticks/sec**, interpolating presentation independently.

```text
commands → validation → movement → abilities → spatial queries
         → damage → effects → AI → death → loot → events
```

Use seeded RNG, explicit simulation clocks, stable ordering where order matters, no wall-clock calls in rules, deterministic loot, and explicit spatial-query boundaries. Separate RNG streams for combat, loot, AI, and encounters so one new random AI decision cannot change a later loot roll.


## 5. Entity/System Model

Do **not** start by writing a giant general-purpose ECS. Begin data-oriented:

```text
EntityId, Transform, Velocity, Health, Resource, Faction,
Attributes, Resistance, AbilityState, StatusEffects, AIState, Target
```

Systems operate over relevant data: Movement, Ability, Damage, Status, Projectile, AI, Death, and Loot. Godot nodes can represent entities visually but are not authoritative. Optimize packed projectile/effect populations only after profiling.

**Profile before architecture cosplay.**

## 6. Semantic Events

Core emits events such as `EntitySpawned`, `AbilityStarted`, `DamageApplied`, `CriticalHit`, `StatusApplied`, `EntityKilled`, `LootDropped`, `FragmentTriggered`, and `BossPhaseChanged`.

The client consumes them for animation, VFX, audio, camera, UI, floating text, achievements, and telemetry. This separates **what happened** from **how it looked**.

## 7. Combat Pipeline

```text
Attack Created
 → Base Damage
 → Skill Modifiers
 → Attacker Modifiers
 → Critical Resolution
 → Damage Conversion
 → Target Defense
 → Resistance
 → Vulnerability
 → Barrier
 → Health
 → On-Hit Triggers
 → On-Damage Triggers
 → Death Check
```

Ordering is specification. Reflected damage/life steal, proc-on-proc behavior, barrier semantics, immunity, and conversion chains require deliberate rules and regression tests.

Build a shared effect vocabulary: `ApplyDamage`, `ApplyStatus`, `ModifyAttribute`, `SpawnEntity`, `CreateArea`, `Teleport`, `Knockback`, `GrantBarrier`, `Heal`, `ConsumeResource`, `GenerateResource`, `TransformDamage`, `TriggerAbility`.

Most content should compose reusable mechanics; custom Legendary code is the exception.

## 8. Data-Driven Content

Author content as human-readable structured data:

```yaml
id: godsunder
type: weapon
subtype: greatsword
rarity: legendary
damage:
  physical: { min: 42, max: 61 }
affixes: [strength, critical_damage]
legendary:
  effect: divine_rupture
  magnitude: 0.35
tags: [vael, flame, two_handed]
```

Validation catches duplicate IDs, unknown references, invalid ranges, cycles, impossible loot tables, invalid effect combinations, missing localization keys, and unreachable content. Compile source data into a compact runtime representation.

Stable IDs should look like `skill.fire_lance`, `item.ashcleaver`, `fragment.vael.eye_01`, `enemy.cinder_priest`, `effect.burning`. Saves persist IDs plus schema/content versions, never display names.

## 9. The `aw` Toolchain

```bash
aw content validate
aw item show ashcleaver
aw loot simulate --table cinder-reach-t4 --runs 100000
aw combat simulate builds/fire-spirit.yaml bosses/bell-saint.yaml
aw enemy benchmark cinder_priest --count 500
aw replay run bug-1842.awr
aw content refs fragment.vael.eye_01
aw balance report
```

Future AI assistance sits **above** the content model and generates/query structured content. AI output must pass the same schemas and validation as human-authored content.


## 10. Testing and Balance

Use unusually strong automated testing for a game:

- **Unit:** damage, resistance, statuses, resources, cooldowns, affixes, loot, fragments, conversions.
- **Interaction:** Burning + Serath Heart, poison + summons, barriers + reflection, corpse/death triggers.
- **Headless encounter:** run builds against scripted encounters and assert outcomes.
- **Property:** generated items satisfy schemas, loot terminates, calculations remain valid, references resolve.
- **Replay regression:** difficult combat bugs become replay tests.

Headless Core enables balance simulations:

```bash
aw balance builds --level 50 --runs 10000
aw loot simulate --hours 1000
aw boss simulate bell-saint --build-set standard
```

Measure TTK, incoming damage, deaths, resources, proc/status uptime, skill use, and drop distributions. Simulation does not replace playtesting; it catches numerical stupidity first.

## 11. Godot Client Boundary

A bridge owns the simulation `EntityId`, `Node3D`, model, animation controller, VFX/audio anchors, and interpolation state.

Presentation may lie cosmetically—interpolate movement, exaggerate knockback, delay a death effect slightly, spawn debris, shake the camera—but cannot alter authoritative outcomes.

Use Godot physics/navigation where useful while keeping combat rules explicit. A sword can perform a defined shape query; projectiles can be true simulation objects, ray/shape queries, or visual hybrids.

## 12. Enemy AI

Use layered inexpensive behavior:

```text
Perception → Intent → Behavior State → Ability Selection → Action Command
```

Common states: Idle, Acquire, Approach, Attack, Reposition, Flee, Recover, Special, Dead. Bosses use explicit encounter state machines. Stagger expensive decisions across ticks for large packs.

Cinder Ghoul #1837 does not need graduate-level reasoning before becoming paste.

## 13. Save Architecture

Persist logical state, not Godot scenes: identity, progression, equipment, inventory, fragments, quests, world flags, discoveries, Greyhaven, content version, schema version.

Use explicit migrations:

```text
Save v3 → migration → v4 → migration → v5
```

Writes should be atomic with backups. Save compatibility needs automated tests.

## 14. Multiplayer

Do not build multiplayer first. Preserve the option, prove single-player, then add co-op.

```text
Client A ─┐
          ├── Authoritative Game Server ── Persistent Services
Client B ─┘
```

Clients send intent. The server owns combat state, RNG, loot, AI, and authoritative positions. Clients predict/interpolate and reconcile.

The game server should host the same C# simulation model as Core. The Go backend should **not** become the real-time combat simulator.


## 15. Go Backend

Go handles authentication/session integration, accounts, character metadata, durable inventory, matchmaking, parties/lobbies, leaderboards, entitlements, social features, telemetry ingestion, server allocation, and admin APIs.

Start as a **modular monolith**, not twelve microservices:

```text
services/
├── cmd/ashenwake-api/
├── internal/
│   ├── account/
│   ├── character/
│   ├── matchmaking/
│   ├── leaderboard/
│   ├── telemetry/
│   └── persistence/
└── migrations/
```

Split only when scaling, ownership, deployment isolation, or reliability provides a concrete reason. A distributed system is not an achievement badge.

## 16. PostgreSQL, Redis, Protocols

PostgreSQL is the durable store. Prefer boring relational modeling over premature datastore novelty.

Redis is optional for short-lived sessions, coordination, rate limiting, or demonstrated hot caches. Do not mirror PostgreSQL into Redis because Redis exists.

Use HTTP/JSON for control-plane APIs initially. Use WebSockets or a purpose-built binary transport where persistent real-time traffic requires it. Network DTOs are versioned and separate from domain objects.

## 17. Security and Anti-Cheat

For single-player, do not spend months preventing players from modifying their own game.

For authoritative online play: never trust client damage or loot; validate resources, cooldowns, and movement; secure sessions; rate-limit APIs; log impossible transitions; keep valuable persistent outcomes server-owned. Anti-cheat work should be threat-driven.

## 18. Asset Pipeline

Use Blender as the default 3D DCC. Establish conventions for scale, axes, pivots, skeletons, naming, collision, LODs, materials, and export.

```text
Editable Source → DCC Validation → Export → Godot Import → Runtime Asset
```

Keep editable source assets separate from generated/imported files. Use Git LFS for large binaries. Substance Painter/Designer is a strong PBR workflow if available.

## 19. Animation, VFX, Audio

Do not bury combat rules in animation state machines. Animation may emit markers such as `impact_window`; Core determines outcomes. Use root motion deliberately.

Create a reusable VFX vocabulary: impacts, trails, telegraphs, statuses, projectiles, deaths, manifestations, loot beams, environmental Resonance. Establish performance budgets for overdraw, particles, lights, and decals.

An ARPG can make a GPU cry using nothing but transparent particles and optimism.

Start with Godot audio. Introduce FMOD only when adaptive music, sophisticated mixing, event authoring, or audio-team workflow justifies it.


## 20. Observability and CI/CD

Structured logs should carry session, encounter, entity, build/content version, and simulation tick IDs where relevant.

Track frame time, simulation tick duration, entity/effect counts, AI time, latency, server tick overruns, API latency/errors, and matchmaking duration. Use OpenTelemetry where appropriate.

Crash reports should include build ID, platform, content version, recent simulation events/ticks, and ideally replay information.

A mature PR pipeline:

```text
format/lint
 → C# compile
 → Go compile
 → unit tests
 → content validation
 → interaction tests
 → selected headless simulations
 → asset/reference validation
 → package smoke test
```

Nightlies run expensive balance simulations, soak tests, large content validation, server load tests, and replay suites. Release builds get immutable IDs tying code to exact content/assets.

## 21. AWS Deployment

Do not deploy meaningful cloud infrastructure until needed.

Later:

```text
Internet
   ↓
API / Edge
   ↓
Go Services ───── PostgreSQL
   │
   ├── Redis
   └── Game Server Allocator
              ↓
      Dedicated Game Servers
```

Containerize services and game servers. ECS is a reasonable starting point; Kubernetes requires an actual operational reason. Use Terraform/OpenTofu so infrastructure is reproducible rather than console archaeology.

## 22. Performance Budgets

Track CPU frame time, GPU time, simulation tick time, draw calls, visible enemies, projectiles, effects, particles, navigation queries, memory, load time, save size, and network bandwidth.

Create intentional stress scenes: maximum plausible pack, worst VFX combination, projectile-heavy build, summon-heavy build, and pathological chain-reaction build.

Optimize measured hotspots. GDExtension/C++ is available for true bottlenecks, not as a reflex.

## 23. Development Phases

### Phase 0 — Architecture Spike
Prove Godot/C# integration, engine-independent Core, fixed ticks, event bridge, headless test, structured content, and one combat interaction.

### Phase 1 — Combat Sandbox
One character, movement/dodge, basic enemies, damage/status pipeline, six skills, fragments, loot, one room, profiling overlay.

### Phase 2 — Vertical Slice
Greyhaven subset, one dungeon, normal/specialist enemies, elites, Bell Saint, one Godwrought item, save/load, complete content pipeline, audio/VFX pass.

### Phase 3 — Production Foundation
Full disciplines, scalable content authoring, asset pipeline, localization foundation, broader AI, robust saves, telemetry, build/release automation.

### Phase 4 — Campaign Production
Regions, bosses, quests, progression, loot breadth, optimization, accessibility, controller polish.

### Phase 5 — Online Prototype
Only after single-player is genuinely good: authoritative server, two-player co-op, prediction/reconciliation, persistence integration, load/soak testing.

## 24. Decisions to Defer

Do not prematurely lock down exact multiplayer transport, Kubernetes, Redis, FMOD, custom ECS, native C++ optimization, microservices, procedural-world architecture, live-service platform, anti-cheat vendor, or console specifics.

Preserve options without paying their complexity cost before needed.

## 25. Non-Negotiable Engineering Rules

1. Core game rules do not depend on Godot.
2. Content has stable IDs and schemas.
3. Randomness is explicit and seeded.
4. Combat ordering is documented and tested.
5. Simulation uses a fixed timestep.
6. Save formats are versioned and migratable.
7. Godot scenes are presentation/composition, not hidden databases of game truth.
8. New systems expose observability and tests.
9. Optimize only measured bottlenecks.
10. Multiplayer cannot dictate early development at the expense of fun.
11. Backend starts boring and earns complexity.
12. Developer tooling is part of the product.

## 26. First Technical Milestone

The first milestone should prove the architecture with the smallest meaningful loop:

```text
Godot input
   ↓
Command
   ↓
Ashenwake.Core fixed tick
   ↓
Enemy receives damage
   ↓
Status/fragment interaction occurs
   ↓
Semantic events emitted
   ↓
Godot animates and renders result
   ↓
Replay reproduces identical outcome
   ↓
Headless automated test verifies it
```

Add one structured skill, one enemy, one item, one Divine Fragment, one interaction, deterministic loot, save/load, and the `aw` validation command.

If this loop is clean, the architecture can grow.

If it is awkward, fix it before creating hundreds of skills and items.

## 27. Final Architecture

```text
                     CONTENT
              YAML / JSON / Assets
                       │
                validation/compile
                       ▼
              ┌─────────────────┐
              │ Ashenwake.Core  │
              │                 │
              │ Simulation      │
              │ Combat          │
              │ Items / Loot    │
              │ Skills/Effects  │
              │ AI/Progression  │
              └───────┬─────────┘
                      │ semantic events/state
          ┌───────────┴────────────┐
          ▼                        ▼
 ┌─────────────────┐      ┌──────────────────┐
 │ Godot Client    │      │ Dedicated Server │
 │ render/UI/audio │      │ authoritative    │
 └─────────────────┘      └────────┬─────────┘
                                   │
                                   ▼
                          ┌──────────────────┐
                          │ Go Services      │
                          │ accounts/match   │
                          │ persistence      │
                          └───────┬──────────┘
                                  ▼
                             PostgreSQL
```

The core principle is intentionally boring:

**Keep the simulation clean enough to test without the game engine, keep the content structured enough to reason about, and keep infrastructure out of the way until the game proves it deserves infrastructure.**

That gives Ashenwake room to become a large game without requiring us to become archaeologists of our own codebase.
