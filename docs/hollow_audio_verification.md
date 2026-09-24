# Hollow Night audio verification

This pass completes adaptive presentation audio across Acts I–V without changing Core rules, content, commands, RNG or archive formats. Local evidence is under `artifacts/hollow-audio/`.

## Completed checks

- Pinned .NET build: zero warnings or errors.
- Focused campaign combat, Hollow exploration/rooms and late-campaign combat suite: 158 tests passed.
- Expanded headless signal diagnostic: 864 checks passed across 72 music stems and 58 effects, with 130 audition WAVs. All 107 earlier asset fingerprints match the previous Spine package exactly.
- Final exported macOS shared audio diagnostic: 878 checks and six captures passed with clean logs. All 107 earlier asset fingerprints and 20 earlier summed-score fingerprints also match the Spine package.
- Spine regression: 106 checks passed, including its earned route, sequential warnings, saves and replay.
- Final exported headless Hollow route: 109 checks passed across 8,606 public commands through the vault, all three Heart phases, seal exposure, containment and final hub/ending return, including the cancellation fixtures. Save and replay hashes match `4ECF6CD4A591B3831FEAE5B1FCCB55429E8B976701B90CA1EE3B9D222F7D47D3`. The earlier source run passed 103 checks before those additional fixtures were added.
- Final exported macOS Hollow diagnostic: 124 checks and 12 captures passed with a clean shutdown log. This includes the added cancellation fixtures, advancing aligned native playheads, pause/resume, copy isolation and the entire earned route. Its save/replay hash matches the headless route and independent CLI replay.
- Independent integration review verified primary-boss identity across Mirrorborn copies, shield exposure, three-phase score gains and resolution-driven warning sequencing. No actionable findings were found.
- Final build, export, formatting, shell syntax and diff checks passed. Accepted ZIP SHA-256: `288c55df210ec559fca4084bab559dc45c322360bf8a288c01e8c1c459480162`.

Early native Hollow runs passed their functional checks but failed the clean-log gate on two sky texture allocations at shutdown. Final-frame waiting and explicit collection did not resolve it; those runs remain failed evidence. Temporary tracing accounted for all 13 sky creations and releases. The first short-lived fixture crossed the global draw-frame counter without receiving the existing entry-frame teardown flush, consistent with the pinned renderer's previously documented sky lifetime issue. The diagnostic now keeps one fixture world alive across cases and lets it render before testing. Each case still restores a separate combat state and resets audio; only the presentation world is reused. The final exported run is clean. No changes to shipping graphics cleanup or log filtering were needed, and all tracing was removed.

## Prism review

Prism/Gemini run `e264dde0d0093f254781b23360f2c118` reported two high findings, both incorrect build assumptions. `Directory.Build.props` enables implicit usings, and `HollowAmbience.cs` is an existing tracked class with `CueForStyle`. The successful full build confirms both references. Detailed dispositions are retained in `prism-dispositions.json`; neither requires an implementation change.

Prism follow-up `a7b753c2de24de8e126ba1fa4eca14d3` reported no high, two medium and two low findings on the cancellation fixtures. Those fixtures run once per captured key after the route, deliberately deep-copy mutable state, and wait for deferred scene cleanup. Captured follow-ups already require a pending hazard, so requiring at least one removed hazard prevents a vacuous pass. The solo player-ID check follows Core’s explicit invariant. No actionable findings remain; dispositions include the relevant execution paths.

Cleanup reviews `a379326dc4312090a4dbd13e48fae96d` and `381158424e4f43a9c3395994023ab591` found no high findings. The latter review's two medium findings assume fixture cloning and frame waits happen for every campaign hazard; the native report records only two cancellation fixtures after the entire route. Its low findings assume orphaned stage nodes, absent future hazards and a nullable fixture in `finally`: both stage nodes are direct children, captures require a future hazard, and construction precedes `try/finally`. The earlier suggestion to block on finalizers is inapplicable to the final draw-based cleanup, which does not call GC. Detailed dispositions remain in the local review evidence.

Final fixture review `4541ec64082b1218a8b8c0b3fd865ab1` reports no high or medium findings and two low findings. The diagnostic runs one smoke per process and always quits on failure, so there is no later test sharing retained nodes. Its dedicated root owns every non-internal child returned by `GetChildren()`; no unrelated gameplay nodes are destroyed by cleanup. No actionable findings remain.

## Boundaries

The Heart’s original actor is selected by its minimum ID, including after death. Detached fixtures explicitly test that surviving copies cannot inherit the score, phase, exposure or containment cues. Captured-event fixtures and projected-state fixtures remain separate from the earned campaign replay.

PCM headroom, seams, repeatability and native playhead advancement are automated evidence rather than listening assessments. Perceived composition, headphone/speaker balance and the combined legacy/new-effects mix still require listening in the exported game. The full-campaign listening and balance pass remains separate work.
