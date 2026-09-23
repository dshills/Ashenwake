# Greyhaven training ground

Click the marked practice effigy in southwest Greyhaven, or choose **Gear → Practice this build** to walk there. Use **Single target**, **Target group**, the **Sparring** selector, **Reset**, **Breakdown**, and **Leave** in the practice bar. The breakdown pauses combat while you read it. Inventory and journey shortcuts open that breakdown during practice; settings and normal pause controls remain available. See [verification](build_experimentation_verification.md).

The training ground creates a disposable combat copy of the character's current earned equipment, passive projection, anatomy, manifestations, discipline, unlocked ultimate, and selected mutations. Entry requires a living character in Greyhaven within 2.4 metres of the training marker at (-4.5, 4.5). Changing equipment or build choices remains a Greyhaven service; return and begin another session to test a changed build.

Choose a single target with 10,000 health or five targets with 1,500 health each. Targets have zero armor/resistance, never move or attack, and obey the ordinary player damage, status, projectile, summon, kill, corpse, and proc rules. Three practice corpses allow Gravecaller abilities to operate through their ordinary corpse requirements. They grant no initial resource or summon. Killing a target leaves its corpse; targets return only on Reset. Choose **Melee sparring**, **Ranged sparring**, or **Mixed sparring** to face an Ash Ghoul, a Cinder Acolyte, or both. These opponents retain their ordinary health, defenses, movement, attack warnings and combat rules. They use their recognizable creature models; passive targets remain wooden effigies. Sparring can end in defeat without affecting your real character.

Each reset restores the copied character's health, potions, and cooldowns, starts resource at zero, clears transient encounter effects, and resets target health and measurements. It does not change the real character. A session stops after five minutes of simulated time, when all living targets are defeated, or when the copied character dies. Reset or Leave remains available after completion. Closing a training session cannot be reversed; entering again captures the character's current build and remembers the selected practice mode. Reset or Leave retains the last nonempty report for comparison. Empty resets do not erase that report. Comparison stays available after leaving to change gear and re-entering with the same character, including a main-menu round trip. Loading a save, switching characters, or restarting clears the comparison. No practice history is written into the character save.

## Measurement contract

- Time means actual combat simulation ticks at 30 ticks per second. Merely reading the report or pausing the client does not advance it. DPS includes idle simulated time since the last reset.
- Damage is actual enemy health removed by player-owned hits, capped at remaining health. Barrier absorption, overkill, prevented damage, and duplicate critical-hit notifications do not inflate it.
- Every damage row carries the actual damage family plus effect provenance from the resolved hit. Ordinary skill damage and its damage-over-time are grouped by ability; attributed fragment damage uses the fragment identifier; distinct legendary damage uses the legendary effect; ordinary summon attacks have their own category. Bonuses that modify an existing skill's damage are included in that skill's total rather than presented as a fabricated separate contribution.
- The engine retains its normal first-source ownership rule when an existing status is refreshed. Reports reflect that authoritative attribution. They do not attempt a counterfactual allocation of damage to competing effects.
- Trigger counts distinguish readiness notifications from actual triggers. A defensive or utility trigger may have a count without dealing any damage.
- Resource increases and decreases are observed at every authoritative in-combat resource assignment. They include actual capped generation, skill spending, Arcanist heat, venting, passive decay/cooling, and Furnaceheart changes. They are gross changes, not net-per-tick estimates. From the zero-resource start, increases minus decreases equals current resource.

Training instrumentation is runtime-only. Combat events, combat snapshots, content identities, and ordinary replay formats are unchanged. The damage callback sees the already-calculated hit, and resource hooks assign the same values as ordinary combat.

## Defense and comparison

The breakdown partitions resolved incoming damage into armor/resistance mitigation, immunity, barrier absorption, actual health lost, and overkill. The total before these defenses retains the attack's modifiers and vulnerability. Each component comes from the same integer damage calculation used by combat. The components add up to the incoming total; damage absorbed by a barrier is not counted again as health loss. Health loss is cumulative and does not subtract healing. Attacks that miss or never resolve against the player are not assigned hypothetical prevented damage.

Incoming rows identify the opponent, attack and damage family. Defensive effect rows include barrier grants and supported fragment, legendary and Manifestation activations. These are event counts, not additional damage-prevention amounts. A power can appear in both the defensive section and the general trigger list; the UI does not sum those sections into a unique activation total.

The previous-attempt section shows current and previous build descriptions, practice mode, elapsed time, outgoing damage/DPS, health lost and its rate, barriers, mitigation, immunity, resolved incoming hits and defensive event counts. Different modes or durations are labeled explicitly. Even matching modes and durations are not a controlled ranking: movement, skill timing, healing and enemy exposure can differ. The comparison is bounded to one previous attempt.

The report pauses practice and keeps its controls stable while reading. After an automatic completion, held accept or mouse attack input must be released before report actions activate. Reset restores the original copied build, not the damaged practice state. Training defeat opens the practice breakdown, never the campaign death recap or its recovery actions.

## Isolation and verification

The training wrapper holds only a copied combat snapshot. It has no production session, profile, wallet, campaign reward receiver, or mastery reconciliation path. Training kills suppress loot creation but retain normal kill-driven combat effects. Build-changing and pickup commands are rejected. Original journey state and profile are never stepped or reconciled during training, and no result flows back when leaving.

Training's bounded diagnostic replay stores the clean initial copy and up to 9,000 input frames with combat-state and report hashes. New schema-2 reports include defense measurements; the verifier retains the schema-1 report projection for earlier passive-target diagnostics. Verification reconstructs the isolated targets and replays real commands. This is diagnostic evidence, not a persistent training save or a route for importing rewards.

`TrainingSessionTests` cover earned build isolation for all five disciplines, entry proximity, read-only command restrictions, stationary targets, reset/close behavior, resource conservation, real fragment burning and Widow echo attribution, heat/vent accounting, summon damage, capped overkill with loot suppression, deterministic replay, tamper rejection, and the session time bound. Explicit effect fixtures are detached and separate from tests of earned character admission.

See [defensive training verification](defensive_training_verification.md) for this extension’s automated, packaged-client and Prism evidence.
