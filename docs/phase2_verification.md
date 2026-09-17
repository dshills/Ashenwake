# Phase 2 — Greyhaven slice

## Playable integration

The default client now connects Greyhaven, the ossuary, funeral cloister, and Bell Saint sanctum. Mara introduces the wound and implant premise; the journey map shows connected exits, objectives, discoveries, and journal state. Actual combat deaths advance the encounter state. The cloister is an anchor; death resets unfinished encounters and returns the character without removing inventory or permanent rewards.

The three Bell Saint phases use chain/sonic tells, finite corpse resurrection protected by two targetable anchors, then a rushing beast with independent ringing bells. Core owns all hit locations, telegraphs, immunity, corpse limits, rewards, and resets. The priest heals/shields allies and the Emberling rushes into a telegraphed explosion. The slice therefore has three ordinary roles and two distinct specialist behaviors.

The unique Heart of Serath is awarded once per character. Each completed expedition grants materials and a reproducible 20% Ashcleaver chance; repeated completion commands cannot grant duplicate rewards. The slice also grants one unawakened Ashcleaver explicitly for progression demonstration. Its per-instance 1,000-burning-kill threshold is unchanged, and tests exercise awakening without lowering production content requirements. Stacks increase attack speed, awakening produces flame waves, and the confirmed Serath/Orrun graft choices change combat behavior.

Twenty curated equipment combinations have authored intent, fragment/mutation choices, and validated slot compatibility. Burning Blood and Stone Memory have implemented benefits and complications, visible transformation, and NPC reaction text. Later Manifestations remain hidden until their gameplay implementations exist. Anatomy ownership, service proximity, material payment, graft permanence, and resonance suppression are enforced by Core. The funeral-flame Concordance is discovered from tags and persists.

## Persistence, tools, and verification

`ExpeditionSession` coordinates both authorities. Its snapshot atomically includes world progression, full combat, permanent Godwrought state and item-instance mappings, independent RNG state, and a monotonic burning-kill sequence. World interactions and combat commands share a bounded replay recording. Restore rejects inconsistent combat/world anatomy, encounters, or derived bonuses. Rewards that cannot fit a full combat inventory remain in permanent ownership rather than deleting another item.

Tests cover actual full-route combat, all three boss phases, anchor destruction, reward return, mid-fight deterministic continuation, corrupt-save backup recovery, incompatible-save preservation, service/ownership rejection, and death recovery. Adventure fixtures additionally cover one explicit legacy migration and idempotent rewards/crafting. This is a declared development-format migration, not a claim of compatibility with every future save version.

`aw adventure validate|compile|demo|replay` uses the same Core. `aw item show` and `aw content refs` inspect versioned source definitions and their JSON references. `tools/verify.sh` retains Phase 0/1 regression checks and adds the full slice. Export now launches the complete packaged slice smoke, then verifies its replay in tooling.

The initial headless integration completed in 895 combat ticks with no deaths, all three boss completions, two anchor destructions, the unique reward, and the return to Mara. The client smoke additionally implants the reward, selects a different Manifestation, visits Torren, tempers Ashcleaver, and starts a second expedition. Final staged validation and Prism disposition are recorded below.

The full expedition benchmark covers synchronization and replay hashing as well as combat. Five warmed routes measured 4,479 operations: p50 0.290 ms, p95 0.413 ms, p99 0.691 ms, maximum 1.418 ms on the development Mac. Allocation averaged 145,951 bytes per operation; this remains a profiling target as content grows. Rendering, input-policy generation, and disk writes are outside these measurements.

## Prism review disposition

The initial staged Prism review (`prism-phase2.json`) found four medium and one low issue. The UI event counter now uses a dictionary and sorts only for reporting; HUD refresh uses a cheap revision/proximity key; room visitation diagnostics are capped at 256 entries. Explosion damage now uses the committed telegraph position, with a regression that displaces the attacker before detonation. The alleged null-view access is a false positive: `Rebuild` returns before access when the view is null, and `SetView` supplies its related non-null state synchronously.

Additional review corrected Burning Blood to trigger on incoming physical health damage, made capped material rewards unable to block boss victory, and tested null-state save backup recovery. Both outgoing and incoming Burning Blood cases have regressions. Automated route and rendered smoke evidence do not constitute manual keyboard/controller usability testing; that acceptance gate remains open.

The final exact-index verification passed 66 tests, formatting, all retained architecture/combat regressions, content validation, full expedition replay, Godot import, and the packaged macOS smoke. The editor and packaged slice both reached tick 1,380, 17 kills, zero deaths, one Bell Saint victory, and expedition two with identical final hash `9FED1764B055C9C9BBFB78964E9C76E4A04C04CAF705B66A3ED2B2C52BB18E84`. The final benchmark measured expedition p99 0.708 ms; dense combat-only p99 was 0.293 ms. These are development-Mac measurements, not GPU or cross-platform certification.

The initial Prism review completed and its findings were assessed/fixed above. A requested second Prism pass was blocked by automatic approval review because the configured provider transmits staged source to Gemini. That second pass did not run; explicit provider approval remains pending for further reviews. Local review and the final exact-index checks cover the resulting fixes.

## Production acceptance still open

This is a functional procedural prototype. It does not claim a finished skeletal character/animation pipeline, final environment art/audio, localization production, twenty externally balanced loadouts, independent player fun testing, or certification across GPUs/controllers. These remain explicit acceptance work from the implementation plan. Full production cannot be honestly declared accepted based only on deterministic tests. The implemented slice provides a runnable foundation for that work.
