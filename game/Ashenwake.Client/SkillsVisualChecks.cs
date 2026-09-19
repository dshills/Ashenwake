using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

public partial class SkillsSmoke
{
    private async Task CheckSkillVisuals()
    {
        // Isolated UI projection fixtures: these sessions and button counters never touch the earned character.
        string earnedHash = _session.StateHash, appearance = _sandbox.CurrentAppearance.Key;
        int requests = _requests, history = _session.CaptureReplay().Frames.Length;
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(5);
        var content = CombatContent.Parse(_combatJson);
        var layer = new CanvasLayer { Name = "SkillVisualFixture", Layer = 80 };
        AddChild(layer);
        var gallery = new Control { Name = "SkillVisualGallery", Size = new(1280, 800), MouseFilter = Control.MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        layer.AddChild(gallery);
        gallery.AddChild(new ColorRect { Color = new("0b1821"), Size = new(1280, 800), MouseFilter = Control.MouseFilterEnum.Ignore });
        VisualLabel(gallery, "SKILL GLYPHS & COMBAT HOTBAR", new(32, 20), new(1216, 30), 23, new("e8ddc7"));
        VisualLabel(gallery, "Thirty authored skills · five discipline views · isolated visual fixtures", new(32, 58), new(1216, 22), 13, new("9cb6c3"));
        var fixtures = new Dictionary<string, CombatSession>(StringComparer.Ordinal);
        var icons = new List<SkillIcon>();
        var views = new List<CombatSkillView>();
        var examples = new List<SkillButton>();
        try
        {
            for (int row = 0; row < CombatSession.Disciplines.Count; row++)
            {
                string discipline = CombatSession.Disciplines[row];
                var fixture = CombatSession.CreateEncounter(_combatJson, (ulong)(701 + row), "hub");
                fixture.ApplyProgressionBuild(new(Discipline: discipline, UltimateUnlocked: false));
                fixtures.Add(discipline, fixture);
                var view = fixture.View;
                VisualLabel(gallery, discipline.ToUpperInvariant(), new(32, 114 + row * 84), new(144, 24), 14, new("b6cbd3"));
                for (int column = 0; column < view.Skills.Count; column++)
                {
                    var skill = view.Skills[column]; views.Add(skill);
                    var tile = new Panel
                    {
                        Name = "Glyph" + discipline + column,
                        Position = new(186 + column * 171, 94 + row * 84),
                        Size = new(162, 70),
                        MouseFilter = Control.MouseFilterEnum.Ignore
                    };
                    tile.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                    {
                        BgColor = new("132833"),
                        BorderColor = new("2b4654"),
                        BorderWidthTop = 1,
                        BorderWidthBottom = 1,
                        BorderWidthLeft = 1,
                        BorderWidthRight = 1,
                        CornerRadiusTopLeft = 4,
                        CornerRadiusTopRight = 4,
                        CornerRadiusBottomLeft = 4,
                        CornerRadiusBottomRight = 4
                    });
                    gallery.AddChild(tile);
                    var icon = new SkillIcon { Position = new(8, 12), Size = new(42, 42) };
                    tile.AddChild(icon); icon.SetSkill(skill.Id, discipline, skill.Shape); icons.Add(icon);
                    VisualLabel(tile, skill.Name, new(57, 9), new(99, 33), 11, new("e0e9e9"));
                    VisualLabel(tile, skill.Shape, new(57, 48), new(99, 17), 10, new("91acb9"));
                }
            }
            Check("visual_gallery_uses_all_thirty_authored_skill_views", views.Count == 30 &&
                views.Select(skill => skill.Id).ToHashSet(StringComparer.Ordinal).SetEquals(content.Skills.Where(skill => CombatSession.Disciplines.Contains(skill.Discipline)).Select(skill => skill.Id)) &&
                views.All(skill => content.Skills.Any(authored => authored.Id == skill.Id && authored.Name == skill.Name && authored.Shape == skill.Shape)));
            Check("visual_glyphs_have_distinct_keys_and_ignore_input", icons.Select(icon => icon.IconKey).Distinct(StringComparer.Ordinal).Count() == 30 &&
                icons.All(icon => icon.MouseFilter == Control.MouseFilterEnum.Ignore && icon.GetChildCount() == 0));

            VisualLabel(gallery, "HOTBAR STATES · ORIGINAL 148 × 54 FOOTPRINT", new(32, 526), new(1216, 25), 15, new("e8ddc7"));
            var vanguard = fixtures["Vanguard"].View;
            var arcanist = fixtures["Arcanist"].View;
            var readySkill = vanguard.Skills.Single(skill => skill.Id == "skill.cleave");
            var shortSkill = vanguard.Skills.Single(skill => skill.Id == "skill.shield_breaker");
            var heatSkill = arcanist.Skills.Single(skill => skill.Id == "skill.fire_lance");
            var lockedSkill = vanguard.Skills.Single(skill => skill.Id == "skill.cataclysm");
            var events = fixtures["Arcanist"].Step([new(CombatCommandKind.Cast, SkillId: "skill.vent")]);
            var cooldownView = fixtures["Arcanist"].View;
            var cooldownSkill = cooldownView.Skills.Single(skill => skill.Id == "skill.vent");
            fixtures["Vanguard"].Step([new(CombatCommandKind.SetMutation, SkillId: "skill.shield_breaker", ContentId: "mutation.orruns_patience")]);
            var chargeView = fixtures["Vanguard"].View;
            var chargeSkill = chargeView.Skills.Single(skill => skill.Id == "skill.shield_breaker");

            SkillButton Example(int index, string heading, CombatSkillView skill, CombatView view, string binding, int? resource = null)
            {
                var position = new Vector2(32 + index * 204, 592);
                VisualLabel(gallery, heading, position - new Vector2(0, 25), new(185, 20), 11, new("91acb9"));
                var button = new SkillButton { Name = "VisualSkill" + index, Position = position, Size = new(148, 54) };
                gallery.AddChild(button);
                button.SetSkill(skill, view.Discipline, view.ResourceName, resource ?? view.Resource, view.MaxResource, binding);
                examples.Add(button); return button;
            }
            var ready = Example(0, "READY", readySkill, vanguard, "1");
            var cooldown = Example(1, "COOLDOWN · REAL VENT CAST", cooldownSkill, cooldownView, "4");
            var shortfall = Example(2, "RESOURCE SHORTFALL", shortSkill, vanguard, "2");
            // A deliberate boundary argument to the UI adapter, not an edited combat snapshot or earned resource grant.
            var heat = Example(3, "HEAT CAP · UI BOUNDARY", heatSkill, arcanist, "1", arcanist.MaxResource);
            var locked = Example(4, "LOCKED ULTIMATE", lockedSkill, vanguard, "6");
            var charge = Example(5, "CONFIGURED CHARGE BINDING", chargeSkill, chargeView, "K");
            VisualLabel(gallery, "Cooldown uses exact Core ticks. Resource-blocked buttons remain clickable; locked skills do not dispatch.", new(32, 674), new(1216, 24), 13, new("bfd4db"));
            VisualLabel(gallery, "Heat cap is a UI-only max-resource boundary. All other examples come directly from isolated CombatSession views.", new(32, 706), new(1216, 24), 12, new("91acb9"));
            VisualLabel(gallery, "No earned mastery, items, resources, command history or live character state are changed by this gallery.", new(32, 749), new(1216, 22), 12, new("90c6af"));
            await Frames(5);

            Check("visual_ready_state_preserves_name_binding_and_resource_generation", ready.StateText == $"+{readySkill.Generate} {vanguard.ResourceName}" &&
                ready.Text == "1  " + readySkill.Name + "\n" + ready.StateText && !ready.Disabled && ready.CooldownRemainingTicks == 0);
            string seconds = (cooldownSkill.RemainingTicks / 30d).ToString("0.00", CultureInfo.InvariantCulture) + "s";
            var progress = cooldown.GetNode<ProgressBar>("SkillCooldown");
            Check("visual_cooldown_uses_real_remaining_ticks_and_progress", events.Any(e => e.Kind == "AbilityStarted" && e.ContentId == "skill.vent") &&
                cooldownSkill.RemainingTicks > 0 && cooldown.StateText == "CD " + seconds && cooldown.CooldownRemainingTicks == cooldownSkill.RemainingTicks &&
                progress.Value == cooldownSkill.RemainingTicks && progress.MaxValue == cooldownSkill.CooldownTicks && !cooldown.Disabled &&
                cooldown.TooltipText.Contains($"({cooldownSkill.RemainingTicks} ticks of {cooldownSkill.CooldownTicks}", StringComparison.Ordinal));
            Check("visual_resource_shortfall_is_explicit_without_disabling_cast", vanguard.Resource < shortSkill.Cost &&
                shortfall.StateText == $"Need {shortSkill.Cost}" && !shortfall.Disabled && shortfall.TooltipText.Contains("Insufficient " + vanguard.ResourceName, StringComparison.Ordinal));
            Check("visual_heat_cap_uses_additive_resource_rule", heatSkill.ResourceMode == "Heat" && heat.StateText == "Heat cap" && !heat.Disabled &&
                heat.TooltipText.Contains($"{arcanist.MaxResource} + {heatSkill.Cost} exceeds {arcanist.MaxResource}", StringComparison.Ordinal));
            Check("visual_locked_ultimate_is_distinct_and_disabled", !lockedSkill.Available && locked.Disabled && locked.StateText == "Locked");
            Check("visual_orrun_charge_tooltip_uses_configured_binding", chargeSkill.Mutation == "mutation.orruns_patience" &&
                charge.Text.StartsWith("K  ", StringComparison.Ordinal) && charge.TooltipText.Contains("Hold K / right click", StringComparison.Ordinal));
            var children = examples.Select(button => button.GetChildren().Select(child => child.GetInstanceId()).ToArray()).ToArray();
            string iconKey = icons[0].IconKey;
            icons[0].SetSkill(readySkill.Id, "Vanguard", readySkill.Shape);
            cooldown.SetSkill(cooldownSkill, cooldownView.Discipline, cooldownView.ResourceName, cooldownView.Resource, cooldownView.MaxResource, "4");
            Check("visual_repeated_configuration_reuses_glyphs_and_controls", icons[0].IconKey == iconKey &&
                examples.Select((button, i) => button.GetChildren().Select(child => child.GetInstanceId()).SequenceEqual(children[i])).All(value => value));
            File.WriteAllText(Path.Combine(_output, "skills-gallery-layout.json"), JsonData.Write(examples.Select(button => new
            {
                skill = button.SkillId,
                bounds = button.GetGlobalRect().ToString(),
                children = button.GetChildren().OfType<Control>().Select(child => new
                { name = child.Name.ToString(), bounds = child.GetGlobalRect().ToString(), fits = button.GetGlobalRect().Encloses(child.GetGlobalRect()) }).ToArray()
            }).ToArray()));
            await Capture("skills-icons-and-hotbar.png");
            Check("visual_gallery_and_hotbar_fit_1280x800", icons.All(icon => GetViewport().GetVisibleRect().Encloses(icon.GetGlobalRect())) &&
                examples.All(button => button.Size == new Vector2(148, 54) && GetViewport().GetVisibleRect().Encloses(button.GetGlobalRect()) &&
                    button.GetChildren().OfType<Control>().All(child => button.GetGlobalRect().Encloses(child.GetGlobalRect()))));
            int readyClicks = 0, cooldownClicks = 0, shortClicks = 0, heatClicks = 0, lockedClicks = 0;
            ready.Pressed += () => readyClicks++; cooldown.Pressed += () => cooldownClicks++; shortfall.Pressed += () => shortClicks++;
            heat.Pressed += () => heatClicks++; locked.Pressed += () => lockedClicks++;
            await Click(ready); await Click(cooldown); await Click(shortfall); await Click(heat); await Click(locked);
            Check("visual_native_clicks_preserve_cooldown_and_resource_dispatch_but_block_locked", readyClicks == 1 && cooldownClicks == 1 && shortClicks == 1 && heatClicks == 1 && lockedClicks == 0);
            GetViewport().PushInput(new InputEventMouseMotion { Position = new(1240, 770) }, true);
        }
        finally
        {
            RemoveChild(layer); layer.QueueFree(); await Frames();
        }
        Check("visual_projection_gallery_preserves_earned_state_history_requests_and_appearance", _session.StateHash == earnedHash &&
            _requests == requests && _session.CaptureReplay().Frames.Length == history && _sandbox.CurrentAppearance.Key == appearance);
    }

    private static Label VisualLabel(Node parent, string text, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = size,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_color", color); parent.AddChild(label); return label;
    }
}
