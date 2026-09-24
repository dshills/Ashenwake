# Verdant creature animation

Act II creatures now have movement and combat poses fitted to their existing silhouettes:

- Needle swarms crawl with eighteen articulated legs in alternating groups, gather before striking, then scatter into a low collapse.
- Carnivorous vines and feeding roots keep their bases grounded. Tendrils move with travel; mouth petals spread during anticipation and close into the resolved strike. Death folds the stem and lets the tendrils droop.
- Bloom carriers move with an uneven shoulder sway, brace before lunging, recoil visibly from unprotected hits and buckle when killed.
- The Antler carries its weight through the shoulders, lowers its head before striking and settles into a heavy crouched collapse.
- Rootheart's combat rig spreads its petals and draws back its tendrils before striking. Its restrained recoil and slower collapse distinguish it from the smaller plants.

Behind the Rootheart arena, the existing corpse-flower also reacts more expressively. Destroyed feeding roots wilt over a short transition and cause a brief seed recoil. The exposed seed breathes more strongly. Victory petals open in sequence before quiet new growth emerges. These responses use only Core's existing living-root count and victory state; they add no phases, targets or rewards.

Resolved creature attacks begin at contact, followed by cosmetic recovery. New authoritative start events interrupt old follow-through; windup warnings remain readable when damage lands. Target impact effects and health changes still follow actual Core events. All actor-root transforms, hit detection, damage, encounter timing, save formats and replay commands remain unchanged.

Pause freezes the rigs and transitions. Reduced Effects suppresses optional breathing, and the arena flower settles root-release and victory transitions even while paused. Restoring effects does not replay them; loading a completed encounter starts with the settled aftermath. Character attack and death poses remain visible for readability.

The rigs allocate joints and meshes when built. Animation changes existing transforms and the flower's private material; it creates no geometry per frame. Creature motion selection is limited to the authored Verdant IDs. Shared plant mesh construction also gives the existing plant variants articulated petals and root tendrils, with their previous generic motion unless selected by this pass. Co-op shares the creature rigs and anticipation; solo event-driven clips still require the solo event adapter.

See [verification](verdant_animation_verification.md) for native captures, campaign replay checks and Prism review.
