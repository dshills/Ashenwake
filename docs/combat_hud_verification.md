# Combat HUD and reward feedback verification

Implementation baseline: `c557df5`. Toolchain: Godot 4.6.2 Mono, .NET SDK 8.0.425, macOS Apple Silicon.

## Scope and authority

The solo HUD groups health, barrier, resource, potion/dodge readiness, six skill buttons and level XP. Player status chips display Core durations/stacks and use a bounded cache; navigation, objectives, target details, utility controls and the pause card adapt to smaller windows. Loot notices use the collected `CombatItem`; level, mastery and ability notices compare actual progression views. A quiet session baseline prevents save restores from replaying rewards, including historical discipline unlocks. Cosmetic queues and timers are bounded and pause with gameplay.

Core retains combat, progression, item-roll, ownership, save and replay authority. The sole Core addition is a read-only `ProductionSession.CurrentLevelExperience` projection of the existing progression threshold method. No content, balance, save schema or reward rules changed. Co-op retains its existing dedicated HUD.

## Source evidence

- All **434 Core tests** passed; no skipped tests. Log: `artifacts/combat-hud-tests.log`.
- Client build passed with **zero warnings and errors**. Logs: `artifacts/combat-hud/build.log` and `format.log`.
- Final rendered HUD diagnostic: **104/104 checks**, **18 captures**, **4,901 commands** and **1,675 earned XP**, in `artifacts/combat-hud/source-clean-captures.Pi5u0l`.
- A fresh character reaches and defeats the Bell Saint through ordinary campaign commands. The separate, unchanged phase-four archive supplies the endgame character; normal Fracture combat earns mastery/levels and Mara retraining unlocks Arcanist abilities. All commands have exact replay coverage in segments of 1,394, 1,800 and 1,707 commands. Final state: `01EE18E2F2B2C81C506AF1580E896D5A5AA90C0D517D87704FA19F6C91ACB34C`.
- The Vanguard route did not naturally produce stacked status effects. A separate fresh Veilwalker uses two ordinary Venom Knife casts in an authored Monastery encounter to earn actual poison stacks. Its independent **22-command** replay ends at `13B8E5F32C5C36F57A01F37E1F375CBF7440DEBDE6888A7000ABBDE9DBC83A59`. No actor health, stats or statuses were edited to produce this evidence.
- Screenshots were inspected at **1280×800, 1000×720 and 780×720**, including compact skill names, XP, loot rarity/icons, earned milestones and stack/duration glyphs.
- The corrected source mouse-action diagnostic passed **102/102 checks** with 14 exact branch replays in `artifacts/combat-hud/mouse-source.JRPaX1`. Client formatting verification and staged/unstaged whitespace checks also passed.

The HUD diagnostic drives commands explicitly while rendered frames do not advance Core. It ignores desktop focus changes through the existing automatic diagnostic mode, so captures remain unobstructed. Its pause checks verify stable HUD projections and paused reward lifetimes. The shipping-scene interaction diagnostic separately verifies real pause/input behavior. Early rendered shipping-scene runs were interrupted by desktop focus loss; their partial failures are retained and are not claimed as passes. A subsequent headless shipping-scene run passed all 42 then-current navigation/pause checks. A 43rd compact pause-card assertion was added after screenshot inspection exposed its fixed position.

## Prism review and fixes

Both staged reviews used the user's approved Prism/Gemini configuration and `tools/prism-implementation.json`.

- Review `dc13141738215632f208b33ed14b144d` reported two medium and two low findings. The Adventure HUD now caches viewport/panel bounds; unchanged status displays skip allocations, joins and UI updates. A pickup arriving near a coalesced burst's eight-second limit starts a fresh queued burst, preserving readable notice time. The remaining localization proposal concerns English presentation copy throughout the existing client and was not expanded into a content/schema refactor.
- Final review `fc84c0fbd1635c9a6d6083eaf9ba4e40` reported one medium and three low findings. The medium overflow concern is not present: the existing `if (OverflowCount > 0) visible--;` reserves the overflow slot, and native narrow-strip assertions verify displayed plus overflow counts equal active conditions. The low slot-parse concern is a defensive fallback for malformed presentation data; actual pickups originate from validated Core equipment definitions. The string-enum and centralized-name proposals are architectural/localization suggestions, consistent with the current string-based Core status contracts rather than defects in this phase.

Review JSON and logs are saved as `artifacts/combat-hud/prism-review.*` and `prism-final.*`. Native visual inspection additionally corrected compact skill wrapping, the hoverable control reminder and pause-card positioning.

## Package acceptance

The first package run exposed an obsolete mouse-diagnostic assumption: its fixed floor checkpoint projected beneath the new dock, so the UI correctly consumed the click. Checkpoint selection now verifies real UI occlusion and navigation reachability before a native click, then asserts both route creation and arrival. Loot picking also uses the shipping zoom control when its projected body is obscured. The original pickup-range, selected-item ownership, cancellation and exact-replay assertions remain intact. Superseded failures remain in the artifact directory rather than being counted as passes.

The final export passed all **20 package suites**, including **2,305 UI assertions**, in `artifacts/package/22f02d6da01b.ThD5sH`. Mouse actions passed all 102 checks and the shipping interaction scene passed all 43 checks, including compact pause-card containment. The 32,190-command campaign/endgame replay retains `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`; the 2,516-command Echoes replay retains `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both archives also passed CLI replay verification. Log: `artifacts/combat-hud/export.log`.

The final `artifacts/combat-hud/Ashenwake.zip` was freshly extracted into `artifacts/combat-hud/exact-package.9eOWVY` and ran the rendered HUD diagnostic successfully: **104/104 checks and 18 captures**. Commands, earned XP, actual pickups and every replay hash match the accepted source run. Compact Bell Saint, earned milestone and stacked-status screenshots from this exact archive were inspected. The receipt includes the archive SHA-256 and each suite's result.

Package results and the exact-archive verification receipt are recorded in `artifacts/combat-hud/verification.json`. The checks exercise implemented rendering and state/replay behavior; they do not establish campaign balance, external player acceptance or performance on other hardware.
