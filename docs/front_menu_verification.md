# Main menu verification

Baseline: `ac20755`. Toolchain: pinned Godot 4.6.2 Mono and .NET 8.0.425 on macOS Apple Silicon.

## Scope

The main menu, visual discipline selection and saved-character browser use the existing character preview and actual Core-backed appearance/ability data. New characters receive unique destinations. The separate Echoes wrapper is preserved when loading through Characters or Continue, and an original-slot sidecar protects return routing. No Core gameplay rules, content catalogs or archive schemas change.

The native diagnostic uses the shipping EndgameDirector and real viewport mouse/keyboard events. It checks three window sizes, all five discipline previews, modal pause and Settings isolation, unique durable creation, original byte preservation, actual save failures, corrupt/backup/future-version behavior, and original/Echoes continuation. One real gameplay command is replayed exactly. The maintained Phase 4 fixture provides an earned Echoes origin without fabricating progression.

An independent persistence inspection found that replay verification still used the default character filename, startup profile loading preceded the catalog's byte guards, and Settings could expose gameplay actions behind the main menu. These paths now use an excluded diagnostic checkpoint, bounded profile reads, and a Settings backdrop with disabled gameplay actions. The new native route explicitly exercises the default-save preservation and Settings paths.

## Prism review

Prism/Gemini review `bea203b38ee3656f11e150cc56d0f5d0` reported two medium and one low findings, with no high findings.

- The alleged load-failure pause lock refers to a `Pause(true)` call that does not exist in the changed route. Failed loads preserve the current session and the menu's explicit pause owner. Unpausing on failure would allow the world to run behind the menu. Native failure/retry/resume coverage checks the intended lifecycle.
- Scanning on menu entry is bounded to 128 cards and uses actual archive validation so external save/backup changes are visible. It does not run every frame or when simply selecting a card; full validation is repeated before switching sessions. A cache or asynchronous catalog is a future scaling optimization, not evidence of corrupted state or a measured gameplay regression.
- The additional existence checks cover a selected character outside the capped roster. They retain access to that Continue target while avoiding a phantom default character in a fresh folder; they are not redundant with an exhaustive scan.

The follow-up Prism review `ab5ef3f4424063fb3ad84c5db673e39a` reported one high, one medium and two low findings. The high finding claims the `--continue` implementation is missing; the actual `_Ready` branch explicitly calls `ShowFrontMenu()` and `PlayCharacter()` with the recent selector or legacy fallback. The native restart checks independently exercise that same loader for ordinary and Echoes archives. Its input-leak concern is also contradicted by the first guard inside `BlockFrontMenuInput`, which checks visibility and modal context before processing input. The remaining findings recommend reducing small filename-parser allocations and abstracting the two archive loaders. Those are maintenance/scaling suggestions; explicit wrapper selection remains covered by the native cross-character and restart checks.

## Source verification

- All **434 Core tests** passed, with zero failures or skips.
- The ordinary interaction route passed **43 checks** using the new startup controls and selected save filename.
- The final rendered menu run passed **142 checks** with **27 screenshots** at 1280×800, 1000×720 and 780×720. It includes two fresh-director restarts, real failed-save/failed-quit behavior and separate Echoes ownership after another character is played.
- The actual gameplay command retains replay hash `F34F379FB2B8D13C5E396E9E154F8F4FD411784E6C97776D72A0E1951F483062`. Menu selection and preview changes create no gameplay commands.
- Visual inspection checked the main menu, compact character browser and discipline screen, Settings backdrop, and readable save-failure recovery. The build has zero warnings/errors. Successful application shutdown is separate from the diagnostic's failed-quit check.

The existing Echoes screen diagnostic passed **163 checks** after updating its failed-save fixture. That fixture previously changed the active save filename, which now correctly changes the identity used to find the character's Echoes journey. It instead preserves the actual filename and original archive bytes, temporarily blocks that same destination with a directory, and restores it in `finally`. This exercises a real save failure without bypassing the new ownership check. The first package attempt exposed this outdated fixture; the final package suite was restarted with the correction included.

## Final packaged verification

- All **22 packaged client suites** passed **2,583 assertions** from `artifacts/package/22f02d6da01b.Xu6ngG`. The endgame run retained its 32,190-step hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`; the existing 2,516-command Echoes replay retained `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`.
- A fresh extraction of that same ZIP passed **142 rendered menu checks** and produced **27 screenshots** in `artifacts/front-menu/exact-package-final.r4sc0rib/front-menu`. Its 115 non-capture checks and one-command replay match the headless package run and the final source run. Inspection confirmed readable main, discipline, character, Echoes continuation and failed-save screens, including the compact layout.
- Two independent launches of the extracted executable with `--continue --echoes` loaded isolated copies of the original and Echoes saves. `--echoes` only opened the board after Continue so the screenshots explicitly identify **Vanguard · Level 10 · Original character** and **Vanguard · Level 10 · Echoes character**. Both runs exited successfully with checked logs, and the copied archives/profile/sidecars remained byte-identical. Evidence is in `artifacts/front-menu/cli-original.3bubfvm9` and `artifacts/front-menu/cli-echoes._9yqscke`. These direct CLI launches additionally resolve Prism's missing-Continue allegation; their bounded diagnostic exit does not exercise the native window-close or successful Save & quit gesture.
- The verified playable archive is `artifacts/front-menu/Ashenwake.zip`, identical to `artifacts/export/Ashenwake.zip`, with SHA-256 `49bbe9536aa6e5595e689dafd3665f778bc1265a9bea6fd1825a8aec37b88441`.
- `artifacts/front-menu/verification.json` records the package identity, suite counts, replay hashes, source/rendered agreement, direct CLI evidence and Prism review IDs. Local artifacts are intentionally ignored by Git.
