using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class AppearanceSmoke
{
    private async Task LegendaryArmorGallery(Node3D gallery, Camera3D camera, CanvasLayer canvas, Label title, Label subtitle)
    {
        title.Text = "THREE OATHS IN THE ASH";
        subtitle.Text = "Pyrebound Treads · Oathkeeper's Reprisal · Widow's Last Echo\nFlame seams, a shield crest and spectral silk match their inventory silhouettes.";
        var actors = new Node3D(); gallery.AddChild(actors);
        var captions = new Control(); canvas.AddChild(captions);
        string[] ids = [LegendaryEquipment.Pyre, LegendaryEquipment.Oath, LegendaryEquipment.Widow];
        EquipmentSlot[] slots = [EquipmentSlot.Boots, EquipmentSlot.Chest, EquipmentSlot.Gloves];
        string[] disciplines = ["Vanguard", "Vanguard", "Arcanist"];
        for (int i = 0; i < ids.Length; i++)
        {
            var appearance = new CharacterAppearance(disciplines[i], ItemAppearance.Empty, ItemAppearance.Empty, ItemAppearance.Empty, new("item.starter_chest"))
            { Gloves = new("item.starter_gloves"), Legs = new("item.starter_legs"), Boots = new("item.starter_boots") };
            appearance = i switch
            {
                0 => appearance with { Boots = new(ids[i], "Legendary") },
                1 => appearance with { Chest = new(ids[i], "Legendary") },
                _ => appearance with { Gloves = new(ids[i], "Legendary") }
            };
            var actor = CharacterVisual.Create("player", "Player", disciplines[i], appearance: appearance);
            actor.Position = new((1 - i) * 3.0f, 0, 0); actors.AddChild(actor);
            for (int tick = 0; tick < 60; tick++) actor.Animate(1.0 / 60, Vector3.Zero, facing: new Vector3(.12f, 0, -1));
            float left = 120 + i * 370;
            var icon = new GearItemIcon { Position = new(left + 118, 645), Size = new(42, 42) };
            icon.Configure(ids[i], slots[i], disciplines[i], ItemRarity.Legendary); captions.AddChild(icon);
            var caption = new Label { Text = EquipmentNames.For(ids[i]), Position = new(left, 705), Size = new(280, 32), HorizontalAlignment = HorizontalAlignment.Center };
            caption.AddThemeColorOverride("font_color", new("e0c181")); captions.AddChild(caption);
        }
        camera.Position = new(0, 3.6f, -14); camera.LookAt(new(0, 1.1f, 0)); camera.Size = 7.3f;
        await Capture("legendary-armor.png");
        gallery.RemoveChild(actors); actors.QueueFree(); canvas.RemoveChild(captions); captions.QueueFree(); await Frames();
        camera.Position = new(0, 4, -14); camera.LookAt(new(0, 1.3f, 0)); camera.Size = 11.8f;
    }
}
