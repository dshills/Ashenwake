using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task CreatureGallery()
    {
        for (int group = 0; group < 2; group++)
        {
            foreach (var child in _gallery.GetChildren()) child.Free();
            _heading.Text = group == 0 ? "THE VERDANT MAW / HUNGRY ROOTS" : "THE VERDANT MAW / WEIGHT AND INTENT";
            _caption.Text = group == 0
                ? "Carnivorous vine · feeding root · needle swarm\nLiving tendrils and crawling insects gather before the strike."
                : "Bloom carrier · the Antler · Rootheart\nDistinct anticipation poses make each creature's next strike readable.";
            for (int i = 0; i < 3; i++)
            {
                var (id, role, _) = CreatureMotionChecks.Creatures[group * 3 + i];
                var visual = CharacterVisual.Create(id, role);
                visual.Position = new((1 - i) * 5.2f, 0, 0); _gallery.AddChild(visual);
                Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
            }
            await Capture(group == 0 ? "verdant-small-creature-anticipation.png" : "verdant-large-creature-anticipation.png");
        }

        foreach (var (id, role, title, filename) in new[]
        {
            ("boss.rootheart", "BellSaint", "ROOTHEART / THE STRIKE AND THE FALL", "rootheart-motion-strip.png"),
            ("boss.antler", "Beast", "THE ANTLER / A HEAVY HUNTER", "antler-motion-strip.png")
        })
        {
            foreach (var child in _gallery.GetChildren()) child.Free();
            _heading.Text = title;
            _caption.Text = "Anticipation · contact · recovery · collapse\nThe actual gameplay rig, frozen across its cosmetic motion. Combat timing remains authoritative.";
            for (int i = 0; i < 4; i++)
            {
                var visual = CharacterVisual.Create(id, role);
                visual.Position = new((1.5f - i) * 4.15f, 0, 0); _gallery.AddChild(visual);
                Advance(visual, .3); visual.BeginAttackWindup(); Advance(visual, .4, windup: true);
                if (i > 0) { visual.React("attack"); visual.Animate(0, Vector3.Zero, windup: true); }
                if (i == 2) Advance(visual, .5, state: "Recover");
                if (i == 3) { visual.React("death"); Advance(visual, 2); }
            }
            await Capture(filename);
        }
    }
}
