using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task HollowCreatureGallery()
    {
        var creatures = CreatureMotionChecks.Creatures.Where(creature => creature.Kind is "Shadow" or "Echo" or "Breach").ToArray();
        foreach (var child in _gallery.GetChildren()) child.Free();
        _heading.Text = "THE HOLLOW NIGHT / SHADOWS WITH INTENT";
        _caption.Text = "Doubled shadow · breach echo · Breach Heart\nUnnatural movement, readable attacks and a wound that survives defeat.";
        for (int i = 0; i < creatures.Length; i++)
        {
            var (id, role, _) = creatures[i];
            var visual = CharacterVisual.Create(id, role);
            visual.Position = new((1 - i) * 5.2f, 0, 0); _gallery.AddChild(visual);
            visual.SetBreachExposed(false);
            Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
        }
        await Capture("hollow-creature-anticipation.png");
        foreach (var (id, role, kind) in creatures)
        {
            foreach (var child in _gallery.GetChildren()) child.Free();
            _heading.Text = kind switch
            {
                "Shadow" => "DOUBLED SHADOW / THE FRACTURED STRIKE",
                "Echo" => "BREACH ECHO / AN UNNATURAL RITUAL",
                _ => "BREACH HEART / THE CONTAINED WOUND"
            };
            _caption.Text = "Anticipation · contact · recovery · collapse\nThe actual gameplay rigs, frozen across their cosmetic motion.";
            for (int i = 0; i < 4; i++)
            {
                var visual = CharacterVisual.Create(id, role);
                visual.Position = new((1.5f - i) * 4.15f, 0, 0); _gallery.AddChild(visual);
                visual.SetBreachExposed(i > 0);
                Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
                if (i > 0) { visual.React("attack"); visual.Animate(0, Vector3.Zero, windup: true); }
                if (i == 2) Advance(visual, kind == "Shadow" ? .28 : .5, state: "Recover");
                if (i == 3) { visual.React("death"); Advance(visual, 2); }
            }
            await Capture(id.Replace('.', '-') + "-motion-strip.png");
        }
    }
}
