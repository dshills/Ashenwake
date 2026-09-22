# Act I combat depth and first upgrades

The Grey March opening now places its enemies around specific jobs. At the road bend, two ghouls pressure the approach while a nearby priest can heal wounded allies. The monastery combines a Dirgebound guard, a second guard, a memory archer and a flanking ghoul. The Widow's Crypt retains its Gravewake resurrection encounter with a repositioned archer and priest. Room geometry, enemy stats, boss mechanics and encounter rewards are unchanged.

## Dirgebound

The monastery elite announces a **1.2-second chant** inside a fixed mint circle marked **ENEMY WARD**. When the countdown ends, living allies inside its three-meter radius and in line of sight gain enough barrier to reach 24. Repeated chants cannot stack that amount or reduce an already larger barrier. The caster, bosses, ritual mechanisms and temporary copies receive no ward.

Interrupt or defeat the chanting guard to cancel the ward, or draw its allies outside the circle before it resolves. The circle itself does no damage. Small cyan strips above enemy health bars show active barriers; the selected or hovered target card gives the exact barrier amount. The ordinary sonic lanes retain their damaging warning treatment. Required chant geometry and countdowns remain visible with Reduced Effects enabled.

The modifier uses the existing bounded campaign warning, recovery and elite cooldown systems. It introduces no new random draws or persistent counters. The published Fracture modifier pool remains fixed, preserving existing seeded expeditions and historical replays.

## First upgrades

The optional opening reward guidance follows actual ownership and equipment. Collect **Pyrebound Treads** after the road encounter, inspect their power, return to Torren and drag them onto the Boots slot. A moving dodge then leaves burning ground. Guidance uses the existing loot-departure review, specialist approach and equipment transactions.

After the Bell Saint, **Heart of Serath** remains an optional implant at Mara. Its explanation connects damage-over-time kills to raised spirits and identifies the burning dodge trail as one possible setup. Inspection forecasts the actual Resonance change and preserves the existing explicit implant action. Opening these screens does not apply equipment, implant a fragment or spend resources.

## Compatibility

The paired catalogs are `campaign.opening_depth.8` and `campaign-combat.opening_depth.8`. The previous `pacing.7` catalogs are frozen as embedded fixtures. Archive readers authenticate and restore the previous save before rebinding its identities. Active combat, retained rooms, inventory, rewards, progression, timers and random state remain owned by the player. New formations apply when an encounter is created; loading does not replace an already active formation. Older supported migrations retain their one-time pacing adjustment.

Historical replay catalogs retain their original encounters. The new gameplay still runs entirely in Core; the client projects warnings, barriers and optional guidance.

See [verification evidence and limits](opening_combat_depth_verification.md) for automated combat, migration, balance, native-client and Prism review results.
