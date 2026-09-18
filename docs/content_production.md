# Content authoring and production inventory

Run `aw authoring validate` after changing combat, adventure, progression, or text content. It invokes the same immutable parsers as the game and checks discipline-to-skill references and required UI keys. `aw authoring templates [empty-directory]` writes validated examples for skills, mutations, fragments, ordinary enemies, the Bell Saint, items, loadouts, rooms, affixes, objectives, messages, and crafting costs. Generated examples preserve real references. Give new entries distinct IDs, put them into their owning source document, and validate the complete source. The generator refuses to overwrite existing files.

The validators reject unsupported effect verbs rather than pretending arbitrary strings implement new mechanics. Routine parameter/shape/status/trigger changes use this vocabulary; genuinely new mechanics require an explicit Core handler, limits, attribution, save state, and replay coverage. AI-authored content uses exactly the same path. `aw content refs <id>` reports source references. The client consumes a copied, immutable bundle; a source edit requires a new session after compilation so a running save cannot silently change rules.

## Localization

`content/text.en.json` owns production-workshop text. `TextCatalog` supports strict named substitutions and explicit English singular/other forms. Placeholder sets must agree across forms; replacement is one pass, so a user-supplied value is never interpreted as another token. `aw authoring pseudo [new-file]` generates the development `qps-ploc` catalog with accented vowels, markers, and roughly 33% expansion while preserving tokens. The client accepts `--pseudo-locale` for layout inspection. English is the only authored language. Additional plural rules, fonts, and translated content need a reviewed language-specific implementation; pseudo text is not a translation or proof of font coverage.

## Art and preview conventions

Source assets belong in `assets/source/<region>/<kind>/<id>/`; export GLB files to `assets/exported/` and import runtime copies into the client. Use lowercase stable IDs, meters, Y-up, -Z facing, applied transforms, foot origins for actors, and pivot centers for doors/traps. Separate visible mesh, simplified collider, navigation, and telegraph anchors. Authoritative collision stays in Core integer millimeters; cosmetic meshes never decide hits. Keep impossible geometry outside the walkable collision silhouette.

Target a shared humanoid skeleton with stable bone names and separate manifestation attachment points at eyes, heart/chest, spine, arms, legs, and head. Retargeting must preserve foot placement, weapon grip, root scale, and motion-free attack timing; Core windup/recovery drives gameplay. Test idle, movement, strike, hit, dodge, and death in a neutral-lit preview before regional lighting. Prototype primitives do not prove that a production skeleton or retargeting pipeline works.

Initial art budgets for review are two materials per ordinary actor, four per boss, three mesh LODs for large reusable assets, readable silhouette at the game camera, and no collision on decorative attachments. These are provisional authoring targets, not measured shipping limits. Use named telegraph/projectile/impact/status/loot/manifestation VFX, a reduced-flash variant, and distinct shape cues alongside color. Audio routes to Master/Music/Effects/UI; critical tells take priority over low-value hit repetition. Production audio ducking, localization VO, external assets, and rights declarations remain tracked release work.

## Counted scope and acceptance

The authoring validation report counts actual definitions. Campaign commitments are five procedural regions, fifteen encounter contracts, eight elite modifiers, six factions, and four named God Hunts plus an optional Nhal hunt. These contracts are a production inventory, not evidence of final art, independent fun testing, or a sustainable human production rate. Record source authoring, integration, revision, and QA time separately for each new skill, enemy, room, boss, and quest before estimating a staffed production schedule. There is no measured art-team throughput or approved production budget in this repository.

Keep each act's status as playable prototype, content complete, or polished. A phase commit records implemented software and reproducible checks; unperformed qualitative and platform acceptance remains open in the implementation plan.
