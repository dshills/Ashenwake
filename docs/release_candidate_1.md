# Ashenwake 1.0 release candidate 1

RC1 freezes the current solo game for release testing. Its version is **1.0.0-rc.1**; this is a local test candidate, not a public-release acceptance. macOS bundle fields use numeric `1.0.0`, while the game, assemblies and package identity retain the prerelease label.

## Scope and priorities

The candidate includes the five-act campaign and five disciplines, character progression, crafting and Divine Anatomy, optional exploration and secrets, hunts and champions, Fractures and God Hunts, equipment sets and saved builds, appearance wardrobe, bestiary and permanent pets. Retained network/co-op prototypes are outside the 1.0 solo acceptance scope.

No new major systems are planned during this pass. Fix crashes, corrupted or lost saves, progression blockers, broken interactions, unreadable combat, and reproducible performance problems first. Tune pacing and rewards from observed play rather than changing balance solely to make an automated driver faster.

## Engineering acceptance

| Gate | Required result |
| --- | --- |
| Regression | Clean solution build and formatting; full Core and server tests; release-script/source-identity tests. |
| Content and saves | Current content validation; immutable historical fixture hashes; supported upgrades; pre-pet and pet-owning saves restore without lost progression. |
| Actual package | Run the packaged campaign/endgame and feature diagnostics on the exact exported bytes; retain logs and deterministic replay checks. |
| Stability | Checkpoint/replay soak and a persistent endgame run; record measured scope separately from rendering or long human sessions. |
| Identity | Reviewed source commit/tree, version, content/asset hashes, bundled notices and a verified package manifest. |
| Review | Prism findings fixed or dispositioned with evidence; no unexplained failed gate. |

Results and candidate identity are recorded in `docs/release_candidate_1_verification.md` as execution completes. Raw evidence belongs under `artifacts/rc1/`, with package runs in the fresh directories printed by the release tools.

## Independent playtest plan

Use a copy of the candidate and fresh characters; retain existing saves before upgrade. Start with a player unfamiliar with the game, without coaching. Record candidate identity, discipline, device/input, approximate play time, expected behavior, actual behavior, and reproduction steps for each issue. Screenshots and a locally exported diagnostic are useful when available.

1. **First session:** launch, choose a discipline, understand movement and combat, finish the Road, find the next destination, equip the first upgrade, rescue the fox, save, quit and continue. Record every moment that needs explanation or leaves the player unsure what to do.
2. **Campaign:** cover all five acts across the discipline roster. Note deaths with unclear causes, missing or misleading objectives, lengthy travel, repetitive fights, missed upgrades and difficulty spikes. Check that declining optional content never blocks the main story.
3. **Build and rewards:** compare a discovered upgrade, store/retrieve equipment, switch a saved build, try an earned legendary effect and use training to understand it. Record whether rewards feel useful and whether costs/consequences are clear.
4. **Endgame:** enter an earned Fracture and available hunt, recover from defeat, claim rewards, return to Greyhaven, then reload. Verify that the player understands entry costs, objectives and recovery.
5. **Comfort and stability:** test intended display sizes, graphics settings, mouse/keyboard and available controller hardware; check text, contrast, audio/Quiet mode, reduced effects, focus loss and reconnect. Include a longer uninterrupted play session and a later resume.

A completed checklist is not automatically a passing playtest. Record issues and observed outcomes. Prioritize blockers first, then repeated confusion and combat/reward pacing. Recheck changes on the same route before expanding the feature list.

## Release decision

This pass targets the local macOS build on the available Apple Silicon host. A universal binary does not establish Intel Mac or Linux hardware acceptance. Independent playtesting, the intended hardware/input matrix, project credit/distribution decisions, signing/notarization and install/update acceptance remain explicit gates. Public tagging or publishing follows acceptance of a concrete candidate; it is not part of generating this local RC.
