using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class AppearanceSmoke
{
    private async Task ArmorGallery(Node3D gallery, Camera3D camera, CanvasLayer canvas, Label title, Label subtitle)
    {
        string[] disciplines = ["Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden"];
        EquipmentSlot[] slots = [EquipmentSlot.Shoulders, EquipmentSlot.Gloves, EquipmentSlot.Belt, EquipmentSlot.Legs, EquipmentSlot.Boots];
        var captions = new Control(); canvas.AddChild(captions);
        for (int i = 0; i < disciplines.Length; i++)
        {
            float left = 46 + i * 241;
            var name = new Label { Text = disciplines[i], Position = new(left, 623), Size = new(222, 30), HorizontalAlignment = HorizontalAlignment.Center };
            name.AddThemeColorOverride("font_color", new("e8d7b2")); captions.AddChild(name);
            for (int j = 0; j < slots.Length; j++)
            {
                var icon = new GearItemIcon { Position = new(left + 3 + j * 43, 670), Size = new(40, 40) };
                icon.Configure("item.starter_" + slots[j].ToString().ToLowerInvariant(), slots[j], disciplines[i], ItemRarity.Common);
                captions.AddChild(icon);
            }
        }
        camera.Position = new(0, 3.6f, -14); camera.LookAt(new(0, 1.1f, 0)); camera.Size = 8;
        foreach (string mode in new[] { "equipped", "armor-only", "removed", "moving", "rear" })
        {
            title.Text = "THE GREYHAVEN WARDROBE";
            subtitle.Text = mode switch
            {
                "armor-only" => "Five independent armor slots · funeral cloth, ivory guards, ritual seals and ember seams",
                "removed" => "Shoulders, gloves, belt, leg armor and boots removed · the underlying clothing remains",
                "moving" => "Equipment follows the existing joints · walking pose and three-quarter view",
                "rear" => "Rear view · split robes retain their side and back panels",
                _ => "Wakeguard Mantle · Gravesoil Grips · Last-Rite Girdle · Mourner's Greaves · Cindertrail Boots"
            };
            var actors = new Node3D(); gallery.AddChild(actors);
            for (int i = 0; i < disciplines.Length; i++)
            {
                var appearance = new CharacterAppearance(disciplines[i], new("item.starter_mainhand"), new("item.starter_offhand"),
                    new("item.starter_head"), new("item.starter_chest"))
                {
                    Shoulders = new("item.starter_shoulders"),
                    Gloves = new("item.starter_gloves"),
                    Belt = new("item.starter_belt"),
                    Legs = new("item.starter_legs"),
                    Boots = new("item.starter_boots")
                };
                if (mode == "armor-only") appearance = appearance with { MainHand = ItemAppearance.Empty, OffHand = ItemAppearance.Empty, Head = ItemAppearance.Empty, Chest = ItemAppearance.Empty };
                if (mode == "removed") appearance = appearance with { Shoulders = ItemAppearance.Empty, Gloves = ItemAppearance.Empty, Belt = ItemAppearance.Empty, Legs = ItemAppearance.Empty, Boots = ItemAppearance.Empty };
                var actor = CharacterVisual.Create("player", "Player", disciplines[i], appearance: appearance);
                actor.Position = new((2 - i) * 2.42f, 0, 0); actors.AddChild(actor);
                for (int tick = 0; tick < 70; tick++)
                    actor.Animate(1.0 / 60, mode == "moving" ? Vector3.Right : Vector3.Zero, facing: new Vector3(mode == "moving" ? -.5f : .12f, 0, mode == "rear" ? 1 : -1));
            }
            await Capture("armor-" + mode + ".png");
            gallery.RemoveChild(actors); actors.QueueFree(); await Frames();
        }
        canvas.RemoveChild(captions); captions.QueueFree();
        camera.Position = new(0, 4, -14); camera.LookAt(new(0, 1.3f, 0)); camera.Size = 11.8f;
    }
}
