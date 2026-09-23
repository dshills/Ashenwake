# Weapon animation and locomotion

Equipped weapons now select their visible attack motion: swords sweep, daggers jab, axes carry a broad follow-through, hammers drive downward, spears thrust, staves cast, and empty hands strike. Shield abilities use an equipped shield; spell abilities retain a casting gesture with another weapon equipped. Appearance rebuilds after equipment changes select the new motion automatically.

Common Act I enemies use the same articulated approach: memory archers draw and release their actual bow, funeral guards prepare a spear thrust, casters gather energy, and ghouls rear into claw strikes. Bespoke boss and roaming champion rigs retain their existing poses. There is no player bow mesh in this pass; Widowthorn keeps its authored thorned blade and sword motion.

Attacks enter contact on the authoritative resolved event and then follow through. Windup remains driven by Core. Cosmetic recovery blends back to locomotion over 90 ms, with attacks, dodges, new tells and death taking immediate priority. Gait follows distance and speed instead of a wall-clock running cycle, with longer planted steps, corrected foot height, smoother starts/stops and turning lean. This remains a procedural rigid-joint rig, without skinned animation or terrain-aware inverse kinematics.

Weapon glints sample the animated tip, casting hand or shield, retaining six recent contact positions inside the existing 40-burst pool. Spell motes gather at the active casting anchor. Effects follow interpolated actor transforms and retire on dodge, death, removal or normal expiry. Reduced effects suppresses them; pause freezes them. Target impacts remain at the authoritative target position and time.

No animation changes actor-root position, damage timing, hit detection, movement rules, saves, replay commands or Core randomness. Co-op receives shared gait and anticipation improvements; its network adapter still does not dispatch the solo event-driven attack/effect clips.

See [verification](weapon_animation_verification.md) for checks and native captures.
