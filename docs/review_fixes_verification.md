# Full code review fixes

These changes address all seven actionable findings from the full project review of `870d39dfe9ac8652742ec70ad9352e76e6bec2d9`.

| Finding | Result | Regression coverage |
| --- | --- | --- |
| F1: locale-dependent save ordering | String-keyed sorted collections deserialize and serialize in ordinal order; generated build/content lists use explicit ordinal ordering. Existing archive versions and maintained identities remain unchanged. | Czech and Slovak fresh save/restore, old English serializer output, physical save/load, Phase 2 import and Phase 4 migration. |
| F2: full inventory cannot recover | Confirmed discard at Torren removes owned unequipped items without granting rewards. Equipped gear and the last Godwrought weapon are protected; discarding an extra copy removes only that copy's progression records. | Capacity 512 and 10,512; ownership, confirmation, location, specialist availability and duplicate-operation rejection; extra Godwrought preservation; nested runtime save/replay; actual GUI cancel and confirm. |
| F3: restored co-op match waits for both players | A persisted tick greater than zero preserves the match's started state, allowing one returning player to continue. | Progressed restored match advances with one peer; a fresh tick-zero match still waits for both. |
| F4: same-tick client state is stale | Ordered authoritative updates replace the current view even at the same tick; interpolation timing changes only when the simulation tick advances or the client rejoins. | Connection changes, acknowledgments, interpolation history, stale-frame rejection and join reset. |
| F5: catch-up continues after pause | A callback that pauses the fixed clock ends catch-up immediately and clears remaining wall-clock debt. | First-step pause, no later callbacks while paused, and resumption without accumulated steps. |
| F6: legacy identity omits authored rules | Legacy hashing removes only fields absent from the source bundle. Explicit behavior, resource and equipment rules participate in identity. | Maintained legacy identity, all six optional fields, distinct behaviors, and rejection of unchanged archives under modified rules. |
| F7: character previews block the menu | One bounded background worker refreshes cached previews; menu navigation remains responsive and selected archives are validated afresh. | Cache hits/invalidation, backups and profiles, selected slot outside the 128-card bound, cancellation, menu navigation and Settings focus during refresh. |

The player-facing controls are documented in [equipment controls](gear_drag.md) and [main menu and characters](front_menu.md). The discard confirmation defaults to **Keep item**, explains that removal grants no reward, and preserves other pause reasons when dismissed.

## Verified results

- Full solution build: zero warnings or errors; formatting verification passes.
- All 462 Core tests and 17 server tests pass.
- Source Appearance diagnostic: 385 headless checks and 407 rendered checks pass. The rendered discard confirmation and completed inventory were inspected for readable text and accessible buttons.
- Source Front Menu diagnostic: all 119 checks pass, including navigation and Settings focus while catalog refresh is pending.
- Final macOS export: all 23 packaged smoke suites pass. The endgame route completes 32,190 public actions, tier-ten Fractures and all five God Hunts, including retry, abandonment, recovery, save/load, Phase 4 import and replay. The packaged Appearance, Front Menu and Settings suites pass 385, 119 and 109 checks respectively.
- Prism/Gemini reviewed the full staged diff, repeated the review with expanded context, and reviewed the cache correction separately. Its cache eviction finding was addressed by pruning entries outside the new bounded window before refresh. Other candidates were traced to existing guards, pinned Godot semantics, or unsupported execution paths. No actionable finding remains unresolved; raw reports and individual dispositions are in `artifacts/review-fixes/`.

## Reproduce

```bash
source tools/env.sh
dotnet build Ashenwake.sln --no-restore
dotnet format Ashenwake.sln --verify-no-changes --no-restore
dotnet test game/Ashenwake.Tests --no-build --no-restore
dotnet test game/Ashenwake.Server.Tests --no-build --no-restore
bash tools/export.sh
```

The Appearance and Front Menu diagnostics are included in the exported application checks. Source diagnostics can additionally render screenshots with `--appearance-smoke --capture-appearance` or `--front-menu-smoke --capture-front-menu`, each using a fresh `--output=<directory>`.

Review and local verification artifacts are retained under `artifacts/review-fixes/`. The original full-review report and reproductions remain under `artifacts/full-review/`. No archive fixtures were regenerated to mask compatibility failures.
