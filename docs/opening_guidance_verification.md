# Opening guidance verification

The milestone adds optional contextual hints, a paused First steps guide, earned build/service navigation and character-local preference persistence. No combat values, authored content, authoritative archive schemas or multiplayer behavior are changed.

## Automated evidence

Evidence is retained locally under `artifacts/opening-guidance/`.

- Core guidance: 14 targeted tests pass, covering real Act I rewards, contextual warning selection, unlock gating, read-only projection/replay behavior, bounded memory, corrupt/foreign/future files, backup recovery and symbolic-link rejection.
- Opening balance: ten public-command routes cover all five disciplines on seeds 42 and 43. See [measurements and limitations](first_hour_balance.md).
- Headless client diagnostic: 73 checks, 12 viewport clicks and 1,767 public commands pass. Coverage includes current key bindings, compact layout, preferences across reload, character-slot isolation, earned services, actual stash approach, training damage and campaign preservation.
- Final packaged macOS diagnostic: 78 checks, 12 viewport clicks, 1,767 public commands and four rendered captures pass. The final build has zero warnings/errors, and native/export logs pass the Godot log check. The hints and build guide were visually inspected at 1280×800 and 780×720. The final package SHA-256 is `e64259367f0f23985e15d382c9a2b48164ec9e0d93d6c24cbcb3d38c3375fdd3`.
- Server suite: all 17 tests pass. Changed-file whitespace verification and automated release-engineering checks pass; existing public-release gates remain open.
- Full gameplay regression suite: all 1,597 tests pass, with no skips or failures, in 14 minutes 42 seconds (`core-full.trx`). This includes all 24 new guidance and opening-route cases.

The first native attempt correctly refused navigation while focus interruption had paused the game. The diagnostic now explicitly restores focus and resumes before the training approach; both subsequent native runs pass. The initial failed evidence is retained in `native-attempt1`, with the final result in `native-final`.

## Prism review

Prism/Gemini reviewed the staged implementation (`dd2b311fed220a3e2cd93f9f13d6f2a6`) and final UI changes (`327dec283a459f92187b1e174a53a9ae`), with default secret redaction. The initial report contained two high, three medium and three low findings; the follow-up contained one medium and one low. Raw reports are `prism-review.json` and `prism-followup.json`. These were assessed against the called runtime paths, rather than treated as a zero-finding review.

- Both high missing-player claims assume an invalid combat view. `CombatSession.ValidateSnapshot` requires unique actor IDs and exactly one player with ID 1. Dead players remain in the view; room transitions replace a copied snapshot and retain the player before publishing it. No asynchronous empty frame is exposed.
- The claimed per-frame JSON serialization does not occur. The director caches the projected context, and the panel serializes only on `SetView`; its process callback invokes that method only when bindings change. Text substitution likewise runs when rebuilding changed content, not on every frame. The bounded actor/hazard queries deliberately avoid full archive projection on unchanged frames; no measured bottleneck was established by those suggestions.
- The valid input-polling suggestion was addressed: key bindings are checked every half second, with immediate refresh when opening or presenting the guide.
- Corrupt files do not disable the guide. A valid backup is recovered and writable; when neither copy is trustworthy, changes work in memory and existing bytes are preserved. The tests verify both paths. Automatically replacing unknown damaged data would weaken the established preservation behavior.
- Character changes are synchronous. `Adopt` resets guidance memory before assigning the new session and refreshing it. The Core identity guard remains useful; the proposed mismatch race has no execution path here.
- The follow-up null-action claim is not reachable: UI dispatch uses non-null action strings authored by the projection. Even a null argument would fail the preceding `c.Action == action` comparison before reaching `action.Length`, because the known card actions are non-null. Moving the constant length check out of this small bounded lookup is optional cleanup, not an established performance defect.

Local review also separated support and damaging warning changes in the context cache, excluded already-resolved casts from interrupt completion, and made the shared training action acknowledge its service invitation. The native diagnostic checks the latter.

## Reproduction

```sh
source tools/env.sh
dotnet build Ashenwake.sln --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
dotnet test game/Ashenwake.Tests/Ashenwake.Tests.csproj --no-build --no-restore
dotnet test game/Ashenwake.Server.Tests/Ashenwake.Server.Tests.csproj --no-build --no-restore
"$GODOT" --headless --path game/Ashenwake.Client -- --opening-guidance-smoke --discipline=Vanguard --output="$PWD/artifacts/opening-guidance/recheck"
```

Use a fresh output directory for each diagnostic run. Add `--capture-opening-guidance` to a rendered packaged run to capture the hint, guide and compact layout. Keep the native window focused while the scripted viewport input runs.

## Remaining acceptance

Independent first-time players should validate whether the hints teach movement, interaction and interrupt timing without coaching, whether the first build is understandable, and whether notifications distract during combat. Automated success does not replace novice difficulty testing or hardware certification. Public release gates remain tracked separately.
