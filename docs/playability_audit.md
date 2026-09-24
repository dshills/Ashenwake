# Campaign playability audit

Audited on 2026-09-24 from `3a99e5b684ceb1b5318e39f74077813647a239ee`, using the pinned .NET 8 and Godot 4.6.2 Mono runtimes on macOS.

## Player-facing fixes

- **F reaches Way Forward.** Both campaign directors now select the nearest in-range projected world interaction, including the contextual Way Forward marker. Previously keyboard input considered only Core interactions, so a marker that worked with the mouse could do nothing with F. The existing callback still owns reward review and story confirmation. Pressing F while paused does not interact.
- **Travel prompts respect the Stop binding.** Approaching an NPC or collecting loot shows the configured cancellation key instead of always saying X.

The input diagnostic checks distant rejection, paused state preservation, nearby keyboard activation, retained drops, and no repeated action after closing reward review. It also temporarily rebinds Stop to F8, reads the displayed prompt, delivers F8 through viewport input, and verifies cancellation without interaction. The original binding is restored without saving a test preference.

Two stale diagnostic assumptions were corrected while running the audit. The former fixed Greyhaven ground click now selects the training entrance; setup instead finds an unoccluded reachable floor point and proves the character reached it through mouse input. Repeating Rooms now reveals both nearby shadows at spawn, so the hidden-actor presentation check uses the actual distant shadow in the Unremembered Vault. It reads Core visibility without altering actor state.

## Fresh-character campaign measurements

The existing `earned-build` policy starts a fresh level-one character and profile for each discipline, on seed 42. It spends earned passive points and equips owned stat improvements at Torren between acts. Every command is recorded in replay segments of at most 900 commands; each segment is replayed and restored, and the final save/profile is loaded and hash-compared. No rewards, levels, victory states or damage boosts are injected.

The full exploration route completed all 15 main encounters and eight optional exploration areas for every discipline. All finished at level 10, with 5,150 XP, 475 materials, zero deaths and zero potion uses. All five main-path-only routes also completed the 15 main encounters with no optional exploration, at level 10 with 5,150 XP, 255 materials, zero deaths and zero potion uses. In total, these ten runs cover **56,856 commands and 285 verified replay/restore segments**, with no final-save hash mismatch.

| Discipline | Full-route commands | Combat seconds | Health lost | Longest combat room |
| --- | ---: | ---: | ---: | --- |
| Vanguard | 7,145 | 159.1 | 445 | Bell Saint, 27.2 s |
| Veilwalker | 6,378 | 131.5 | 408 | Bell Saint, 30.1 s |
| Arcanist | 5,368 | 96.5 | 171 | Bell Saint, 12.9 s |
| Gravecaller | 8,335 | 149.9 | 597 | Bell Saint, 14.8 s |
| Warden | 7,816 | 180.8 | 203 | Bell Saint, 33.2 s |

| Discipline | Main-path commands | Main-path combat seconds |
| --- | ---: | ---: |
| Vanguard | 4,690 | 122.2 |
| Veilwalker | 3,931 | 96.7 |
| Arcanist | 3,059 | 65.6 |
| Gravecaller | 4,825 | 117.5 |
| Warden | 5,309 | 142.0 |

Combat seconds sum ticks begun with living enemies, at 30 ticks per second. Health lost sums positive per-tick health decreases and excludes room transitions; healing allows it to exceed one health bar. Neither measure represents human play duration or difficulty. The policy reads hazards precisely, uses skills promptly and does not spend time learning controls.

The measurements show no mandatory optional-content dependency or discipline-specific completion blocker. Bell Saint is the longest fight for all five policy runs. This is a useful first-time-player observation target, not sufficient evidence for flattening class differences or changing boss health. No numerical balance changes were made.

## Loot, persistence and readability coverage

The rendered input diagnostic exercises selected-drop pickup, other-drop preservation, filtered loot, hold-to-reveal, hover health/conditions, protected boss instructions, dead/hidden actor labels, and the crypt's treasure/return/revisit path. Campaign branches and separate readability/mechanism combats replay exactly.

The existing Core suite covers fresh completion with all five disciplines, branch deaths and anchor restoration across the five acts, full-inventory departure, retained ground loot, one-time testaments, storm expiry/retry, save compatibility, crafting transactions and equipment protection. Those cases supplement the successful measured routes: a zero-death scripted run does not itself test death recovery.

The gear heuristic scores raw damage, armor and critical chance. It does not optimize behavior powers, craft, respec, spend materials or evaluate whether a player notices an upgrade. Consequently, the unspent material totals are not evidence of an economy problem. No change to loot frequency, affixes or crafting prices was justified by this audit. Independent first-time play sessions should record upgrade recognition, crafting choices, potion use, failed attempts and time spent reading separately from fighting.

## Reproduction

From the repository root, use fresh output directories:

```sh
source tools/env.sh
dotnet build Ashenwake.sln --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
dotnet test game/Ashenwake.Tests --no-build --no-restore
dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll balance campaign artifacts/audit-full 1 --managed-build
dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll balance campaign artifacts/audit-main 1 --main-path --managed-build
"$GODOT" --path game/Ashenwake.Client -- --mouse-actions-smoke --discipline=Vanguard --capture-mouse-actions --output="$PWD/artifacts/audit-input"
```

Keep the rendered diagnostic focused; ordinary focus loss deliberately pauses the game. Add `--headless` and omit capture for the non-rendered run. Evidence from this audit is under `artifacts/playability-audit/`, including the initial failed fixture runs. The campaign policy covers the campaign and its eight exploration branches; it is not an exhaustive endgame, hunt-board, secret-chamber or cooperative playthrough.

## Prism review

Prism/Gemini reviewed the staged fixes with default secret redaction (`f53312a85ce00d96d98ebe3c35bb23fd`). Its two high findings were checked against the source and are not actionable:

- The alleged missing player during transitions conflicts with the validated session invariant: `CombatSession.ValidateSnapshot` requires exactly one player with ID 1. Creation installs it, death retains it, and room transitions replace the complete session synchronously. The input helper also rejects a dead player. There is no identified missing-player execution path.
- The alleged `_session.View` typo conflates different fields. Sandbox owns `CombatSession`; the directors own campaign/endgame runtime sessions and access their `.Combat`. The solution builds without warnings or errors.

Follow-up review `c562be55a6a9f44526ff09ac6fc3fae1` covered the hidden-actor fixture correction. Its medium concern was conditional: visibility is calculated by Core's `CombatProductionBuild.ActorVisible`, and the diagnostic only reads the result. Its low suggestion to replace authored content with synthetic visibility would remove the integration coverage this diagnostic intentionally provides; the explicit assertion should fail if future content stops exercising hidden actors. No unresolved actionable Prism findings remain.

## Verified build

- Solution build: zero warnings/errors. Changed C# whitespace formatting passes.
- Full Core suite: **1,597 passed, zero failed or skipped**, in 12 minutes 29 seconds (`artifacts/playability-audit/tests.log`).
- Source input diagnostic: **194 headless checks**, **211 rendered checks**, 17 captures, 419 input commands, 336 campaign setup commands and 15 matching campaign replay branches. Readability and hunt-mechanism combat replays also match.
- Exact exported Mac app: **211 rendered checks** with the same command/check counts and 17 captures. Visual inspection confirms F opens the reward review with both remaining drops retained.
- Export and successful diagnostic logs pass `tools/check-godot-log.py`.
- Source evidence: `artifacts/playability-audit/mouse-headless-3/` and `mouse-native-3/`; exact-package evidence: `package-mouse-2/`. The first package attempt lost operating-system focus and correctly paused; that interrupted attempt is retained separately.
- Updated playable archive: `artifacts/export/Ashenwake.zip`, SHA-256 `f7bb448286344190f8678e67daac5cd345461255e38788430a783c01657ae629`.
