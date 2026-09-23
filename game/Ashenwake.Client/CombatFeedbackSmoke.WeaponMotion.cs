using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private void CheckWeaponEffects()
    {
        var holder = new Node3D { Position = new(4, 0, -3), Rotation = new(0, .4f, 0) };
        AddChild(holder);
        var visual = WeaponFixture("item.ashcleaver"); holder.AddChild(visual);
        var effects = new CombatEffects { Position = new(-2, .2f, 1) }; AddChild(effects);
        try
        {
            visual.React("attack"); visual.Animate(.016, Vector3.Zero);
            effects.EmitWeapon(visual, Godot.Colors.Gold);
            effects.Advance(.016, false, false);
            var first = Descendants(effects).OfType<MeshInstance3D>().First();
            visual.TryGetWeaponEffectAnchor(out var tip);
            Check("weapon_effect_uses_animated_world_contact", first.GlobalPosition.IsEqualApprox(visual.ToGlobal(tip)));
            var frozen = first.GlobalTransform;
            visual.Animate(.1, Vector3.Zero, paused: true); effects.Advance(.1, true, false);
            Check("weapon_effect_pause_preserves_contact", first.GlobalTransform.IsEqualApprox(frozen));
            holder.Position += Vector3.Right; visual.Animate(.04, Vector3.Zero); effects.Advance(.04, false, false);
            visual.TryGetWeaponEffectAnchor(out tip);
            Check("weapon_effect_follows_actor_and_swing", first.GlobalPosition.IsEqualApprox(visual.ToGlobal(tip)) && !first.GlobalTransform.IsEqualApprox(frozen));
            visual.React("dodge"); effects.Advance(.016, false, false);
            Check("weapon_effect_yields_to_dodge", effects.Count == 0);
            effects.EmitWeapon(visual, Godot.Colors.Gold);
            Check("weapon_effect_does_not_start_during_dodge", effects.Count == 0);
            Advance(visual, .5); visual.React("attack");
            for (int i = 0; i < 90; i++) effects.EmitWeapon(visual, Godot.Colors.Gold);
            Check("weapon_effect_pool_is_bounded", effects.PoolCount <= CombatEffects.Maximum);
            effects.Advance(.016, true, true);
            Check("weapon_effect_reduced_clears_even_when_paused", effects.Count == 0);
            effects.EmitWeapon(visual, Godot.Colors.Gold, true);
            Check("weapon_effect_reduced_suppresses_emission", effects.Count == 0);
            effects.EmitWeapon(visual, Godot.Colors.Gold); visual.Free(); effects.Advance(.016, false, false);
            Check("weapon_effect_releases_freed_actor", effects.Count == 0);
            effects.Emit("hit", new(2, 0, 1), Vector3.Forward, Godot.Colors.White);
            effects.Advance(.016, false, false);
            Check("ordinary_impact_can_reuse_weapon_pool", effects.Count == 1);
            Check("weapon_effect_has_no_physics", !Descendants(effects).Any(n => n is CollisionObject3D));
        }
        finally { effects.Free(); holder.Free(); }
    }

    private async Task WeaponGallery()
    {
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "THE WEAPON SHAPES THE STRIKE";
        _caption.Text = "Sword · axe · hammer · spear · staff\nEquipped weapons at contact; their glints follow the moving hand or weapon tip.";
        string[] items = ["item.cinder_edge", "item.ashcleaver", "item.oath_hammer", "item.pilgrim_pike", "item.greatstaff"];
        var effects = new CombatEffects(); _gallery.AddChild(effects);
        var actors = new List<CharacterVisual>();
        for (int i = 0; i < items.Length; i++)
        {
            var visual = WeaponFixture(items[i]); visual.Position = new((2 - i) * 3.1f, 0, 0); _gallery.AddChild(visual);
            Advance(visual, .3); visual.React("attack"); visual.Animate(0, Vector3.Zero);
            effects.EmitWeapon(visual, new Color("ffc88d")); actors.Add(visual);
        }
        for (int frame = 0; frame < 4; frame++)
        {
            foreach (var visual in actors) visual.Animate(1.0 / 60, Vector3.Zero);
            effects.Advance(1.0 / 60, false, false);
        }
        await Capture("weapon-contact-poses.png");
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "A HEAVY STRIKE / CONTACT TO RECOVERY";
        _caption.Text = "Ashcleaver: contact · follow-through · settling · ready\nDamage resolves at the first pose; recovery is cosmetic.";
        for (int i = 0; i < 4; i++)
        {
            var visual = WeaponFixture("item.ashcleaver"); visual.Position = new((1.5f - i) * 3.25f, 0, 0); _gallery.AddChild(visual);
            Advance(visual, .3); visual.React("attack"); visual.Animate(0, Vector3.Zero);
            Advance(visual, i * .24);
        }
        await Capture("weapon-follow-through.png");
    }

    private static CharacterVisual WeaponFixture(string item) => CharacterVisual.Create("player.vanguard", "", "Vanguard",
        appearance: new("Vanguard", new(item), ItemAppearance.Empty, ItemAppearance.Empty, ItemAppearance.Empty));
}
