# Cinder Reach creature animation

Act III's four featured combat rigs now carry different kinds of motion:

- Emberlings move with quick, twitchy gestures, brace for their detonation and crumple when defeated. Their actual detonation kills them in Core, so terminal collapse takes priority over an attack follow-through.
- Furnace brutes use weighty steps and a broad crushing strike, with a heavy recovery.
- Forge sentinels keep a planted defensive stance and use deliberate weapon preparation and follow-through.
- Furnace Spindle reacts to its actual guarded/exposed state, braces for attacks and settles into a mechanical collapse.

The large extraction engine behind the boss arena retains its Core-driven armor and vent directions. Secondary machinery reacts to exposure, and victory sequences a finite mechanical shutdown and core cooling. No new boss phases, targets or rewards are introduced.

Resolved attacks start at contact. The remaining motion is cosmetic follow-through; new start events, pause, death and warning priority retain their existing contracts. Animations never move actor roots or change hit detection, combat timing, commands, saves or replay state. All joints and render resources are built once, and animation changes existing transforms or room-owned materials.

Reduced Effects suppresses optional motion and settles the scenery's shutdown even while paused. Loading a defeated furnace starts in its settled state, and restoring effects does not replay victory. Creature selection uses exact authored IDs, so the previous Verdant and player weapon motions remain intact. Co-op shares the base rigs and anticipation; the solo event adapter remains responsible for the event-driven clips.

See [verification](cinder_animation_verification.md) for reports and native captures.
