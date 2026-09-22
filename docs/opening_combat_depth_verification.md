# Act I combat-depth verification

Baseline source: `af84cf7` (Hollow Night depth). Validation uses .NET 8.0.425 and the exported Godot 4.6.2 Mono macOS app on Apple M4 Pro, Compatibility renderer.

## Gameplay and compatibility

The change authors new Road, Monastery and Crypt formations, adds the monastery's Dirgebound ward, and guides the earned Pyrebound Treads and Heart of Serath upgrades. Enemy stats, room collision geometry, boss mechanics and reward amounts remain unchanged. Campaign identities advance to `campaign.opening_depth.8` / `campaign-combat.opening_depth.8`.

The focused combat suite passes **34 cases**, including twenty actual starter-build victories across all five disciplines and the four opening encounters. Isolated fixtures verify the 36-tick announcement, fixed circle, live ally range and line of sight, capped barrier, preserved larger wards, caster/boss/mechanism/copy exclusions, actual kill and hard-control cancellation, cooldown, exact restored events and recorded command replay. These controlled geometry fixtures are separate from the unmodified live-encounter victory cases.

The migration and reward checks pass **160 cases** across the new release and maintained predecessors. Exact frozen pacing catalogs authenticate original bytes and state before rebinding. Tests preserve pending casts, active encounters, cached rooms, ownership, receipts, discovery, timers and RNG. Existing Fractures and Bound Memory states migrate. Published pacing rewards are not granted twice; older direct imports retain their required adjustment. Save/profile inspection stays read-only, invalid content is rejected, and explicit writes retain backups. Historical replay catalogs retain their original formations. The original ordered eight-trait Fracture pool is pinned independently of the expanded campaign trait registry.

The complete Core suite passes **805 tests**, with zero failures or skips, and all **17 server tests** pass. These totals include the focused Core checks above. The solution builds with zero warnings/errors, authored content compiles, and formatting verification passes.

## Campaign measurements

Before/after runs cover all five disciplines at seeds 42, 43 and 44: **30 full campaigns**, **206,312 public commands** and **1,096 saved replay segments**. Every run completes all fifteen main encounters and eight optional encounters with no deaths or failed world actions. Segment checkpoints restore exactly and each final save/profile loads with the expected state hash. All finish at level 10 with 5,150 XP and 475 materials before spending; story reward amounts are preserved.

The `earned-build` policy equips owned raw-stat upgrades, spends earned passive points and uses its documented starting anatomy/Manifestation setup. It does not optimize legendary powers or craft. These results validate deterministic routes across three seeds; they do not prove equal class difficulty. Changed formations, an additional monastery actor, loot draws and resulting gear choices can change combat and later command sequences. Raw measurements and the source-aware comparison are in `artifacts/opening-combat-depth/before/`, `after/` and `balance-comparison.json`.

## Native combat

The packaged opening combat diagnostic passes **47 checks** and produces **nine captures**. It earns all four Act I contexts and all three Bell Saint phases with 1,619 public commands, deliberately observing the monastery's first chant through ordinary Stop/idle input. A separately restored 36-command branch allows that real chant to resolve; both the branch and full route replay exactly. The complete route's final hash is `A69BCB54131F37128F9E45C8D527BB0593EFF133EA549563202DD8BB1F98E445`.

High and Reduced Effects/Performance captures verify the exact fixed circle and countdown, interrupt label, compact enemy barrier strips, exact focused target amount and absence of forced names on unfocused warded enemies. The damaging sonic lane remains visually distinct. The runtime log is clean. Accepted compact-barrier evidence is under `artifacts/opening-combat-depth/combat-final/`; an exact-package rerun in `combat-release/` repeats all 47 checks, nine captures and the same final/replay hash. Earlier captures remain as the record of the clutter correction.

## Native first upgrades

The exported upgrade journey passes **134 checks**, with **18 captures**, 1,593 setup commands, 45 viewport-driven combat commands and two verified replay branches. The actual Road encounter earns the boots; a ground click collects that exact item. Inspection preserves gameplay, reveals the owned item even when inventory filters previously hid it, and retains sort and saved ground-loot preferences. Cancel and Confirm each receive two mouse-button events and exactly one activation, preserving the existing departure transaction.

The route approaches Torren normally, then performs actual equip, unequip and reequip drags. Combat stays paused during each drag. An actual moving dodge creates the three Pyre patches and they expire normally. The later Bell Saint reward supplies the Heart lesson, real Resonance preview, Mara approach, implantation, removal and Manifestation checks. Save/load and both recorded branches reproduce. The saved hash is `598590EED6922DE7C53BDDA23FE143B33A17823120405CF181E8F6EFC6B51F8B`.

Gear bounds, visible Close/Boots controls and internal scrolling pass at 1280×800, 1024×720 and 780×720. Captures were inspected for the earned-item review, equipped boots, moving dodge and Heart preview, with anatomy and character layouts also captured. Runtime logs are clean. Accepted evidence is under `artifacts/opening-combat-depth/upgrades-release/`; earlier failed attempts are retained separately.

The same final package passes the Journey regression's **464 checks**, with no skipped checks, a clean runtime log, five navigation requests and two saved replay segments verified across restoration. It covers the opening rewards, travel confirmations, region choice, Bell Saint phases, transition into Act II, journal, save/load and shared environment presentation. Its existing story/travel transaction probes use explicit public confirmation signals; the upgrade journey above additionally verifies actual mouse delivery to its return dialog. Evidence is in `artifacts/opening-combat-depth/journey-release/`.

Final export: `artifacts/export/Ashenwake.zip`, SHA256 `72091d5e079b1b6944733aabc11c49587b541e076ab08b8428dc1e3e780f16be`. Final solution build and formatting logs are `build-release.log` and `format-release.log`; authored compilation is recorded in `compile-package.log`. The known pinned Godot 4.6.2 Android editor-shutdown message is accepted only by the export log checker; all native runtime logs use its strict mode.

## Review

Prism/Gemini run `1b78bb09dac44153e962e41660733e52` reviewed the staged implementation with default redaction. Its seven entries contain six positive confirmations and one formatting suggestion, with no actionable defect. The medium entry confirms correct ward exclusions and suggests reviewing hypothetical future mechanism roles; current mechanism/copy exclusions are covered by focused cases. The low Unicode entry refers to equivalent JSON representations of an unchanged apostrophe; literal Unicode formatting was restored to avoid unrelated textual churn. Other entries confirm the fixed circle, support-aware smoke policy, stable event order, isolated Fracture pool and one-time migration behavior. Raw findings and dispositions are retained under `artifacts/opening-combat-depth/`.

Native visual inspection identified overlapping per-enemy ward announcements, which were refined before acceptance. Upgrade testing exposed a real Gear-panel layout bug: wrapped inspection rows could grow the panel after its initial sizing and push Close below the screen. Coalesced deferred layout now reapplies viewport bounds after content changes; item details remain inside their scroll container.

The diagnostics also needed updates for the new optional reward prompt and existing continuation button text. An initial popup-input attempt sent coordinates to the wrong viewport. The final upgrade helper follows the existing discard-dialog input implementation: embedded dialogs receive parent-viewport coordinates, while separate windows receive mouse-entry notification. Both buttons must receive a full mouse press/release and exactly one activation. No public-signal fallback remains for these opening return confirmations.

The follow-up staged review `22b0376c25e81e9fcb77f28f0cab28ee` reports five low-priority suggestions and no high/medium findings. Localization, possible future pooling and configurable ward tuning are nonblocking authoring/optimization follow-ups. Ending the specialized Heart reminder after Act I is existing intentional behavior; ordinary anatomy inspection remains available. The private ordered Fracture array is initialized before use, never exposed or mutated, and preserves the existing seeded contract. Per-entry dispositions are in `prism-final-disposition.json`.

Layout follow-up `0ed1e7ccda295945628a0f110cd30a56` reports four entries, all resolved by inspection with no required code change. The high entry mistakes the deferred call at the end of guarded `Rebuild` for a call inside `LayoutPanel`; that recursion does not exist. An independent review checked the actual call path and pinned Godot setters: unchanged minimum sizes and geometry do not emit the signals that queue layout. The medium retraining-level entry concerns an unchanged rule that matches Core. The two low entries propose an equivalent guard and redundant property checks. Exact dispositions and pinned engine source links are in `prism-layout-disposition.json`.

Final diagnostic review `362e7f7ac9952703a69a5df7c07da0f0` covers the popup-input and Journey assertion corrections. Its medium selector suggestion overlooks the helper's existing visibility filter and intentional uniqueness assertion; taking an arbitrary first match could hide an ambiguous control. Its low loop-bound suggestion concerns accelerated deterministic setup commands, already bounded by the harness. Both are nonblocking; the actual native flow passes. Details are in `prism-diagnostics-disposition.json`.

## Limits

Scripted victories and deterministic balance measurements establish functional coverage, not human difficulty, encounter length, enjoyment or accessibility acceptance. No independent human playtest or additional OS/GPU/controller certification is claimed.
