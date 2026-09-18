# Echoes: Borrowed Memory

This is a permanent, optional local experiment based on Game Design §22 and Implementation Plan Phase 8. It reuses one ordinary four-room Fracture contract, its generated manifest, existing enemies, and existing `skill.echo_storm`. It does not create a season calendar, daily task, reset character progression, add a currency, or extend the online economy.

The Core implementation is in `game/Ashenwake.Core/Experiments`, with an opt-in board and memory/Storm presentation in the endgame client. The `aw experiment` commands validate content, import an ordinary hub into a separate archive, run the contract, and verify its replay. `tools/experiment-verify.sh` checks the CLI and actual client routes; the export pipeline also runs the contract in the packaged application. Automated checks establish mechanics and persistence, while player judgments about novelty, balance, and clarity remain a playtest gate.

## Player choice and counterplay

At the unlocked Fracture gate, the player chooses **Keep my Mind** or **Borrow a memory** for an owned Sigil. Keep my Mind starts exactly the ordinary Fracture. Borrow a memory suppresses the equipped Mind fragment's effects while seeking one elite memory. The owned fragment stays installed and owned, and its permanent Resonance does not change. Other anatomy slots, discipline resources, passives, mutations, manifestations, equipment, and ordinary Fracture rules continue to work.

The first eligible elite killed by the player or a player-owned effect leaves a memory at its death position. Illusions, mechanical boss actors, and already-consumed resurrected bodies cannot create another offer. The player must walk within 1,400 world units, have line of sight, finish their current action, and explicitly **Bind memory**. A corpse's memory is a separate scoped pickup; binding does not steal the physical corpse used by Gravecaller or Voracious Renewal.

Binding grants one use of the authored Echo Storm ability for 450 simulation ticks. Its actual range, damage, radius, family, status, and action recovery come from the existing skill definition; it uses the same damage, barrier, resistance, critical, and effect processing as other player actions. It can interact with the Fracture's existing fragment-overcharge and resistance rules. It does not become a permanent skill or gain permanent mastery.

Casting also creates a hostile Storm field at the player's cast position. A warning lasts 45 ticks; the field then lasts 60 ticks with ordinary area pulses of 18 base Storm damage and a 1,600-unit radius. The field belongs to a real enemy source and uses normal defenses, barriers, immunity, and line-of-sight checks. Moving or dodging away is the intended counterplay. It is removed with ordinary enemy hazards when the room is won. The warning uses shape, text, and a numeric countdown; it must remain understandable with reduced motion, reduced flash, alternate colors, and muted sound.

The Mind effect returns when the loan is spent, released, expires in combat, or is lost on death. The permanent fragment is never deleted or replaced. A bound loan can cross an explicit room advance with its remaining combat lifetime; an unbound offer is forfeited when leaving its room. A death ends the loan for that contract, including retries. Leaving the Fracture clears all scoped loan state. The player can decline or release the loan and still finish the ordinary run for ordinary rewards.

The 450-tick lifetime is a combat ability timer. There is no wall-clock availability window or attendance deadline.

## Reward and ownership

An actual completed Fracture in which the borrowed Echo was bound and cast earns the character one `cosmetic.borrowed_memory` receipt. It is a display badge with no power, materials, experience, item, or unlock effect. The receipt records the immutable experiment rules hash, Fracture run ID, source elite, Echo action, and completion tick. It must point to an existing authoritative Fracture reward receipt.

Repeating the contract can exercise another build, but cannot replace, reroll, or duplicate the cosmetic receipt. Keep my Mind, a released/unused memory, an abandoned run, a death without eventual eligible completion, or a forged reward action cannot create it. Ordinary Fracture loot, experience, catalysts, attempts, and death penalties remain owned by `EndgameRuntimeSession` and `ProductionSession`.

Both Keep my Mind and Borrow a memory create a bounded permanent entry-history receipt linked to the consumed Sigil and actual outcome. These choices survive replay rollover and save/load. The cosmetic is stored in the joint experiment character archive. It is deliberately character-local and does not create another account economy or shared currency. Existing shared profiles still carry only their existing validated discovery/unlock metadata.

## Published identifiers and retirement

- Catalog file: `content/experiments.json`, schema 1.
- Contract: `experiment.borrowed_memory`.
- Immutable rules: `echoes.1`; runtime archive: `experiment-runtime.1`; scoped combat state: `borrowed-memory.1`.
- Cosmetic: `cosmetic.borrowed_memory`; ability: existing `skill.echo_storm`.
- Admission policy: `Available` or `Retired`.

The definition hash covers every mechanic and reward identifier. Admission policy is explicitly separate from that immutable hash. Retiring the contract changes entry policy only: existing runs can restore, finish, claim their eligible cosmetic, and return normally; new Borrow a memory entries fail without consuming their Sigil. Keep my Mind and all ordinary endgame content remain available. Recorded replays retain the admission policy under which they were recorded, so retirement does not change historical input outcomes.

A mechanical change is a new exact rules identity. The loader rejects mismatched hashes, unsupported schema versions, or unsupported rules before typed parsing/fallback can overwrite a future archive. Retiring a version requires keeping its published definition and runtime available to finish old runs. If a future build cannot support that runtime, it must preserve the original archive and offer an explicit, separately tested safe-hub migration into a new destination. Silent content rebinding and silently abandoning an in-flight loan are prohibited.

No existing base combat catalog is altered. `CombatSnapshot.Experiment` is nullable and omitted when absent. Original endgame archives and ordinary characters retain their prior hash and behavior. Opt-in validates an existing hub archive and wraps it in a separate archive; the original file is preserved.

## Sustainable scope and validation

The release scope is one contract, one borrowed ability, one hazard, and one cosmetic. It reuses existing authored Fracture geometry, elite eligibility, damage/area processing, attempts, generation, progression, and accessibility settings. There is no scheduler, new matchmaking mode, live-operations service, or promise of a repeating content cadence.

Automated acceptance includes ordinary-run equivalence, original archive/hash preservation, actual elite kill and proximity binding, Mind suppression and restoration, actual Echo damage and avoidable Storm damage, room transfer/expiry/death/retry/abandon cleanup, complete real Fracture reward idempotency, active save/replay, raw future-header preservation, safe character-first publication, and retirement during an active contract. Client acceptance additionally requires a discoverable opt-in board, an explicit tradeoff, visible memory and hazard markers, an earned badge, and save/reload coverage through the public wrapper.

Before describing the experiment as player-validated, observe players on at least three materially different builds, including an installed Mind fragment and a summoning build. Record whether they understand suppression, deliberately bind or release, use the Echo before expiry, recognize the warning, and can decline without feeling excluded from required power. Adjustments after release require a new rules identity and compatibility fixtures. No claim of human playtesting or production-art readiness follows from an automated route.
