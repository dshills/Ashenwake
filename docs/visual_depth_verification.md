# Visual depth verification

This client-only phase uses .NET 8.0.425 and Godot 4.6.2 Mono, with the native macOS Compatibility renderer on an Apple M4 Pro. Core, content catalogs, collision, saves and replay formats are unchanged.

## Rendered evidence

Baseline captures are in `artifacts/visual-depth/before-journey/` and `before-characters/`. The first enhanced package passed **463 Journey checks** and **174 character-gallery checks** with clean logs, including all model definitions, paused animation, geometry/material limits, independent accents and shared mesh templates. All catalog models remain at most 22 meshes and seven materials; the cache contains 54 templates and 159 base materials.

Before/after inspection covers Greyhaven, the road, monastery, Bell Saint and monster lineups. The pass adds visibly recessed paving, pools of light around existing flames, folded garments and shaped armor. A subsequent visual adjustment softened the moss color and replaced square patches with overlapping low facets.

The initial matching Greyhaven frame samples (90 frames per preset, capped at 60 FPS) measured median/p95 **16.665/16.717 ms** on High and **16.668/16.802 ms** on Performance. Baseline values were **16.664/16.918 ms** and **16.663/17.060 ms** respectively. Initial draw calls increased from 391 to 419 and rendered primitives from 83,445 to 93,493. These are local frame-pacing samples, not GPU benchmarks or platform certification.

The Journey route reproduces the baseline's two replay segments exactly: 135 commands ending at `709D2A474C86C9D5BE5083DEDCD6DE7974BF39D2A65E2482E4ACAAB5D3CEED59`, then 977 commands ending at `3D05E54C66CD4BCB7B69A3FF8BC90AD5663EB1FA4A25B50A62CA97E3420F25D8`.

## Regressions and review

`OpeningLightingChecks` exercises all five opening styles, finite positive bounds, stable node identities, both quality limits, pause/resume, immediate Reduced Effects, ten simulated minutes of bounded light variation, and absence of physics/navigation nodes. Journey exercises the actual settings controls and room lifecycle, including deferred native-node release. The earned opening-audio route also inspects the optional crypt's geometry, walkable approaches, visible interaction targets and lighting using the same checks as Journey.

The first appearance run exposed a native drag-cancellation crash on application focus loss during drop dispatch. It is not counted as a pass; its log is retained under `after-appearance/`. Cancellation now invalidates stale transactions immediately and defers native preview removal until Godot finishes dispatch. The queued cancellation is restricted to the original drag's owner and epoch.

Initial Prism/Gemini review `3d72f475902d3448670ec434b6b9bfed` reported one medium and one low finding. Both were inspected against the integration code:

- Alleged repeated character construction: the private builders run only while creating a new `CharacterVisual`. `SynchronizeActor` removes and frees the prior actor root when appearance changes. `Finish` and `BuildModule` retain the established bounded mesh batching/cache behavior; the proposed accumulating-node path does not exist.
- Alleged per-paver draw calls: `EnvironmentBuilder.Flush` already merges all primitives of a material into one static mesh. The cited path `game/Ashenwake.Common/Building/OpeningGround.cs` does not exist. Actual native ground-budget and frame-pacing checks cover the increased geometry.

Follow-up Prism/Gemini review `cff15e1789be1d63f1b5717852bdf945` reported two medium and two low findings. Its input-redispatch concern prompted an additional guard: a queued menu event is discarded if a newer native drag has started. The reported `Input.ParseInputEvent` call is actually deferred `Viewport.PushInput`, but explicitly excluding a current drag strengthens that boundary. The remaining findings cite nonexistent `CreateRobeSkirt` and `Performance.GetQuality()` methods, or suggest an `IsInsideTree()` guard that already precedes `GetViewport()` in `CancelDrag`; no corresponding defect was found.

Final targeted drag review `01e4df8c452cd6f59ef18eb10c6fce51` reports one medium and four low findings. The medium concern objects to revision-based stale-drag rejection, which is an intentional pre-existing transaction boundary: the active drag pauses gameplay, and an explicit restore/service/ownership change must invalidate the old payload. The native stale-payload test verifies this. Three low findings propose optimizing existing signature/owner string work or replacing existing diagnostic reflection; no measured regression or correctness defect is supplied. The fourth incorrectly places action-map lookups on mouse motion; that code executes only in the key/joypad-button branch. No actionable finding remains unaddressed.

## Final graphics package

The softened-moss package passes Journey **463 checks** (`final-journey/`) and the earned crypt/audio route **307 checks** (`final-crypt/`), both with clean native logs. After the final input guard and race regression, the rebuilt/extracted app passes **684 equipment/appearance checks** (`accepted-appearance/`) with a clean log and 33 captures. Graphics/accessibility Settings passes **162 checks** (`after-settings/`), and the unchanged final character geometry passes the **174-check** gallery (`after-characters/`). These are 1,790 checks across the selected native suites; the final guard changes only the equipment input path, which was rerun in full.

The five authored grounds remain within the 40,000-triangle/48-material budget: Greyhaven 15,948 triangles / 32 batches; road 10,248 / 14; monastery 13,512 / 13; sanctuary 24,876 / 12; crypt 28,752 / 13. Their maximum vertex height is -0.004 m. Real click-route, interaction visibility, collision-footprint, batching and Core-state checks pass. The crypt route earns all 1,725 commands and preserves the pre-pass hash `0944B2C0B491A20D63C769A2B94DC7473DE22D83AC9FC639969B4B4BAAD9800D`.

The CLI independently replays `final-crypt/opening-audio.awcampaign` with the same hash; `crypt-replay.log` records the result. The final appearance suite injects focus loss during native drop dispatch and separately starts a new native drag before queued cancellation/menu callbacks, verifying that neither stale transactions nor stale menu events execute.

Final Greyhaven samples are median/p95 **16.660/17.150 ms** on High and **16.664/17.332 ms** on Performance, with 419 draws and 98,869 rendered primitives. Both remain at the local 60 FPS cap; no wider hardware performance claim is made.

Solution build and formatting verification pass, and content compilation and automated release-engineering checks pass. Existing external release acceptance gates are unchanged.
