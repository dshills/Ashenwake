# Combat animation and feedback

Solo play now uses distinct attack poses for all five disciplines, monster swings/casts/lunges, stronger enemy anticipation and recovery, dodge leans, recoil, and collapsed death poses. Visible enemies retained in Core remain on screen for their finite fall (.86 seconds, or 1.12 for bell/beast models), then hide. They stop being targets immediately when Core reports zero health. Revived actors receive a fresh rig. Loading existing corpses does not replay old deaths. Authoritatively hidden actors stay hidden; removed/expired summons and actors from an abandoned arena are removed immediately.

`Sandbox.PresentCombatEvents` synchronizes the authoritative view and dispatches cosmetic reactions once per batch. Resolved abilities and campaign hazards produce the attack stroke; damage, dodge, barrier, death and phase events produce their corresponding feedback. Animations never move the actor root, delay damage, consume Core randomness, or change commands, collision, rewards or saves. The introductory journey forwards real Core events through this same adapter.

Weapon strokes, short spell motes, impact sparks and dodge dust use at most 40 pooled bursts with six shared-mesh pieces per burst. Each burst owns one mutable material. Oldest bursts can be replaced under load; no combat outcome depends on this cosmetic budget. Geometry is allocated on pool growth only. Floating combat labels are capped at 32 and pause alongside poses, transient effects, camera shake, and audio. Reduced visual effects suppresses optional bursts and bell debris while preserving authoritative floor warnings, character poses and combat labels. Reduced camera shake remains independent.

Sixteen procedural PCM cues use shaped noise, pitch sweeps, layered metal harmonics and short tonal sequences. There are separate blade, bow, spear, magic, bone, enemy, armor, hit, dodge, death, bell, chain, victory, healing, loot and incoming-attack sounds. Eight audio voices are bounded; two are reserved for warning/boss cues. Repeated identical cues within 70 ms are coalesced. These are synthesized development sounds, with no downloaded samples.

The sanctuary's architecture persists through phase and interaction changes. Its separate bell rig swings gently in phase I, sways more strongly with ritual lights in phase II, and drops 18 preallocated chain links as it tilts in phase III. Victory adds a finite 3.2-second toll/ember sequence. A save loaded in phase III or after victory starts in the final pose. The bell, links, lights and embers stay beyond the northern combat boundary; none adds physics or navigation. Reduced effects settles transitions immediately and reduces ambient swing.

This milestone targets solo combat and the opening route through Bell Saint. Co-op shares the improved windup/recovery rigs; its network presentation does not yet dispatch the new event-driven clips, effects or audio. These procedural clips are not production skinning, equipment-specific animation sets, recorded Foley, or measured performance certification.

## Reproduce

Build the solution, then use fresh output folders. The first diagnostic checks animation priority, pause, terminal deaths, root motion, material isolation, effect budgets, PCM bounds and bell transitions. The second navigates real opening encounters and captures actual event-driven feedback.

```bash
source tools/env.sh
feedback_output=$(mktemp -d "$AW_ROOT/artifacts/combat-feedback-smoke.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 1200 \
  --log-file "$feedback_output/smoke.log" -- \
  --combat-feedback-smoke --capture-combat-feedback --output="$feedback_output"
python3 tools/check-godot-log.py "$feedback_output/smoke.log"

journey_output=$(mktemp -d "$AW_ROOT/artifacts/combat-journey.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 2400 \
  --log-file "$journey_output/smoke.log" -- \
  --journey-smoke --capture-journey --output="$journey_output"
python3 tools/check-godot-log.py "$journey_output/smoke.log"
```

`tools/export.sh` runs both diagnostics from the exported application alongside the existing campaign replay and UI checks. Rendered captures complement structural assertions; external playtest and hardware acceptance remain separate work.
