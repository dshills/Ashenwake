# Shattered Spine creature animation

Act IV's combat rigs now express distinct weight and intent:

- Oath giants prepare broad, heavy strikes and settle into a weighty collapse.
- Contract keepers use ritual book and staff gestures, with a clear casting recovery.
- Bone sentinels brace behind their shields and deliver deliberate polearm attacks.
- Covenant Warden changes its stance with the actual guarded/exposed state, follows through on its attacks and collapses when defeated.

The separate monumental tablet behind the Warden's court preserves immediate Core-driven guard, oath and fault signals. Secondary shield motion and a staged seal-breaking release enrich those transitions without adding a boss phase or changing warning timing.

All animation is cosmetic. Actor roots, hit detection, combat timing, saves and replay remain authoritative in Core. Resolved attacks start at contact; new attack starts, defensive cues and terminal deaths retain their existing priority. Pause freezes motion. Reduced Effects suppresses optional motion and settles scenery victory without replaying it when effects return. Loading a defeated Warden starts with the monument settled.

Animation reuses constructed joints and render resources. Previous player, Verdant and Cinder clips remain selected by their existing exact IDs. Co-op shares base rigs and anticipation; this pass verifies event-driven clips through the solo production adapter.

See [verification](spine_animation_verification.md) for actual checks, native evidence and review dispositions.
