# RC1 validation record

This record covers preparation of Ashenwake **1.0.0-rc.1** from the prior pet milestone `04d784f`. It is not public-release acceptance. See [scope and independent playtest criteria](release_candidate_1.md).

## Changes under validation

- Correct the roaming-champion historical catalog regression by comparing the frozen generations involved, while separately retaining exact current champion definitions and reward mappings. The later equipment-set additions remain covered.
- Initialize permanent pets before new-character and imported-character first durable saves. The native front-menu diagnostic caught a live/save hash mismatch caused by enabling pets only during the subsequent refresh.
- Label the application and assemblies `1.0.0-rc.1`; use numeric `1.0.0` in macOS bundle metadata and show the prerelease label on the main menu.
- Extend the immutable release fixture inventory with actual pre-pet and pet-owning client archives. Preserve original bytes and all logical progression; distinguish frozen-document integrity checks from executable restore claims.
- Add pet, secret/champion and equipment-set visual sources to the asset inventory without inventing distribution approval.
- Rebuild release helpers from the reviewed source before generating content or attestations, and test a fresh extraction of the exported Mac ZIP using its exact executable.

## Evidence

Raw evidence is retained in `artifacts/rc1/`. The opening audit passed its maintained fixtures, ten-session/9,000-tick checkpoint-and-replay soak, and projectile-ceiling check. These are bounded simulation checks, not rendered performance or a long hardware playtest.

The corrected catalog plus related equipment-set selection passed 27 tests (`artifacts/rc-champion-catalog-tests.log`). Expanded release diagnostics passed eight tests. Full regression, reviewed-source package and final audit results will be recorded after their runs complete.

## Remaining acceptance

Independent playtests and pacing assessment, additional OS/GPU/physical-input coverage, longer rendered sessions, owner-approved distribution/credits and final signing/install/update acceptance remain open. No public release or tag is authorized by a passing automated report alone.

## Prism review

Prism/Gemini reviewed the complete staged RC changes with default redaction (`51d7df4c383be74461246229b6c4a984`, `artifacts/rc1/prism.json`). It returned zero high findings and two medium suggestions, both checked against the execution path:

- The claimed server reconciliation race does not apply to this offline solo director. The client issues a synchronous authoritative Core `EnablePets` command before writing and validating the new/imported save. Core owns its rules and ledger. There is no network transition or the suggested `EndgameService`/`GameStateMachine` in this path. The native front-menu test now verifies durable/live equality and original/Echoes separation.
- The new generic endgame fixture check compares the entire loaded authoritative state with the original state after the explicitly supported catalog rebind, then compares the round-trip hash. Campaign progression, equipment, resources and pet state remain part of that comparison and are validated by the loader. The hub/tier-10/five-hunts assertions remain specific to the completed PhaseFive fixture; imposing them on a genuine early-game save would be incorrect.

No actionable review finding remains.

The accepted native menu run (`front-menu3`) passed **146 checks** and produced **27 captures**, covering new/imported characters, original/Echoes switching, archive preservation, failures/recovery and compact menus. The log passes the engine-error checker; deliberate I/O-failure warnings belong to the diagnostic. The RC version and compact main menu were visually inspected.
