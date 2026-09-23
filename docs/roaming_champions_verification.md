# Roaming champion verification

Baseline: `aaadda2` (personal stash). Evidence is retained under `artifacts/roaming-champions/`.

## Behavior and persistence

The three optional champions have stable per-character locations in six possible secured campaign rooms. Discovery requires proximity; inspection and collection views are read-only. Each refuge has a safe foyer, explicit challenge, combat, victory/claim and retreat states. The Pilgrim's chains pull and root, its bell can be interrupted, the Widow's three destructible nests stop producing poison when killed, and the Tithekeeper alternates armor with a real open-vent damage window.

Retreat restores the exact campaign room and position, preserving ground loot and progression. Unfinished combat resets, but victories and unclaimed treasure persist. Permanent victory and reward receipts prevent duplicate claims. Champion arenas suppress ordinary loot and XP; the explicit treasure grants one named legendary per champion per character. Permanent build changes, stash transfers, other journeys, training and Borrowed Memory entry are blocked while inside.

Unused champion state is omitted. Restoration checks stage, source room, seeded identity, combat outcome, receipts and nested combat. Unsupported future champion schemas protect save files. Additive item-catalog migration is pinned to frozen personal-stash-era fixtures and preserves stored equipment metadata and active regional hunts.

## Automated verification

The first focused Core run passed 52 tests. The final 14-case runtime run additionally covers stash-before-fight, stored signature ownership and the full-backpack boundary. Collection routing and spoiler checks pass 23 tests. The server suite passes 17 tests. Four additional earned-route cases pass for Veilwalker, Arcanist, Gravecaller and Warden: 12 champion victories and claims without forged builds or combat stalls. Together with Vanguard, all five disciplines are covered. Four real chain-hit boundary/collision cases extend the focused combat suite to 16 passing tests.

The initial full suite completed with 1,564 passing tests and one failed assertion in 13 minutes 51 seconds. It exposed an outdated collection test assumption that every non-secret legendary had a repeatable Fracture source. Champion-only items deliberately do not. The expectation now excludes champion sources and explicitly verifies hidden champion IDs; the corrected collection suite passes.

The final full regression run passes **1,573 Core tests**, with zero failures or skips, in 13 minutes 41 seconds (`core-final.trx`). It includes the corrected collection expectation, all five disciplines and the additional chain-collision cases. All seven content compilation commands complete, and the solution builds without warnings or errors. Final changed-source formatting verification and strict export/runtime log checks pass. The release engineering audit passes automated fixture integrity and file-presence checks; its existing public-distribution gates remain separate.

## Client verification

The headless diagnostic (`headless-2`) passes 222 checks across 8,829 public gameplay commands and 26 viewport button clicks. It earns the campaign route, approaches each real world target through mouse-navigation code, cancels and accepts challenges, fights all three champions, recovers from an actual death, leaves and reenters won-but-unclaimed encounters, claims each treasure once, previews the equipment and follows its collection source. It verifies exact source loot/progress/position restoration and save/load/replay at each encounter stage. The champion journal fits 1280×800 and 780×720.

Challenge dialog accept/cancel uses the dialog's public signals; menu actions use viewport mouse events. This is functional automation, not a claim that an independent player completed the route. The first headless run failed a diagnostic position expectation after its own retry walk moved the character closer to the sighting; the expected position now records that walk, and death-return separately asserts the original position.

## Prism review

Prism/Gemini review `37097fe7e552a8c4358b6b5afbd95237` ran with default secret redaction. It reported high, medium and low findings around the chain-pull handler. The high null-source claim is contradicted by the handler's existing `source is null` early return. The medium collision claim is contradicted by `MoveActor`, which passes movement through `_spatial.Move` and checks living-actor overlap; the champion arena also has no interior obstacles. These findings cite incorrect five-line file locations and do not establish the described execution paths. The low suggestion was addressed with named chain content and pull-distance constants. Four real-hit regression cases confirm arena-boundary and close-source actor collision behavior.

The second review, `dabb55f0b787a0652fe4aafd0089dafa`, reports one high, three medium and three low findings. The high claim assumes inventory capacity is checked only by the UI; Core's `ChangeRoamingChampion` rechecks `Production.CanClaimRoamingChampionReward` before both challenge and claim, on the synchronous command path. Its proposed change would incorrectly allow a failed grant to be marked claimed. Full-backpack rejection is tested atomically.

The three medium claims are also contradicted by the called code: `Toward` caps at the target when the requested step reaches it; all persisted champion state goes through `CaptureRoamingChampions`, which captures the live arena; and `GrantRoamingChampion` already calls `SynchronizeItemSequence` before granting. Save/load/replay assertions cover foyer, combat, defeat, victory, unclaimed reentry and claimed states, including stored ownership.

The remaining low suggestions concern comparing at most three UI entries and moving authored encounter constants into configuration. The small bounded comparison prevents redundant panel rebuilds and is not an established performance defect. Encounter values follow the existing code-authored optional-encounter pattern; the shared 2500 vulnerability bonus also serves existing Vulnerable status behavior. No balance or catalog change is warranted by these suggestions. Both raw reviews are retained; this does not claim that Prism returned zero findings.

## Rendered checks

The first packaged native run (`native-final`) passes 255 checks, 8,829 commands and 26 viewport button clicks, with 26 rendered captures. All three custom silhouettes, arena landmarks, the Tithekeeper's authoritative vent countdown, signature-equipment previews, the three-entry compact journal and all seven compact Journey tabs were visually inspected. The native process exits cleanly without the initial headless ObjectDB warning after diagnostic shutdown releases the scene and audio resources.

Visual review identified redundant journal copy and overlapping static nest captions. The final UI pass removes those duplicates and keeps the current champion selected during an active encounter so another sighting cannot supply mismatched tactics or reward information. The UI-only Prism follow-up, `cba2d4bb6e20e34307abbfc8dacff72b`, reported three invalid-state hypotheses. `CampaignContent.Validate` requires exactly five acts numbered 1–5, and champion acts are fixed catalog values; `CombatSession.ValidateSnapshot` requires exactly one player actor with ID 1, retained through death and synchronous encounter transitions; champion names come from non-null catalog entries rather than serialized names. These guards rule out the claimed missing-act, missing-player and null-name paths. The raw report and `prism-ui-triage.json` preserve the findings and evidence. The authored room-name display lookup was added immediately after this review dispatch and uses the existing nullable lookup/fallback pattern.

The second packaged native run (`native-recheck`) passes 265 checks, including active-journal selection consistency and rejection of stale selection requests, with the same 8,829 commands, 26 viewport clicks and 26 captures. The compact journal copy was visually rechecked. An inherited Rootheart “SEVER” caption on the reused nest actor was subsequently suppressed specifically in the champion encounter; ordinary Rootheart roots retain their cue. The final packaged run (`native-clean-captions`) also passes all 265 checks with 8,829 commands, 26 clicks and 26 captures. Its Widow capture confirms compact nest health bars and a single focused name instead of repeated mechanic captions. The runtime log is clean and native shutdown reports no leaked instances. Final package SHA-256: `b532998b40d36b0d2ef7f7ec58b50674be31ca83e9be5ae122a124c9089c79a8`.

## Scope

This milestone provides solo optional encounters and three existing legendary powers in new equipment slots. It does not add account-wide rewards, recurring champion respawns, a shared-world roaming simulation or new multiplayer encounters. Scripted tests do not substitute for independent balance playtests or wider hardware certification.
