# Appearance wardrobe verification

Evidence is retained under `artifacts/appearance-wardrobe/`, with the initial Core regression log at `artifacts/wardrobe/core-regressions.txt`.

## Automated and native checks

The initial targeted selection passed 100 cases covering wardrobe operations and storage, collection memory, and equipment set content/combat. Coverage includes actual ownership and stash observation, retention after disposal, highest observed rarity, all seven armor slots, locked/incompatible selection rejection, eight named looks, pure operations, optimistic concurrency, backup recovery, file bounds, malformed/duplicate JSON, foreign/newer files, symlink aliases, and filename/character isolation. The directory-move test moves the actual sidecar directory and reloads it successfully.

The native diagnostic uses actual viewport input for wardrobe and character-menu controls. It earns the Last Vigil pair through the ordinary public campaign route, changes real equipment, salvages the earned helmet, and checks appearance retention. Cosmetic operations must leave the authoritative state hash and replay history unchanged. Five discipline previews are explicitly detached presentation projections; they are not five separately played campaigns.

The final native run (`native13`) passed **181 checks**, including **64 actual UI clicks**, keyboard popup selection, physical-key text entry, eight rendered captures, and **8,963 public campaign/equipment commands**. Applied looks, cancelled drafts, staged save/delete, reset, actual salvage retention, save/load, character switching, and final replay all pass. Wide (1280 × 800) and compact (780 × 720) screenshots were inspected. The engine log is clean. The final targeted Core regression selection passed **100/100** again after the storage parsing change; these overlap the initial run and are not 200 distinct tests. Full solution build, formatting verification, export-script syntax and staged whitespace checks pass.

The exact refreshed macOS ZIP passed **173 packaged wardrobe checks** (the eight screenshot checks are omitted headlessly) and **33 packaged release/pause/recovery checks**, with clean accepted engine logs. `tools/export.sh` includes the wardrobe diagnostic in future package verification. The playable export is `artifacts/export/Ashenwake.zip`, SHA-256 `61f43ded2b7fce01178de57c0977eac1188820f0fb8234102a6b06b9d2f56ab9`.

Initial native runs exposed two diagnostic assumptions: Vanguard's starting chest is `item.march_plate`, and its resolved item definition must come from the combined production catalog. The diagnostic now derives initial appearances from the actual equipped inventory. Screenshots also exposed preview minimum-size propagation and a collapsed Save button; the wardrobe now bounds its preview and gives the button an explicit minimum width.

Physical-key name entry caught the global map shortcut consuming `M` before the GUI text field. Global pre-GUI shortcuts now yield to focused text fields. The saved-look diagnostic uses root-viewport input forwarding and navigates to the actual focused popup row before accepting it; direct popup-viewport events skip Godot's window handler, and a mouse-opened popup initially has no focused row. The pinned engine [viewport input implementation](https://github.com/godotengine/godot/blob/4.6-stable/scene/main/viewport.cpp) was inspected to resolve the diagnostic routing. Intermediate failed runs remain available rather than being represented as passing evidence.

## Prism review

Prism/Gemini reviewed the staged implementation with default secret redaction and `tools/prism-implementation.json`. Initial review `c66180b5d4d055fabc78de2bbc0e8f6e` returned two findings:

- The reported dependency on the absolute directory is incorrect. `Identity` hashes `Path.GetFileName(Path.GetFullPath(savePath))`, so only the stable save filename enters the hash. Moving the entire directory works and is tested; renaming individual save files is deliberately not supported. Filename binding is necessary because the existing character archives reuse the logical `wanderer` identity across distinct slots. Removing this check would weaken isolation.
- The store now deserializes its envelope from the already parsed JSON element using the canonical serializer options, avoiding another UTF-16 input conversion. The bounded metadata and duplicate-member checks remain necessary before accepting the document.

Follow-up review `375305cbe699b7cc649b64a7a3fdba9a` reported no high-severity findings. Its remaining suggestions were assessed against the implementation:

- Keep atomic backup replacement instead of the proposed direct overwriting `File.Copy`, which could damage the backup if interrupted. Sidecars are bounded to 256 KiB and writes occur only for discoveries or explicit Apply.
- Save filenames are stable, generated slot names or names retained verbatim by the character browser. Case-only filename renaming is outside the documented directory-move contract; normalizing would collapse distinct files on case-sensitive volumes.
- Reasserting panel bounds while open fixes the observed Godot container minimum-size expansion after rebuilding wrapped controls. Expensive preview layout changes still occur only when viewport dimensions change.
- The palette and bounded identity length follow existing local UI/storage conventions. Rarity comparisons use the game's existing ordered `ItemRarity` contract; tests verify highest-rarity retention. These are not current correctness defects.

The final input/integration review `066a6568f19b2ddbe756d8538a261c70` raised two performance suggestions, both addressed: layout still detects container expansion while open but writes position/size only when values differ, and unlock change detection now compares the immutable unlock records directly instead of serializing and hashing both arrays. The native diagnostic also retries a freshly resolved button when the asynchronous character catalog replaces it during layout, without bypassing the actual click.

## Scope

Cosmetics change only the client appearance projection. Equipped items, set counts, stats, resources, progression archives, content hashes and replay commands retain their existing authority. Appearance memory lives in optional character-specific sidecars. The networked co-op prototype retains its existing equipment presentation. Native scripted verification is not a substitute for human usability testing or hardware certification.
