# Equipment wardrobe

Shoulders, gloves, belts, leg armor and boots now appear on solo characters alongside the existing weapons, off hands, helmets and chest armor. Dragging any of these items off its slot removes its own attachments; equipping it restores them in the world and character preview. Character creation, saved-character portraits and anatomy previews use the same appearance projection.

| Equipment | Visible details |
| --- | --- |
| Wakeguard Mantle | Layered funeral cloth, embroidered ends, an engraved clasp and a protective crest |
| Gravesoil Grips | Soil-dark palms, wrapped cuffs and ivory knuckle guards |
| Last-Rite Girdle | A funerary seal, prayer strips and a sealed reliquary pouch |
| Mourner's Greaves | Overlapping thigh and knee plates, chevron engraving and mourning ribbons |
| Cindertrail Boots | Reinforced toes, crossed laces and copper-ember seams |

The five disciplines retain their own wardrobe colors and materials: Vanguard metal, Veilwalker muted violet, Arcanist blue and brass, Gravecaller bone and ash cloth, and Warden bark and green cloth. The mantle uses bone or leaflike crests for Gravecaller and Warden. Inventory, crafting and drag icons share the wardrobe palette and reproduce the equipment's identifying motifs. Rarity borders remain independent of these material colors; empty slots remain muted.

Chest items no longer own shoulder pads or forearm armor. Robes have a split front that exposes the independent knee and boot modules. Empty slots leave underlying clothing, bare hands and simple soft foot coverings. Rings and amulets retain their icons and stat effects without body attachments. Cooperative characters retain their existing fixed equipment presentation.

The new modules attach to the existing arm and leg joints, so walking, attacking, dodging and death use the same cosmetic rig. They create no collision, navigation, lights or particle emitters. The appearance identity includes all nine visible slots, using definition, rarity and evolution metadata. Item ownership, combat rules, content catalogs, serialized saves and replay schemas are unchanged. Existing characters gain the wardrobe when opened.

Independent slot geometry uses the existing bounded cache of 160 equipment/manifestation templates, with the existing 96 base-template and 256 material limits. No full-loadout combinations are cached. The build checks geometry on all five disciplines; this is not a target-hardware performance certification.

## Reproduce

Build the client and compile production content with the pinned tools, then run the existing appearance diagnostic in a fresh directory:

```bash
source tools/env.sh
armor_output=$(mktemp -d "$AW_ROOT/artifacts/armor-visuals/rendered.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 9000 \
  --log-file "$armor_output/smoke.log" -- \
  --appearance-smoke --capture-appearance --output="$armor_output"
python3 tools/check-godot-log.py "$armor_output/smoke.log"
```

The diagnostic checks independent removal on all five disciplines, paired attachments, mesh sharing, limb motion, finite geometry, rarity support, robe openings and cache limits. Native drag/drop exercises the five added slots through authoritative equipment transactions, preserving ownership, save/reload and replay. Captures include complete outfits, the five new armor slots alone, removed armor, walking and rear poses and the matching icons, as well as the existing inventory layouts and gameplay scenes. Use `--headless` without `--capture-appearance` for assertions only.

## Verification

- Solution build: zero warnings and errors; formatting and whitespace checks pass.
- Rendered appearance diagnostic: **547 checks**, **36 native drag gestures**, **27 captures**. Final galleries and inventory layouts at 1280 and 780 pixels were visually inspected. Source evidence: `artifacts/armor-visuals/verified-rendered.8Tub3p`.
- Refreshed macOS package: Appearance **520**, CombatFeedback **168**, Anatomy **75**, FrontMenu **119** checks pass. Export and runtime logs pass the Godot log checker. Package evidence: `artifacts/armor-visuals/verified-package.K0Rqv5`.
- Existing save/replay, maintained campaign fixture, world/preview agreement and ownership checks pass through the appearance diagnostic. No Core, content, save schema or fixture changes.
- Prism/Gemini reviewed the implementation twice. The robe inspection prompted explicit front/back normal separation and a lighting-normal check. Remaining suggestions were checked against the existing shared mesh cache, model replacement lifecycle and identifier validation; no actionable findings remain. Reports and dispositions are in `artifacts/armor-visuals/`.
