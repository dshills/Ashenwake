# Hunter’s Bestiary

Open **J → Hunter’s Bestiary** during the campaign, or **B → Bestiary** from the expedition board. Browsing pauses the solo game.

An undiscovered entry hides its name, creature model, story, habitat, combat notes and rewards. Encounter a visible living creature to reveal its name, lore, regions and rotatable model. Defeat it to unlock attack tells, counterplay, base defenses and known equipment sources. The preview uses the creature’s actual game model; drag it or use the Left, Front and Right controls.

Use **Seen**, **Defeated**, **Families**, **Bosses** or **Champions** to narrow the journal. Search matches discovered names, regions and categories. Bosses and the three roaming champions have separate entries; champion victories do not inflate the record for their ordinary creature family. Personal records show total defeats and the elite subset.

**View source** opens the existing encounter or reward inspection panel. It does not start travel, spend resources or claim treasure. Rewards describe their encounter or source, not a guaranteed drop from every individual creature. Future and undiscovered source details remain hidden. Fracture treasure depends on the contract’s region, independently of its boss family.

## Records and saves

Discoveries and defeat records are remembered immediately in an optional `<character-save>.bestiary.json` sidecar, bound to the character and save filename. Reloading the same defeat does not count it again. Each character has separate records; the sidecar can move with its save directory. Preserve the sidecar and backup when copying a character’s saves.

Existing characters begin recording from the encounters played after this feature is installed. Old quest completions do not manufacture historical defeat totals. Training and retained legacy/co-op modes do not award bestiary knowledge. The solo campaign, optional encounters, Fractures, God Hunts and local Echoes flow record normal encounters.

The journal never changes equipment, damage, resources, progression, save schemas, content hashes or replay commands. Invalid, foreign or newer sidecars are preserved; the panel explains when discoveries can only be remembered for the current session. A valid backup can recover a corrupt primary. Records retain up to 16,384 unique defeats without evicting earlier deduplication evidence; at capacity, a notice appears and new creature discoveries continue.

See [verification and review](hunters_bestiary_verification.md) for the tested scope.
