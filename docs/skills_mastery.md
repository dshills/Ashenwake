# Skills & Mastery

Open **C → Skills**. The six cards use the same icons as the combat bar and show mastery toward mutation access. A mastered skill keeps its actual accumulated mastery count; locked ultimates show their level requirement. The Character tab retains discipline retraining and links to this screen.

Select a card, then a base form or mutation. Inspection shows the active and selected forms, delivery, resource cost, cooldown, base power, mutation multiplier, range and radius, with the mutation's existing behavior description. Locked variants remain inspectable and explain their mastery requirement. These are static ability parameters; final damage and mitigation also depend on the build, target and combat conditions. Inspecting a variant never selects it in combat.

Choose **Offense**, **Defense** or **Resource** to preview one passive point. The comparison shows ranks, passive damage/armor, combined equipment/passive resource investment, generation bonus, remaining points and materials. Resource generation increases at each full ten-point investment threshold, applies only to generating abilities and remains subject to the existing 100-resource cap. No preview invents an immediate benefit below that threshold.

Apply build changes near Mara in Greyhaven. **Preview refund** shows the points returned, bonuses lost and material fee. **Review passive refund** then opens an explicit confirmation. Canceling, switching selection, changing state or loading a session discards the pending confirmation. Completion feedback follows the director's actual transaction result. The screen keeps the selected investment visible after a successful change so additional points can be reviewed and applied individually.

The screen owns its own pause and mouse backdrop. Escape closes it; C and I keep their existing menu routing, while J, B and H close it before opening the destination menu. Save/load remains available. Closing it preserves any other pause owner, and gameplay shortcuts cannot leak into combat while it is visible.

The combat bar shows an icon, binding, ability name and readiness state. A shrinking bar and remaining time show cooldown; text distinguishes insufficient resource, the Arcanist's Heat cap and an unavailable skill. Tooltips retain the full details when compact labels truncate. Resource/cooldown-blocked buttons still dispatch their existing cast request, preserving Core's input buffering and rejection behavior; locked abilities remain disabled. Icons draw directly in Godot without textures, extra viewports or animation loops.

## Authority and validation

`ProgressionSession.PreviewBuild` restores an isolated character and invokes the existing allocation, respec or mutation transaction with a collision-safe hypothetical receipt. The UI does not grant points, mastery or mutations. Detached before/after snapshots and progression views provide the comparison. `CombatSession.InspectSkill` exposes read-only static skill/mutation metadata. Shared `PassiveEffects` formulas are used by both combat and preview presentation; their values and gameplay behavior are unchanged.

`--skills-smoke --output=<fresh-directory>` runs the earned-character UI diagnostic; add `--capture-skills` for rendered captures. It completes real expeditions to earn mastery, applies passive and mutation changes, cancels and invalidates refunds, checks saves/replays and tests three window sizes. Native respec dialog confirmation uses the public dialog signal because viewport-injected input cannot route through its separate native popup. Verification details and Prism dispositions are recorded in [the verification record](skills_mastery_verification.md).

The diagnostic also renders a separate thirty-icon gallery and checks ready, cooldown, resource-shortfall, Heat-cap, locked and charge-binding hotbar states. These isolated UI fixtures do not change the earned character or its replay history. `skills-gallery-layout.json` records every hotbar child's bounds.
