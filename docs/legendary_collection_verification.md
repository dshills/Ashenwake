# Legendary collection verification

Baseline: `c0e7581`. The journal adds presentation knowledge and navigation for the nine existing legendary items; it does not add rewards, change equipment statistics or modify gameplay archive identities. Evidence is under `artifacts/legendary-collection/`.

## Automated checks

The complete Core suite passes **1,403 tests**, with no failures or skips (`tests/core-full.trx`). All **17 server tests** pass (`tests/server.trx`). This includes 37 new collection cases covering actual ownership and extraction, retained discoveries, tracking, bounded serialization, checksum/backup protection, slot isolation, authored sources, progression gates, secret identities and unchanged gameplay/replay state.

Prism's portability correction adds two more persistence cases. The final combined collection rerun passes **39 cases** (`tests/collection-final.trx`), including 23 persistence cases covering moving a whole save folder with its sidecar and backup, rejecting copied journals under another slot name or character ID, and protecting dangling symbolic links. Together these runs cover **1,405 distinct Core cases**; the full-suite invocation precedes the portability correction.

## Desktop checks

The exported macOS game is exercised one native process at a time with fresh isolated save directories. Collection diagnostics send actual viewport pointer input to the shipping director and earn Pyrebound Treads through ordinary Core commands, then verify exact replay. The historical completed-campaign import used for endgame source navigation and the future-version sidecar used for write protection are explicitly separate fixtures.

- Initial native collection run: **73 checks, 25 pointer clicks, 207 gameplay commands and 10 captures** (`native-1/collection-review.json`). All nine previews, filters, source inspection, tracking, owned counts, story redaction, pause ownership, H transfer, save/load, separate character journals and protected future files pass.
- Headless collection run: **63 checks** (`headless-2/collection-review.json`).
- Existing native Journey regression: **464 checks and 27 captures** (`native-journey/journey-review.json`). Its strict runtime log check passes.
- Final native collection rerun: **73 checks, 25 pointer clicks, 207 commands and 10 captures** (`native-final/collection-review.json`).
- Final native character-menu regression: **146 checks and 27 captures** (`native-front-menu/front-menu-review.json`), including character creation/switching, Continue, archive preservation, guarded failures, backup recovery and Echoes return. Its intentional blocked-path fixtures produce expected warnings; the strict runtime error check passes.

The final build has zero warnings/errors, formatting verification passes, and the export-specific log check passes. The final two native runs use package SHA-256 `6dd21fae99daa4e25ba41bd8e540075b1d704e19cd378ff0399fc783c0ddde96`. The earlier Journey run precedes the bounded persistence and viewport-cache corrections.

Inspected collection captures show readable details and controls at 1280 and 780 pixels, a cosmetic equipped-item preview, and fitting tracked-source guidance in Journey and the expedition board. The owned matching Sigil and known Hunt links select existing destinations without launching them or spending resources.

## Prism review

Prism/Gemini staged review `d214502d5efd061e6a3e249df8e5c122` identified an absolute-path binding that would block collection writes after moving a save folder. The identity now binds to the stable save filename and character ID. Folder relocation preserves discoveries, tracking and recoverable backups. Renaming a slot still protects its old sidecar. The review's layout concern is addressed by caching viewport dimensions; repeated unchanged frames do not reset layout. Link checks now cover both `LinkTarget` and reparse attributes, with dangling-link regression coverage.

Other findings were evaluated against their execution paths:

- Pickup persistence is dirty-gated: ordinary loot and repeated copies do not write. There are at most nine first-discovery writes per character, plus explicit tracking/save actions. Immediate bounded persistence keeps a discovered item remembered if it is subsequently destroyed. The suggested asynchronous/debounced writer would introduce ordering and shutdown-loss concerns without evidence of a current hitch.
- Source lookups use the shipped, validated campaign and endgame definitions. Tests project every authored item against those definitions. Unsupported modified content with removed encounters is outside the shipped catalog contract; silently suppressing a broken authored route would hide a content defect.
- `IndexOf('.') + 1` is zero when the delimiter is absent and never exceeds the string length. The reported substring exception has no valid execution path for these nonnull authored IDs.
- The persistent lock filename is intentional. Deleting it after releasing a lease can unlink the inode already locked by another process, permitting a third writer to acquire a different lock file. The character catalog excludes sidecars and lock files.

Follow-up staged review `0d6f8b78909f35af357e62470998caef` reports **zero high findings**, repeats the medium synchronous-I/O suggestion and adds a low filename-renaming suggestion. Immediate dirty-gated writes remain the chosen durability tradeoff described above; ordinary loot does not repeatedly write. Stable slot filenames distinguish characters whose gameplay ID can all be `wanderer`, without migrating authoritative archives. The game keeps slot filenames stable; manually renaming a save and journal is outside the supported relocation contract and is documented. No unaddressed functional defect remains from these reviews.

## Limits

Old characters reconstruct discoveries from current inventory and learned innate powers. Items destroyed before collection memory existed cannot be inferred. Optional collection files never overwrite corrupt, incompatible or protected journals; a visible notice explains session-only changes. Verification is scripted macOS coverage, not independent player acceptance or certification of other operating systems.
