using Godot;

namespace Ashenwake.Client;

/// <summary>Detached visual fixtures: equipment choices change presentation without changing actor transforms.</summary>
internal static class WeaponMotionChecks
{
    public static void Run(Action<string, bool> check)
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (item, expected, cue) in new[]
        {
            ("item.cinder_edge", "sword", "slash"), ("item.dagger", "dagger", "slash"),
            ("item.ashcleaver", "axe", "heavy_slash"), ("item.oath_hammer", "hammer", "heavy_slash"),
            ("item.pilgrim_pike", "spear", "thrust"), ("item.greatstaff", "staff", "spell"),
            ("", "unarmed", "strike")
        })
        {
            var visual = Make("Vanguard", item);
            try
            {
                var root = visual.Transform;
                int nodes = Descendants(visual).Count();
                check("weapon_" + expected + "_matches_equipped_mesh", visual.ActiveWeaponMotion == expected);
                visual.Animate(.016, Vector3.Zero);
                check("weapon_" + expected + "_has_animated_anchor", visual.TryGetWeaponEffectAnchor(out var idleTip) && idleTip.IsFinite());
                visual.React("attack", "skill.cleave"); visual.Animate(0, Vector3.Zero);
                check("weapon_" + expected + "_resolved_cue_is_immediate", visual.AttackEffectCue == cue && visual.ActiveCue == "attack" &&
                    visual.TryGetWeaponEffectAnchor(out var contact) && !contact.IsEqualApprox(idleTip));
                var pose = Pose(visual);
                signatures.Add(string.Join("|", pose.Select(p => p.Basis.ToString())));
                visual.Animate(.1, Vector3.Zero, paused: true);
                check("weapon_" + expected + "_pause_preserves_pose", Same(pose, Pose(visual)));
                for (int frame = 0; frame < 75; frame++) visual.Animate(1.0 / 60, Vector3.Zero);
                check("weapon_" + expected + "_returns_without_root_motion_or_nodes", visual.ActiveCue == "" && visual.Transform.IsEqualApprox(root) && Descendants(visual).Count() == nodes);
                check("weapon_" + expected + "_grip_returns_to_rest", Descendants(visual).OfType<Node3D>()
                    .Where(n => n.Name == "EquipmentMainHand").All(n => n.Transform.IsEqualApprox(Transform3D.Identity)));
            }
            finally { visual.Free(); }
        }
        check("weapon_families_have_seven_distinct_poses", signatures.Count == 7);
        foreach (string discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" })
        {
            var visual = Make(discipline, "item.ash_axe");
            try
            {
                visual.React("attack", "skill.cleave");
                check(discipline + "_equipped_axe_overrides_discipline_motion", visual.ActiveWeaponMotion == "axe" && visual.AttackEffectCue == "heavy_slash");
            }
            finally { visual.Free(); }
        }
        var shield = Make("Vanguard", "item.ashcleaver", "item.starter_offhand");
        var empty = Make("Vanguard", "");
        var caster = Make("Arcanist", "item.cinder_edge");
        try
        {
            shield.React("attack", "skill.shield_breaker"); shield.Animate(.05, Vector3.Zero);
            check("shield_skill_uses_equipped_shield", shield.ActiveWeaponMotion == "axe" && shield.AttackEffectCue == "shield" && shield.TryGetWeaponEffectAnchor(out var point) && point.IsFinite());
            empty.React("attack", "skill.shield_breaker");
            check("unequipped_shield_does_not_animate_phantom_shield", empty.AttackEffectCue == "strike");
            caster.React("attack", "skill.fire_lance");
            check("spell_skill_preserves_cast_with_nonstaff_weapon", caster.ActiveWeaponMotion == "sword" && caster.AttackEffectCue == "spell");
            caster.Animate(.016, Vector3.Zero, windup: true);
            check("resolved_attack_survives_stale_windup_view", caster.ActiveCue == "attack");
            caster.BeginAttackWindup();
            caster.Animate(.016, Vector3.Zero, windup: true);
            check("new_authoritative_windup_supersedes_cosmetic_follow_through", caster.ActiveCue == "");
            caster.React("death"); caster.React("attack", "skill.cleave");
            check("weapon_motion_never_overrides_death", caster.ActiveCue == "death");
        }
        finally { shield.Free(); empty.Free(); caster.Free(); }
        foreach (var (id, role, family) in new[]
        {
            ("enemy.ash_ghoul", "Melee", "unarmed"), ("enemy.funeral_guard", "Armored", "spear"),
            ("enemy.memory_archer", "Ranged", "bow"), ("enemy.cinder_acolyte", "Ranged", "staff")
        })
        {
            var visual = CharacterVisual.Create(id, role);
            try
            {
                visual.Animate(.1, Vector3.Zero); var idle = Pose(visual);
                for (int i = 0; i < 18; i++) visual.Animate(1.0 / 60, Vector3.Zero, windup: true);
                var drawn = Pose(visual);
                check(id + "_weapon_specific_anticipation", visual.ActiveWeaponMotion == family && !Same(idle, drawn));
                visual.React("attack"); visual.Animate(.016, Vector3.Zero);
                check(id + "_draw_releases_at_authoritative_attack", visual.ActiveCue == "attack" && !Same(drawn, Pose(visual)));
                check(id + "_anchor_tracks_articulated_parent_chain", visual.TryGetWeaponEffectAnchor(out var tip) && tip.IsFinite());
            }
            finally { visual.Free(); }
        }
    }

    private static CharacterVisual Make(string discipline, string item, string offhand = "") => CharacterVisual.Create("player.test", "", discipline,
        appearance: new(discipline, new(item), new(offhand), ItemAppearance.Empty, ItemAppearance.Empty));
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static Transform3D[] Pose(Node node) => Descendants(node).OfType<Node3D>().Select(n => n.Transform).ToArray();
    private static bool Same(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(pair => pair.First.IsEqualApprox(pair.Second));
}
