using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task CinderCreatureGallery()
    {
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "CINDER REACH / FIRE, IRON AND WEIGHT";
        _caption.Text = "Emberling · furnace brute · forge sentinel · Furnace Spindle\nDifferent bodies prepare different strikes. These are the actual gameplay rigs.";
        var creatures = CreatureMotionChecks.Creatures.Where(creature => creature.Kind is "Emberling" or "Brute" or "Sentinel" or "Spindle").ToArray();
        for (int i = 0; i < creatures.Length; i++)
        {
            var (id, role, _) = creatures[i];
            var visual = CharacterVisual.Create(id, role);
            visual.Position = new((1.5f - i) * 4.15f, 0, 0); _gallery.AddChild(visual);
            visual.SetFurnaceExposed(false);
            Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
        }
        await Capture("cinder-creature-anticipation.png");
        foreach (var (id, role, title, name) in new[]
        {
            ("enemy.emberling", "Rusher", "EMBERLING / HEAT AND COLLAPSE", "emberling"),
            ("enemy.furnace_brute", "Armored", "FURNACE BRUTE / A CRUSHING STRIKE", "furnace-brute"),
            ("boss.furnace_spindle", "BellSaint", "FURNACE SPINDLE / EXPOSED AND SPENT", "furnace-spindle")
        })
        {
            foreach (var child in _gallery.GetChildren()) child.Free();
            _heading.Text = title;
            _caption.Text = "Anticipation · contact · recovery · collapse\nDamage stays authoritative. The body follows through and settles without moving its actor root.";
            bool emberling = id == "enemy.emberling";
            if (emberling) _caption.Text = "Idle · anticipation · detonation · collapse\nThe emberling spends its life in the blast. Its final motion follows that actual outcome.";
            for (int i = 0; i < 4; i++)
            {
                var visual = CharacterVisual.Create(id, role);
                visual.Position = new((1.5f - i) * 4.15f, 0, 0); _gallery.AddChild(visual);
                visual.SetFurnaceExposed(i > 0);
                if (emberling)
                {
                    Advance(visual, .3);
                    if (i > 0) { visual.BeginAttackWindup(); Advance(visual, .4, windup: true); }
                    if (i > 1) { visual.React("death"); Advance(visual, i == 2 ? .14 : 2); }
                    continue;
                }
                Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
                if (i > 0) { visual.React("attack"); visual.Animate(0, Vector3.Zero, windup: true); }
                if (i == 2) Advance(visual, .58, state: "Recover");
                if (i == 3) { visual.React("death"); Advance(visual, 2); }
            }
            await Capture(name + "-motion-strip.png");
        }
    }
}
