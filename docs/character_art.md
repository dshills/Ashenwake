# Character art pass

The playable client uses articulated low-poly characters built in `CharacterVisual.cs`, `CharacterVisual.Humanoids.cs`, and `CharacterVisual.Monsters.cs`. These are repository-authored meshes and materials, with no downloaded art or texture dependencies. Solo and cooperative presentation share the same models. Greyhaven specialists use the same humanoid proportions with individual clothing and props.

Vanguard has plate armor, a crested helmet, sword and shield. Veilwalker wears a hood and carries two blades. Arcanist has a jeweled staff and robes. Gravecaller wears a bone mask and rib ornaments. Warden has antlers and a spear. These silhouettes represent disciplines; they do not yet change with every equipped item.

Enemy models use stable definition IDs to distinguish skeletal archers, hunched ghouls, armored guards, hooded casters, crawling creatures, roots, ritual objects and bosses. The Bell Saint has a bell helm, captive face and chains; its third campaign phase uses the unbound beast model. Regional and endgame bosses share body families with different heads, attachments and palettes.

Models use meters, Y-up, -Z facing and a foot origin. Walking, idle movement, facing, windup and recovery are cosmetic joint transforms. They neither move the actor root nor introduce physics or navigation nodes. Core positions, hit detection, attack timing, telegraphs and progression are unchanged. Solo pause freezes the character pose. Manifestations and marked echoes alter an actor's accent material without recoloring other actors.

The default gameplay camera is closer and follows the local player with a short smoothing delay. Mouse-wheel zoom still works. Label heights include tall weapons and attachments, so names clear the new silhouettes.

Static mesh pieces are combined by joint and material once per visual identity, then matching actors reuse that immutable geometry. Base materials are shared by palette color and shading properties; each actor owns its mutable accent. Caches retain at most 96 mesh templates and 256 base materials. Each actor uses at most seven materials; diagnostics cap visible mesh nodes at 100 and verify resource sharing and accent isolation. This first pass exceeds the earlier provisional two/four-material production targets. It does not establish a production skinning, LOD or equipment-retargeting pipeline. Dedicated hit, dodge and death clips, final texture work and hardware performance acceptance remain future art work.

## Reproduce the visual check

After compiling content and building the client:

```bash
source tools/env.sh
visual_output=$(mktemp -d "$AW_ROOT/artifacts/character-visual.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 1200 \
  --log-file "$visual_output/smoke.log" -- \
  --visual-smoke --capture-visuals --output="$visual_output"
```

The isolated diagnostic creates hero, monster and NPC lineups using the same models as gameplay. It checks every enemy catalog, finite geometry, mesh/material bounds, cosmetic-only node types, pose changes, pause stability and material isolation. Omit `--capture-visuals` and add `--headless` for automated geometry checks. `tools/export.sh` runs that check through the exported application's normal launcher. `--journey-smoke --capture-journey` captures the actual opening encounter and all three Bell Saint phases while exercising the existing campaign route.

Visual inspection is still required: the diagnostic's structural checks cannot establish that a silhouette is readable or an animation looks good.
