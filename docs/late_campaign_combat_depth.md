# Acts IV–V combat depth

The late campaign adds warding contract keepers, coordinated shadow/archer formations and readable attack sequences for Covenant Warden and Breach Heart. The paired catalogs are `campaign.late_depth.10` and `campaign-combat.late_depth.10`.

## Oath wards

A contract keeper can alternate its ordinary rooting oath mark with a **1.5-second ward channel**, followed by **three seconds of recovery**. A hollow three-meter circle shows the announced area. On completion, living original oath giants and bone sentinels still inside that fixed circle and in line of sight receive enough barrier to reach **36**. Existing barrier above the cap is never reduced, and overlapping keepers cannot stack their wards beyond it. The keeper only starts a channel when an eligible ally has less than 36 barrier.

The ward does not heal, damage the player, shield its caster or affect bosses, mechanisms, other enemy types or temporary copies. Barrier uses the existing absorption and persistence rules: it remains until damage consumes it or the encounter is replaced. It is not a timed defense percentage. Killing the keeper after a completed channel does not remove barrier already granted to another actor.

Interrupt or kill the keeper before its warning resolves, draw its allies out of the circle, or use cover to break line of sight. Hard control and death cancel the announced ward; channel recovery remains so it cannot immediately retry. Devourer consumption also removes the consumed keeper's pending ward. As with other campaign warnings, a command arriving on the resolve tick follows the existing tick order: due warnings resolve first.

## Formations

| Encounter | Formation and player decision |
| --- | --- |
| Bone Causeway | A keeper stands between a pursuing Hunter giant and a rear giant. Interrupt its ward while finding a safe gap in the numbered seismic lanes. |
| Contract Hall | Two keepers support a forward bone sentinel from different positions. Their wards share the barrier cap; choose which channel to interrupt or draw the sentinel away. |
| Repeating Rooms | Two concealed shadows pressure the approach while two memory archers cover different angles. Read the firing lanes before committing to an escape route. |
| Identity Memory | A shadow presses forward while two breach echoes and a memory archer constrain safe positions. Delayed causal warnings retain their fixed impact geometry. |

Both Hollow Night rooms have four ordinary enemies instead of three. The Shattered Spine rooms retain their existing populations. Individual enemy statistics, room collision, XP/material grants, boss damage/timing and signature reward sources are unchanged. Added ordinary enemies participate in the usual loot stream; their drops can change later equipment choices and RNG consumption. Existing saves retain already created actors and cached rooms; the new formations apply when encounters are created.

## Final boss windows

Covenant Warden announces **1/2 Oath Mark**, then **2/2 Covenant Fault**. Its guarded countdown reflects the actual defense deadline. Recovery appears only after the guard, pending action and all source warnings have cleared.

Breach Heart's initial protection points to its targetable seal channels. Breaking one channel exposes the Heart under the existing rules. Its attack labels identify the complete announced sequence:

- Phase one: **1/1 First Echo**.
- Phase two: **1/2 First Echo**, then **2/2 Returning Echo**.
- Phase three: **1/3 First Echo**, **2/3 Seal Sweep**, then **3/3 Returning Echo**.

Sequence ordinals do not change when an earlier warning resolves. A phase transition removes the old phase's warnings before a new sequence can begin. A zero-countdown warning that still awaits resolution suppresses the recovery label. Recovery and protection indicators report existing simulation state; they grant no extra damage modifier. Ordinary oath marks, shadow strikes, memory arrows and causal echoes also receive distinct names and countdowns. Coincident delayed circles stack their labels so the later impact remains legible. Required warnings remain visible with Reduced Effects enabled.

## Saves and verification

No new persisted combat timer is required: wards use existing barrier and warning state. Sequence numbers are read-only projections of the source boss, phase and attack identity. Frozen `6259723` catalogs authenticate the previous release before rebinding. Historical replays keep their original behavior mappings and rewards. See the [migration contract](late_campaign_depth_migration.md) and [verification record](late_campaign_combat_depth_verification.md).
