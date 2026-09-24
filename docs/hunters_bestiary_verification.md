# Hunter’s Bestiary verification

Evidence is retained under `artifacts/bestiary/`.

## Automated coverage

The targeted selection passes **86 cases**, including 23 bestiary cases plus related appearance wardrobe and legendary collection regressions. Bestiary coverage includes hidden projections, seen/defeated gating, champion identity, reachable roster, all 25 Fracture region/boss-family combinations, immutable observations, token normalization and deduplication, stale-writer union, the full 16,384-record limit, character/save-filename isolation, backup recovery, malformed/oversized/duplicate JSON, unknown catalog and newer schemas, and symbolic-link protection. The final parser run is `core-tests3.log`.

The native diagnostic starts a fresh Vanguard, plays the public Act I route, earns the Bell-Torn Pilgrim and Bell Saint records, and inspects the journal with actual viewport input. It tests a pre-defeat save rollback and repetition of the same real killing-command sequence, search including the map shortcut letter, filters, model rotation, known reward-source navigation, save/load, character switching and replay. It does not inject discovery or reward fixtures. Wide and compact screenshots are captured for visual inspection.

Initial runs caught modal expansion after deferred layout and diagnostic assumptions about Godot’s sanitized node names. The journal now keeps its scrolling modal bounded and supplies explicit stable node names. The replay diagnostic now follows the existing save contract: restoring starts a new replay checkpoint, so it checks replay validity and exact authoritative state instead of requiring the old replay-frame count. The boss route waits for the complete encounter, including remaining enemies, rather than stopping at the boss’s individual death event. Intermediate failed reports remain retained.

The final native run (`native5`) passed **112 checks**, including **33 actual UI clicks**, physical-key search input, preview button/drag rotation, six rendered captures and **2,041 public campaign/combat commands**. Genuine first sightings, first defeat, the exact same death replayed from a pre-defeat save, champion and boss records, earned reward inspection, save/load, character isolation and final replay all pass. Wide (1280 × 800) and compact (780 × 720) captures were inspected; controls and previews remain within the viewport and longer details scroll. The accepted engine log is clean. Full solution build, formatting verification and export-script syntax checks pass.

The exact exported macOS app passed **106 packaged bestiary checks** (six screenshot assertions are omitted headlessly) and **33 release/pause/settings/recovery checks**, with clean accepted engine logs. `tools/export.sh` now includes the bestiary diagnostic in package verification. The refreshed playable archive is `artifacts/export/Ashenwake.zip`, SHA-256 `49ce18a8e06df7700ca28d6c6f509eab489244c68184e1a040a3340cc1b0173a`.

## Prism review

Prism/Gemini reviewed the complete staged implementation with default secret redaction and `tools/prism-implementation.json`. Report `58a918450b37d9aed474cccd4a8a9888` contains zero high-severity and two medium-severity suggestions. Both were checked against the execution path:

- The proposed missing hunt/act crash is excluded by content validation before `InitializeBestiary`. `CampaignContent` requires acts 1–5 in order. `EndgameCombatContent.Validate` requires exactly 15 phases, three indexed phases for each of the five policy hunts, with existing boss references. The three champion definitions reference acts 1–3. Catalog tests construct the actual composed content and cover every current entry. Fabricated mismatched definition objects are not accepted game content; silently substituting metadata would obscure that error.
- The reported catalog rebuild is bounded to **36 entries**, runs only when opening, selecting or changing a filter/search, and does not run per frame or during combat. Native typing, filtering, rotation and compact-layout checks pass. Prism’s location names a nonexistent `Ashenwake.UI/Panels` path; the actual implementation is `Ashenwake.Client/BestiaryPanel.cs`. Pooling is a possible future scaling optimization, not a demonstrated correctness or performance failure in this roster. Model geometry is already cached when the selection is unchanged, and hidden previews stop rendering.

No actionable defect remained from this review. The report is retained as `prism.json`.

## Scope

This is optional solo presentation memory. No authoritative save/content schema or combat rules changed. Existing characters do not receive invented historical kills; they discover current living visible enemies and record future defeats. Training and legacy/co-op modes do not add knowledge. At 16,384 unique defeats, further defeat counts stop with a notice while discoveries continue. Native automation does not replace human usability testing or hardware certification.
