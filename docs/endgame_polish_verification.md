# Endgame combat polish verification

The diagnostic separates rendering and mechanic fixtures from earned campaign-to-endgame progression. Its modified encounters are not evidence of normal survival, reward pacing, or human playtesting.

## Native presentation diagnostic

`EndgameCombatPolishSmoke` is an explicit exported entry point:

```sh
Ashenwake -- --endgame-polish-smoke --capture-endgame-polish --output=<fresh-directory>
```

It instantiates the shipping `Sandbox`, `EndgamePresentation`, collapsed `EndgameHud` objective, and local exploration map. The map uses the actual arena with fixture-only full discovery to exercise the compact map's occupied screen area. The diagnostic creates no campaign or endgame completion claim.

Fixtures cover all fifteen phases of the five God Hunts, actual phase warning creation, ordinal attack sequences versus simultaneous lane numbers, priority weak points and mechanisms, temporary vulnerability expiration, permanently broken shields, and the six Fracture modifier cues. Mechanic transitions use public combat and mechanism commands. Fixtures use a level 20 reference character with Offense 8 and Defense 8, no equipment or fragments, explicit temporary invulnerability, controlled enemy health, and some suppressed enemy decisions to isolate the visible condition. A separate crowded HUD fixture combines the maximum three modifiers with two inherited boss traits and explicitly projects Cinder Cycle, Widow Echo, and barrier-on-dodge properties. Public spender/generator/dodge commands create the real legendary trigger, readiness, and barrier status while the boss is actually focused; these property projections are not earned equipment. Save and command replay checkpoints verify identical combat state.

For each inspected state, the diagnostic switches High, Performance, Reduced visual effects, and Performance with Reduced effects at 780×720. It checks world labels and the real objective, rule panel, compact local map, focused target card, visible legendary readiness/trigger labels, and player status strip for visibility, viewport containment, and overlap. Named danger labels preserve stage/countdown and true temporal/lane metadata. The paused-state and input-independent rendering must not mutate the combat state.

Native capture uses bounded settle/resume attempts and synchronous `RenderingServer.ForceDraw(false)` / `ForceSync()`. The exact frame is checked for blocking modals before reading the viewport; each PNG must match the requested viewport dimensions. A fresh output directory is mandatory. Cosmetic layout continues through container-reflow frames while the detached command recorder remains the sole gameplay clock. The optional `--only=<lowercase-phase-pattern>` or `--only=modifiers` filter is explicitly recorded as partial diagnostic coverage and is never accepted as the full native result. Headless execution explicitly skips screenshots and does not substitute for native visual inspection.

## Validation discoveries

- At 780×720, the original 81-position label search could leave the third Oathless Slam warning overlapping Orrun's protection label. The bounded search now includes 169 candidates and favors an unobstructed position over shorter movement. Leader lines continue to connect shifted captions to unchanged hazard geometry.
- Session-replacement checks populate campaign warning anchors, replace the session with an endgame encounter, and confirm that old anchor references and nodes are removed. Endgame warning/mechanism roots are detached before deferred destruction, so reused object IDs cannot render stale cues alongside their replacements.
- The full matrix includes a 100-frame CPU sample around manual `Sandbox._Process(0)` on the crowded Orrun third-phase minimum-window state. This measures all sandbox presentation work, excludes GPU/renderer costs, and does not certify a hardware frame rate.

## Results

The accepted packaged native result is `artifacts/endgame-polish/presentation/native-final-03/endgame-polish-review.json`:

- 2,654 checks passed across all fifteen hunt phases, all six Fracture modifiers, and the combined modifier/property/focused-boss stress fixture.
- 2,487 public command steps and 47 independently saved/restored and command-replayed checkpoint pairs passed.
- 154 PNG captures passed exact header/dimension checks, including 39 at 780×720 and 115 at 1280×800. Dimensions are recorded in `native-final-03/capture-dimensions.json`.
- No checks were skipped. The strict native Godot log check passed with no warnings or errors.
- The 100-frame native whole-sandbox CPU sample measured mean 0.1083 ms, 95th percentile 0.1278 ms, and maximum 0.1562 ms. These static presentation samples exclude renderer/GPU work and do not establish gameplay frame rate.

The tested macOS archive is `artifacts/export/Ashenwake.zip`, SHA-256 `7ffa3042becfb41678475979c3047d0de402abb399af1623de0465e70570660d`.

The earlier full headless logic pass (`presentation/headless-final/endgame-polish-review.json`) passed 2,346 checks, the same 2,487 command steps and 47 checkpoint pairs, and the strict log check; screenshots were explicitly skipped. Native-only settling corrections subsequently consolidated screenshot assertions behind the bounded unpaused, unobstructed-frame gate. The accepted native run verifies that final diagnostic path. Earlier `native-final` and `native-final-02` directories are discarded diagnostic iterations, not accepted full results.

Selected native screenshots were manually inspected: combined modifiers at minimum size, active healing echo and persistent scar at minimum size, Vael's timed exposure at High, Nhal's bell exposure at minimum size, and Orrun's third-phase warnings at minimum size. The objective, rule cues, priority/sequence/vulnerability instructions, real target card, compact map, legendary readiness/trigger, and barrier chip fit their intended regions.

The detached renderer uses zero cosmetic delta while holding each combat snapshot. Floating damage, healing, and death captions can therefore remain visible over a required cue in these static captures; normal feedback expires after 0.65 cosmetic seconds. This is an explicit artifact limitation: rectangle assertions cover named encounter/actor/mechanism labels and HUD controls, not transient floating combat text or continuously animated gameplay. The captures and command fixtures do not replace a human combat-feel or difficulty playtest.

## Automated mechanics and earned progression

The full Core suite passed 1,213 tests, and the server suite passed 17 tests, with no failures or skipped cases. The final focused readability suite passed 61 cases, including combined control effects and actual movement, all fifteen phase transitions, immunity and exposure boundaries, warning ordinals, copied/dead bosses, save/restore, replay, and view purity. The focused run includes later additions and overlaps the full suite; these counts are not added as distinct tests. Reports are `artifacts/endgame-polish/tests/core-full.trx`, `server.trx`, and `artifacts/endgame-combat-polish/tests/endgame-readability.trx`.

The endgame content compiler and final solution formatting verification passed. No content identities, snapshots, encounter numbers, damage values, or timers changed.

All fifteen fresh campaign-to-tier-three routes passed, covering five disciplines and three seeds: 145,333 commands, 788 verified replay segments, 180 committed rooms, and 45 final rewards. There were no deaths or failed routes. XP and material accounting reconcile exactly. No blocking progression spike was observed under the declared earned-build policy, so no numeric balance changes were made. See [earned progression verification](endgame_progression_verification.md) for tier measurements, policy identities, and the limits of scripted difficulty evidence.

## Prism review

Prism reviewed the Core projections, the complete staged implementation, and the final presentation/tooling follow-up using Gemini with default secret redaction. The follow-up included the aggregate verification script. There were no high-severity findings. Every reported concern was checked against the implementation and recorded in the corresponding disposition file under `artifacts/endgame-polish/`:

| Review | Run ID | Outcome |
| --- | --- | --- |
| Core | `a06192407fd83b529da69505454f39a2` | The narrow-arena case is rejected by content validation; the existing bounded hazard lookup was unchanged. |
| Staged implementation | `0cfa0ac5033c6fab8e7c8843e701cc36` | Removed per-candidate LINQ closure allocation. Documented leader geometry, measurement scope, logical viewport coordinates, and diagnostic reflection. Verified cumulative XP and event-gated inventory observation. |
| Final presentation/tooling follow-up | `796b3cbec73f26d07fc00f04d323742e` | Verified owned standard materials, the shipping orthographic camera, and synchronous private-dictionary removal; the proposed failure paths do not occur in these implementations. |

Raw reports are `prism-core.json`, `prism-staged.json`, and `prism-followup.json`; matching `*-disposition.json` files retain finding-by-finding rationale. No actionable review issue remains. Final diagnostic-only native settling adjustments and documentation do not alter the reviewed production behavior.

## Packaged regressions

The existing diagnostics were run sequentially in the same final macOS package. All three exited successfully, passed the strict Godot log check, and retained their reports and screenshots under `artifacts/endgame-polish/`:

| Diagnostic | Checks | Public commands | Native captures | Report |
| --- | ---: | ---: | ---: | --- |
| Midgame combat | 209 | 474 | 27 | `native-midgame/midgame-combat-review.json` |
| Late campaign combat | 675 | 573 | 69 | `native-late-campaign/late-campaign-combat-review.json` |
| Expedition board and transactions | 195 | 6,599 | 31 | `native-expedition/expedition-review.json` |

The combat regressions had no skipped checks. The expedition regression exercised 17 board requests and verified all 6,599 commands in five replay segments, including recovery, entry, retries, abandonment, attunement, room advancement, rewards, and return to Greyhaven. It uses the maintained campaign-complete fixture and earns the subsequent expedition outcomes; the separate fifteen-route measurement establishes fresh-campaign progression. Breach Heart phase-three warnings at minimum size and the expedition earned-rewards screen were also visually inspected after the binaries exited.
