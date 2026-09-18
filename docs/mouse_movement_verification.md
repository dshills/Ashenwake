# Mouse movement verification

Validated on 2026-09-18 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. This change follows Verdant Maw commit `1a33d7aa4e488b9ae9cb99177148825a2c0b1b01`. Initial combined validation started from `b38d520a9accea3939dd03276a92ad6df7ccff14` before the milestones were committed separately.

## Scope and evidence

- Solution build: zero warnings and errors. Formatting verification and `git diff --check` pass.
- Click-to-move for solo and co-op, obstacle/body routing, destination ring, tight enemy picking, Shift-click primary attack, existing right-click secondary, keyboard/controller override and stop.
- Core regression suite after Prism fixes: **324 passed**, including twelve navigation cases covering actual movement, collisions, body detours, invalid targets, cancellation, bounded retries, deterministic replay, exact tangency and large squared cross products. The initial suite had 321 cases.
- The viewport diagnostic sends real mouse/key events and advances production fixed ticks. It checks obstacle arrival, no accidental attacks, zoom, marker reuse, manual override, stop, consumed menu clicks, pause/focus/controller interruption, room/session changes, save/load and combat buttons. All five recorded solo input segments replay correctly.
- Socket-free co-op checks use accepted inputs in a real `CoopCombatSession` and verify its replay. Repeated-snapshot and interruption fixtures exercise only the local input adapter; they do not manufacture authoritative gameplay or reward state.
- Focused headless input diagnostic: **83 checks passed**. Six delayed-input scenarios cover zero, two and three ticks each way, obstacle travel, 85 ticks of projected movement suppression, and rejection of a neutral input. The obstacle route with three ticks each way (200 ms round trip at 30 Hz) settles at `(1786, -5086)`, about 87 mm from `(1800, -5000)`, and remains stationary for 24 further ticks. Every arrival requires an acknowledged neutral command and the actual server position inside the 180 mm tolerance. Late acknowledgements cannot revive a cancelled route.
- Exact exported Mac app: **86 rendered mouse checks passed**, including three screenshots of departure with the destination ring, stopped arrival and an enemy-body primary click. Captures were visually inspected; the ring and updated control hints remain readable.
- Exact-package live co-op regression: **1,097 matched snapshots, zero mismatches and ten reward receipts** across all five shared encounters. Both peers authenticate over WebSocket against locally owned temporary services. The established scripted combat route exercises the production transport; actual mouse navigation and delayed arrival are covered by the separate viewport and server-input diagnostics above.
- Full exported verifier: **793 checks passed** — release 33, mouse 83, interactions 17, opening journey 42, Verdant Maw 135, services 19, character visuals 170, combat feedback 168 and appearance 126. Godot log checks and replay verification completed successfully.
- Packaged campaign/endgame: **32,190 commands**, final hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. Borrowed Memory: **2,516 commands**, final hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both hashes match the previous milestone.
- After both milestones' Prism fixes, the refreshed Mac app passed **518 affected checks**: headless release 33, mouse 83, Verdant 136 and journey 42; rendered mouse 83 and Verdant 141. The rendered diagnostic now focuses its window and settles startup notifications, using the real Resume button if needed before input assertions. These final runs omit screenshot capture. Evidence: `artifacts/mouse-movement/reviewed/regression-summary.json`.

## Review status

Local review identified co-op arrival overshoot under network delay. The fix retains the destination until neutral input is processed, brakes for queued movement and uses bounded corrections. The delayed-input regression also exposed near-goal circling after an obstacle; early braking within the queued-motion stopping radius resolved it. Follow-up local review found no additional actionable issues. These local reviews are separate from the requested Prism review.

The user explicitly approved sending these source diffs to Prism's Gemini provider and confirmed standing Prism approval. Initial review `5cc3d32c9e2c16d18965dc4900e81f01` reported one medium and one low performance finding. Both were fixed: body clearance now uses exact integer projections and Int128 cross products, and co-op history pruning only runs when the processed input acknowledgement changes.

Follow-up review `f260a5292d338f8e04a162c0dd2bf1cf` reported one medium and one low finding. The query-limit warning is a false positive: the 466-vertex maximum yields at most 108,345 unordered edge checks, below the existing 131,072 cap. The buffer-pooling suggestion is a bounded optional optimization: the three arrays use at most 6,058 payload bytes per blocked-route search, with automatic replanning at most 3.75 times per second and much smaller shipped rooms. There are no outstanding actionable findings. Full dispositions are in `artifacts/mouse-movement/prism-disposition.json`.

## Artifacts and limits

Build, test, render and package evidence lives under `artifacts/mouse-movement/`. Core movement rules, save formats, replay commands and network contracts are unchanged. Automated checks do not certify performance on other hardware or network conditions outside the tested delay cases. Controls and reproduction steps are in [mouse movement](mouse_movement.md).

- Initial Mac package, before the Prism fixes: `artifacts/mouse-movement/Ashenwake.zip`, SHA-256 `ec1c60f88c87326d04b8342b7c81d3feb3383c7b0b333effb2665bf468461277`. The full 793-check run and live co-op evidence above refer to this build.
- Refreshed package including both milestones' Prism fixes: `artifacts/mouse-movement/reviewed/Ashenwake.zip`, SHA-256 `4c230762eac508236d1916dc5f170372d04b0fbb864b4f9a97a39e5d0834838a`.
- Exact-package rendered report, latency cases and captures: `artifacts/mouse-movement/package-rendered.GeTBDI/`.
- Live co-op package report: `artifacts/coop-package/run.AAL8f8/report.json`.
