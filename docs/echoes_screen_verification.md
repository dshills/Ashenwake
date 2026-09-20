# Visual Echoes verification

Baseline: `cc95402`. Toolchain: Godot 4.6.2 Mono and the pinned .NET SDK 8.0.425 on macOS Apple Silicon.

## Scope and authority

The Echoes board adds side-by-side Keep/Borrow cards, owned-Sigil selection, actual contract history and earned-cosmetic presentation, and explicit original/Echoes character navigation. The retained active-memory controls project Core status, source, suppression, availability, remaining ticks and Storm stages. The modal has its own pause owner and fits 1280×800, 1000×720 and 780×720.

All entry, bind, release, cast, progression and save actions retain the existing experiment wrapper. No Core rules, content, save schemas, timers or reward conditions change. Views and selection do not grant items or invoke commands. Character continuation validates the destination, saves the original, then switches; an exception stops that sequence and remains visible in the current modal.

## Rules and regression evidence

- All **434 Core tests** passed with no skips (`artifacts/echoes-screen/core-tests.log`).
- The existing Echoes client route passed **2,516 commands**, plus archive replay verification, with unchanged state hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71` (`artifacts/echoes-screen/legacy.MplGRH`).
- The screen diagnostic uses the shipping director, actual native buttons and isolated files. The immutable Phase 4 fixture supplies an earned character; subsequent Sigils, memories, elapsed combat time, Storm actions, contract outcomes and cosmetics come from ordinary runtime commands. Exact replay segments cover every branch. Failure evidence is retained alongside successful runs.
- The complete native run passed **163 checks**, covering **3,148 commands** in **eight replay segments** (`artifacts/echoes-screen/source.cFtbVA`). The final rendered source run passed **191 checks** and wrote **28 screenshots** (`artifacts/echoes-screen/rendered-final.ihNVUH`), including successful blocked-save preservation. Visual inspection confirms that the compact Storm warning and escape instruction remain visible, and the status notice correctly reports restored Mind effects after using the Echo.

## Review

Prism review `220dd3a125bf08760fcc540a882aebf4` used the approved Gemini configuration and `tools/prism-implementation.json`. It reported one high, one medium and three low findings.

- The high save-failure allegation does not follow the execution path: `Save()` calls throwing archive writers, and the outer `Safely` handler catches only after the action unwinds. A failed write cannot continue to the subsequent session assignment. The screen diagnostic exercises a real blocked destination and checks both character preservation and a visible error.
- The prerequisite/duplicate-validation findings concern read-only affordances and defensive click validation. The director still submits the transaction to `ExperimentRuntimeSession`, which enforces ownership, admission, location and rollback. Client checks cannot authorize a rejected Core action.
- The board rebuilds on opening or changed presentation state. It remains paused while open, skips unchanged keys and does not reconstruct its contents during closed gameplay. The active-memory controls retain nodes and cache text/style updates.
- Tick displays now use `FixedStepClock.SecondsPerTick` instead of a separate hardcoded simulation frequency.

Independent inspection also corrected an internal actor-number source label and the empty-Mind loan description. The first native layout run exposed a transient wrapped-label minimum height; the board now reapplies its target bounds after containers settle. The diagnostic follows the real expedition-review close flow before attempting active memory actions.

The follow-up Prism review `9bb91a59ae9ad53749411741e8a40d11` reported one medium and one low finding, with no high findings. Its rules-cache concern conflates admission policy with immutable rules: `WithAdmission` deliberately retains the rules hash and values, and the client does not hot-reload mechanical definitions. Cached rules therefore remain valid while retired-entry controls refresh. The layout observation concerns bounded scalar comparisons each frame; they skip unchanged control writes and allow recovery from Godot's initial wrapped-label minimum size. Reverting to a viewport-only cache reproduced the native containment failure described above.

## Package and visual evidence

- `tools/export.sh` passed all **21 packaged suites** and **2,468 native assertions**, including the new Echoes screen route. The endgame route retained its **32,190 commands** and state hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`; the existing Echoes route retained the hash recorded above.
- A fresh extraction of the final ZIP passed **191 rendered checks**, captured **28 screens**, and matched all **eight replay segments / 3,148 commands** from both source and packaged headless runs. Inspected images include the compact bound-memory actions, complete Storm escape instruction, contract comparison, and earned cosmetic/history.
- The build has **zero warnings and errors**; `dotnet format --verify-no-changes` and Git whitespace checks passed. The deliberate blocked-save case produces an expected warning while retaining the current character and displaying the failure in the modal.
- The verified macOS archive is `artifacts/echoes-screen/Ashenwake.zip`, SHA-256 `4680e89195c8d2da2fb016b9b08b99f0e80d910811c31d8743eb350061378c17`.

Final package paths, rendered screenshots, checks and archive identity are recorded in `artifacts/echoes-screen/verification.json`. These tests establish local behavior and replay/persistence consistency; external player acceptance and other hardware remain separate evidence.
