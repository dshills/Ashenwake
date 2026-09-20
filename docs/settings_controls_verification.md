# Settings & Controls verification

Baseline: `be37ecb`. Toolchain: pinned Godot 4.6.2 Mono and .NET 8.0.425 on macOS Apple Silicon.

## Scope and review

This phase replaces the legacy compact settings column with Controls, Audio, Accessibility and Gameplay tabs. Existing gameplay rules, character schemas and content remain unchanged. Local preferences gain optional audio fields, validated keyboard conflicts and clear failure notices. Existing combat and environmental voices use independent buses; the only new audio asset is a short synthesized interface cue.

Independent integration review found that keyboard focus could leave the modal at its boundaries and that character-action shortcut captions remained fixed after rebinding. The final implementation gives enabled visible Settings controls explicit focus neighbors and refreshes those captions and combat HUD hints from the current bindings. Native tests cover forward/reverse Tab boundaries, directional boundaries and activation after wrapping.

The new diagnostic drives ordinary startup, controls and sliders through viewport input, using fresh isolated settings fixtures and one new character. It tests backup recovery, restart persistence, malformed or out-of-range audio values, old settings without audio fields, failed writes retaining live values, tab-specific restores and pause ownership. The rarity OptionButton uses its existing public selection/signal contract; headless popup keyboard events are not claimed. Greyhaven intentionally has no regional ambient voice; the four existing regional suites verify actual Music-to-Master routing when those voices are created.

## Final verification

- The solution builds with zero warnings and errors. Formatting verification passes.
- All **434 Core tests** passed, with zero failures or skips.
- The existing release/pause diagnostic passed **33 checks**, including focus-loss, manual pause, overlapping modal owners, settings recovery and local diagnostic export.
- The rendered source run passed **127 checks** with **18 screenshots** and **four fresh-director preference reloads**, at 1280×800, 1000×720 and 780×720. Initial evidence is in `artifacts/settings/rendered.88A8jj`; the same checks passed after the Prism layout correction in `artifacts/settings/prism-source.PHbIgx`. Native input covers duplicate/cancelled/replaced bindings, keyboard slider adjustment, defaults, focus boundaries, focus-loss cancellation and gameplay pause isolation. Visual inspection confirmed compact Controls/Accessibility/Gameplay layouts, readable audio percentages and clear conflict/write-failure feedback.

An earlier native run aborted inside CoreAudio's mixer during rapid volume changes. Inspection found that the bus helper repeatedly reassigned unchanged routing. The final helper skips unchanged sends and protects actual send changes with the audio driver lock. Godot's pinned [`set_bus_send` implementation](https://github.com/godotengine/godot/blob/4.6.2-stable/servers/audio/audio_server.cpp#L960) does not take that lock itself; the [4.6 AudioServer documentation](https://docs.godotengine.org/en/4.6/classes/class_audioserver.html#class-audioserver-method-lock) describes the explicit lock/unlock contract. The subsequent complete source run used real CoreAudio and passed. This removes the repeated unsynchronized routing writes; a single prior crash does not establish that it was the only possible engine-level cause.

## Full package verification before the layout correction

- All **23 packaged client suites** passed **2,692 assertions** from `artifacts/package/22f02d6da01b.Qz0RYs`, including 109 new headless Settings checks. The existing full campaign/endgame hash remained `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`; the Echoes replay hash remained `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`.
- A fresh extraction of the same ZIP passed **127 rendered Settings checks**, **18 screenshots** and **four preference reloads** with real CoreAudio. Its non-capture checks match the headless package and rendered source exactly. Evidence is in `artifacts/settings/exact-package.ipw2pyat/settings`; visual inspection confirmed compact Audio, full Controls and in-game pause layouts.
- This initial archive is preserved as `artifacts/settings/pre-prism.Ashenwake.zip`, with SHA-256 `94280fde0cad2b93e7bc1a4a5bf1b007cd1d58234ee66442647e80714f47ce95`. Its original complete suite record is preserved in `artifacts/settings/pre-prism-verification.json`.
- These local artifacts are intentionally ignored by Git. Final archive identity and targeted checks after the isolated layout correction are recorded below.

## Prism review

Prism/Gemini review `e0efc185970a05ef495afdbcc03b51bc` reported one high performance finding and one medium correctness finding.

- **Repeated layout work (`4803a6a0771e5c82`): fixed.** Closed Settings now skips layout work. The visible panel caches its target geometry when the viewport changes and only writes Size or Position when the actual value differs. Comparing actual geometry still permits recovery when wrapped text changes the panel minimum during layout settling. The native three-resolution Settings regression passed all 127 checks after this correction. No measured frame-time regression was supplied with Prism's high severity rating.
- **Competing HUD positioning (`41712e2cc728f779`): not present.** `SandboxHud.cs` already limits its old panel-positioning loop to `_inventoryPanel`; it never positions `_settingsPanel`. The cited lines create HUD buttons rather than setting the Settings panel's geometry. The settings layout has one owner.

The follow-up Prism review `6898c6209b183c85653377132b73ce28` reported **zero high, zero medium and two low findings**. Both are accepted nonblocking cleanup suggestions:

- `9a6563ac81edeee6`: the private `playSound` parameter in `SelectSettingsPage` controls focus; interface sound is emitted by the button wrapper. Renaming it would improve internal naming without changing behavior.
- `2c6bb78ce4edf35d`: recursive iterator allocation in the bounded focus traversal is a possible optimization. It runs on menu opening/tab selection, not every frame. No measured regression or unbounded traversal was reported; the native focus-boundary checks pass.

Both reviews used the approved Gemini provider with default secret redaction. No blocking finding remains.

## Final archive after Prism

The only runtime correction after the full 23-suite run caches Settings layout targets and skips hidden/unchanged writes. The affected paths were rechecked on the rebuilt ZIP: **33 release/pause checks**, **115 main-menu checks**, and **109 headless Settings checks**, totaling **257 checks**. The final ZIP also passed all **127 rendered Settings checks**, **18 screenshots** and **four preference reloads** with CoreAudio. Its Settings assertion set matches the corrected source run exactly, and its non-capture checks match the final headless run. Evidence is under `artifacts/settings/exact-prism.BND2UJ`.

The final playable archive is `artifacts/settings/Ashenwake.zip`, byte-identical to `artifacts/export/Ashenwake.zip`, with SHA-256 `7ea12b5d0d961ade4a5bd0f8af4796563b633e9da190d1befcca5628ccdc5247`. `artifacts/settings/verification.json` distinguishes the initial full-suite package from this final targeted verification and records both Prism review IDs. The solution build and formatting verification also pass after the correction.
