# Complete build loadout verification

Baseline: `ce688a2`. The change adds eight character-owned complete builds with read-only previews, atomic switching at Mara, item-use warnings, and names in existing training comparisons. Evidence is retained under `artifacts/build-loadouts/`.

## Core behavior

The focused suite passes **56 cases**: 28 new `BuildLoadoutTests` and 28 existing equipment-preset tests (`tests/build-loadouts.trx`). Tests cover combined equipment/anatomy/mutation/passive application, costs, insufficient materials, missing ownership, mastery and passive requirements, all service commands, naming/capacity, detached projections, unchanged legacy serialization, malformed and null saved choices, Manifestation thresholds, concordance discovery, campaign specialist rescue, saves and deterministic replay. Artificial mastery, missing-reference and malformed-file setups are explicit unit fixtures, separate from the client's earned route.

The complete Core suite passes **1,433 tests**, with zero failures or skips, in 12 minutes 2 seconds (`tests/core-full.trx`). All **17 server tests** pass (`tests/server.trx`). The integrated solution build has zero warnings/errors; final formatting verification passes. Core code remained unchanged during the full run; subsequent changes were client presentation and diagnostic corrections.

Applying a build uses one outer Production transaction. The progression choices, material debit and Adventure anatomy mutation either all commit or roll back, including operation sequence. Anatomy goes through the ordinary Adventure change path so concordance discoveries are retained. Saved selections do not contain health, cooldowns, RNG or resources. Enum values are appended; the optional unused snapshot field is omitted, and content identities remain unchanged.

## Client diagnostic

The headless earned-character run passes **54 checks** (`headless-3/build-loadouts-review.json`). It earns Torren's rescue and enough passive points/materials through ordinary commands, saves two builds, changes equipment/anatomy/passives, verifies exact cost and whole-build application, cancels/stales confirmations, compares named training attempts, restores the normal archive and verifies the recorded route exactly. Missing gear and being outside Greyhaven disable application. Typing menu shortcut letters does not leave the paused panel. The diagnostic uses actual viewport pointer input for panel buttons; native confirmation dialog decisions use explicit public signals because they are separate native windows.

The first headless attempt preceded the diagnostic's compiled Launch route and produced no report; it is not counted as validation. The second reached the actual screen route but exposed insufficient earned points in the diagnostic setup. The corrected route earns level three before spending points; it does not inject XP or materials.

The final headless run passes **60 checks** (`headless-4/`). It additionally clicks **Practice current build**, verifies the requested approach, follows actual mouse-navigation ticks into training and verifies the named comparison. Native attempts 1–2 exposed a diagnostic setup issue: calling Resume while the panel was holding a modal pause could not clear a preceding focus-loss pause. The diagnostic now closes its panel, resumes after focus settles, reopens the panel and clicks Practice. Production pause rules are preserved.

The final native loadout run passes **67 checks, nine viewport clicks, 1,939 gameplay/practice commands and seven captures** (`native-3/build-loadouts-review.json`), including this complete practice-navigation flow. The existing native training/equipment-preset regression also passes **171 checks, 15 captures and 11 replay verifications** (`native-training/training-review.json`) across all five disciplines and the earned Torren route. The export-specific log and strict runtime error checks pass.

These final native runs use package SHA-256 `caa64f3f594c7ceba1cb05653b149d660acf10b1f5b2e7a562850d1eadc86043`. Inspected screenshots show fitting controls at 1280 and 780 pixels, readable before/after item names and exact costs, and the named previous-build comparison with measured differences and its existing interpretation caveat. Long build details remain scrollable.

## Prism review

Prism/Gemini staged review `4a9763cbf8fe96fca56c53155bac6b01` flagged expensive whole-snapshot hashing in the UI. The panel now uses the authoritative revision and bounded saved-loadout/anatomy context; it no longer serializes the full progression snapshot to detect changes. The HUD also skips unchanged revisions, service eligibility and sessions. Configuration runs after the matching gameplay snapshot so an old preview cannot be cached under a new revision.

The other finding assumes a future asynchronous command handler. This screen belongs to the offline shipping director, whose command execution and result callback are synchronous. That contract is now explicit; a `finally` block clears the busy state if the callback fails or does not return a result. Introducing asynchronous/network semantics is outside this single-player UI; no deferred handler is connected.

Follow-up review `6bf9290007666d4da9a45c15a158e76a` reports an empty-slot concern and a display-name concern. The empty-slot claim assumes empty-string entries in saved dictionaries. Empty slots are actually absent keys: ordinary fragment removal calls `Anatomy.Remove(slot)`, saving copies the sparse dictionary, and applying replaces the whole dictionary. Fresh sparse loadouts, empty Manifestation selections and switching to a build with cleared anatomy all pass Core/client checks. Accepting empty IDs would weaken restoration validation, so that suggestion is rejected. The name concern is addressed: the panel now receives authored fragment, skill and mutation names from the already loaded combat catalog. Manifestations use the existing anatomy screen's readable-ID convention because their definition has no separate display-name field.

Final review `a88f3c1511650f8a85648ca9d0b8174f` reports two further findings, both rejected after checking the implementation. The cost claim calls a documented free fragment change inconsistent with a displayed fragment cost of zero; these agree, and the explicit zero distinguishes free anatomy changes from the paid passive refund. The name claim treats a preliminary 128-character input bound as the accepted limit but overlooks `ValidPresetName(name.Trim())`, which enforces the shared 32-character saved-name limit. The existing long-name rejection case passes. These reports do not identify an unaddressed functional defect; their raw severity labels are retained in the artifacts rather than represented as a clean zero-finding review.

## Limits

Scripted training comparisons demonstrate real damage measurements and persistence isolation, not optimal builds or independent balance acceptance. Existing discipline retraining and workshop rules remain required. Native verification applies to the tested macOS export, not all operating systems or GPUs.
