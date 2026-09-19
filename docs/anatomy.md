# Divine Anatomy

Open **Journey map & anatomy [J] → Anatomy** to inspect your implants. The six body slots—Mind, Eyes, Heart, Spine, Arms and Legs—are clickable and keyboard focusable. Filled mint markers show installed fragments; a gold border marks the selected slot. The shipping catalog supplies eight fragments across all six slots. Compatible cards distinguish installed, owned and undiscovered choices.

The right-hand cards show the installed fragment, its effect, Resonance, combination tags and ownership. Choose an owned fragment or **Preview empty … slot** to inspect a replacement or removal. **Body map** returns to the diagram; **Character** shows the rotating model with the proposed implants and active Manifestations. Drag the model or use Left, Front and Right to rotate it.

Inspection is available outside Greyhaven. The Anatomy screen pauses the solo world and blocks gameplay input while it is open. Close it with **J**, **Escape** or **Close**; opening another major character/expedition panel also closes the inspection. Save/load remain available. Closing or loading discards the draft. Closing Anatomy releases only its own pause, so an existing manual or focus-loss pause still requires Resume.

## Preview and apply

The forecast compares installed and proposed Resonance, identifies newly qualifying combinations, and shows each Manifestation threshold. Historical combination discoveries remain recorded even when their tags no longer align. A remembered Manifestation is suppressed below its threshold; regaining enough Resonance restores its active form. The cards show both the benefit and complication of each available form.

Selecting a fragment or form changes only the preview. **Keep current anatomy** restores the installed appearance. To apply a choice, approach Mara in Greyhaven and use the explicit **Implant**, **Remove** or **Apply** action. **Walk to Mara** closes the screen and uses the normal mouse approach path. Being in Greyhaven alone is insufficient: the existing service range, ownership, compatibility and threshold checks still apply. A candidate that would unlock a new Manifestation must be implanted before that form can be selected.

## The first boss implant

1. Defeat the Bell Saint. The Heart of Serath enters your fragment collection automatically; dropped equipment stays on the ground.
2. Use **Inspect the Heart of Serath** from the objective or reward review. You can preview its chest crest and effects before leaving the sanctuary.
3. Return to Greyhaven. If ground drops remain, the Anatomy return action opens the map's reward review first. Collect the equipment or explicitly choose a travel action that leaves it behind.
4. Walk to Mara, inspect the heart and press **Implant Heart of Serath**. With only the starting Eye of Vael installed, Resonance rises from 12 to 30. The chest crest appears immediately, before the first Manifestation threshold.

The opening-act reminder ends once the heart is installed or you advance beyond Act I. The implant stays optional; it adds no new travel requirement.

## Cosmetic marks

| Installed fragment | Visible mark |
|---|---|
| Eye of Vael | A small illuminated ocular lens and facial seams |
| Heart of Serath | An ember crest set over the chest |
| Nerve of Ilyra | A pale vertebral chain and collar lights |
| Orrun's Bone | Ivory forearm plates and illuminated seams |

All five disciplines display these four marks over their equipped clothing and armor. The four other fragments retain their gameplay effects and body-map markers; they do not yet have unique character geometry. Removing one of the marked fragments removes its cosmetic mark. Anatomy inspection preserves the equipped weapon, including Ashcleaver's awakened or evolved form. Existing Manifestation silhouettes remain separate from these small implant marks. Hidden previews stop rendering and processing; reduced effects retain static implants.

## Implementation and reproduction

`AnatomyDiagram` reuses six buttons and draws its silhouette without raster assets. `AnatomyWorkbench` owns temporary selections, cards and the character preview. `CampaignAnatomy` connects the screen to Mara, safe return actions and its independent modal pause owner.

Core's `AdventureSession.PreviewFragment` and `PreviewManifestation` run the existing mutation and validation transaction against an isolated candidate. They return independent before/after anatomy snapshots without changing the live character, reward history, random state or replay. Applying still uses the normal production service transaction. No preview selection or body-map geometry enters a save or network protocol.

`CharacterAppearance.AnatomyMask` derives a cosmetic identity from the four installed fragment IDs. `CharacterVisual.Anatomy` uses independent fragment modules in the existing bounded mesh/material caches; it adds no physics, movement or random behavior. The cooperative prototype retains its existing presentation and does not gain anatomy editing through this screen.

After building the client, run the shipped campaign diagnostic from Bash:

```bash
source tools/env.sh
anatomy_output=$(mktemp -d "$AW_ROOT/artifacts/anatomy-playthrough.XXXXXX")
"$GODOT" --path game/Ashenwake.Client -- --anatomy-smoke --discipline=Vanguard --capture-anatomy --output="$anatomy_output"
```

Add `--headless` before `--` and omit `--capture-anatomy` for a headless run. The same arguments work with the exported application. The diagnostic earns the actual Bell reward through campaign commands, exercises viewport input for inspection and surgery, checks removal, Manifestation suppression, save/load and layout, and verifies its recorded replays. `tools/export.sh` includes the headless Anatomy diagnostic. The separate `--appearance-smoke` route checks cosmetic module identity, resource reuse and preservation of equipped weapon evolution during inspection. See the [verification record](anatomy_verification.md) for results and review dispositions.
