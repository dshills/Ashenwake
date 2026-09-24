using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task SpineCreatureGallery()
    {
        var creatures = CreatureMotionChecks.Creatures.Where(creature => creature.Kind is "Giant" or "Keeper" or "BoneSentinel" or "Warden").ToArray();
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "THE SHATTERED SPINE / WEIGHT AND WILL";
        _caption.Text = "Oath giant · contract keeper · bone sentinel · Covenant Warden\nHeavy blows, ritual gestures and disciplined defenses.";
        for (int i = 0; i < creatures.Length; i++)
        {
            var (id, role, _) = creatures[i];
            var visual = CharacterVisual.Create(id, role);
            visual.Position = new((1.5f - i) * 4.15f, 0, 0); _gallery.AddChild(visual);
            visual.SetWardenExposed(false);
            Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
        }
        await Capture("spine-creature-anticipation.png");
        foreach (var (id, role, kind) in creatures)
        {
            foreach (var child in _gallery.GetChildren()) child.Free();
            _heading.Text = kind switch
            {
                "Giant" => "OATH GIANT / A CRUSHING OATH",
                "Keeper" => "CONTRACT KEEPER / THE RITUAL",
                "BoneSentinel" => "BONE SENTINEL / DISCIPLINE IN DEATH",
                _ => "COVENANT WARDEN / THE BROKEN SEAL"
            };
            _caption.Text = "Anticipation · contact · recovery · collapse\nThe actual gameplay rigs, frozen across their cosmetic motion.";
            for (int i = 0; i < 4; i++)
            {
                var visual = CharacterVisual.Create(id, role);
                visual.Position = new((1.5f - i) * 4.15f, 0, 0); _gallery.AddChild(visual);
                visual.SetWardenExposed(i > 0);
                Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
                if (i > 0) { visual.React("attack"); visual.Animate(0, Vector3.Zero, windup: true); }
                if (i == 2) Advance(visual, kind == "BoneSentinel" ? .32 : .52, state: "Recover");
                if (i == 3) { visual.React("death"); Advance(visual, 2); }
            }
            await Capture(id.Replace('.', '-') + "-motion-strip.png");
        }
    }
}
