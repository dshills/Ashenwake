using System.Globalization;
using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>A compact hotbar button. Availability controls locking; cast readiness remains authoritative in Core.</summary>
public partial class SkillButton : Button
{
    private SkillIcon _icon = null!;
    private Label _name = null!, _binding = null!, _state = null!;
    private ProgressBar _cooldown = null!;
    private StyleBoxFlat _normal = null!, _hover = null!;
    private CombatSkillView? _lastSkill;
    private string _lastDiscipline = "", _lastResourceName = "", _lastBinding = "";
    private int _lastResource = -1, _lastMaxResource = -1;
    public string SkillId { get; private set; } = "";
    public string StateText { get; private set; } = "";
    public int CooldownRemainingTicks { get; private set; }

    public override void _Ready() => EnsureChildren();

    public void SetSkill(CombatSkillView skill, string discipline, string resourceName, int resource, int maxResource, string binding)
    {
        EnsureChildren();
        if (_lastSkill == skill && _lastDiscipline == discipline && _lastResourceName == resourceName &&
            _lastResource == resource && _lastMaxResource == maxResource && _lastBinding == binding) return;
        _lastSkill = skill; _lastDiscipline = discipline; _lastResourceName = resourceName;
        _lastResource = resource; _lastMaxResource = maxResource; _lastBinding = binding;
        SkillId = skill.Id; CooldownRemainingTicks = skill.RemainingTicks;
        _icon.SetSkill(skill.Id, discipline, skill.Shape);
        _name.Text = skill.Name; _binding.Text = binding;

        bool heat = skill.ResourceMode == "Heat";
        bool insufficient = heat ? resource + skill.Cost > maxResource : resource < skill.Cost;
        string seconds = (Math.Max(0, skill.RemainingTicks) / 30d).ToString("0.00", CultureInfo.InvariantCulture) + "s";
        string cost = heat ? $"+{skill.Cost} {resourceName}" : skill.Cost > 0 ? $"{skill.Cost} {resourceName}" :
            skill.Generate > 0 ? $"+{skill.Generate} {resourceName}" : "Ready · no cost";
        string shortage = heat ? "Heat cap" : $"Need {skill.Cost}";
        StateText = !skill.Available ? "Locked" : insufficient ? shortage + (skill.RemainingTicks > 0 ? " · " + seconds : "") :
            skill.RemainingTicks > 0 ? "CD " + seconds : cost;
        _state.Text = StateText;
        // Preserve the native button's label for accessibility and existing text-based diagnostics.
        Text = binding + "  " + skill.Name + "\n" + StateText;
        Disabled = !skill.Available;
        // Cooldown and insufficient-resource presses still reach Cast, preserving the existing input buffer.
        Color status = !skill.Available ? new("8396a1") : insufficient ? new("efb08b") : skill.RemainingTicks > 0 ? new("b2c7dc") : new("a1dec7");
        _state.AddThemeColorOverride("font_color", status);
        _name.AddThemeColorOverride("font_color", skill.Available ? new("e1e9e9") : new("8799a3"));
        _normal.BorderColor = status.Darkened(.45f); _hover.BorderColor = status;
        _icon.Modulate = !skill.Available ? new(.48f, .52f, .56f) : skill.RemainingTicks > 0 ? new(.72f, .78f, .83f) : Colors.White;
        _cooldown.MaxValue = Math.Max(1, skill.CooldownTicks);
        _cooldown.Value = Math.Max(0, skill.RemainingTicks);

        string readiness = !skill.Available ? "Locked: this skill is not yet available." : insufficient ?
            heat ? $"Heat cap: {resource} + {skill.Cost} exceeds {maxResource} {resourceName}." :
                $"Insufficient {resourceName}: {skill.Cost} required, {resource} available ({skill.Cost - resource} short)." : "Resource requirement met.";
        string resourceDetail = heat ? $"Adds {skill.Cost} {resourceName}; cap {maxResource}." : skill.Cost > 0 ? $"Costs {skill.Cost} {resourceName}." :
            skill.Generate > 0 ? $"Generates {skill.Generate} {resourceName}." : "No resource cost.";
        string mutation = skill.Mutation.Length == 0 ? "" : "\nMutation: " + Readable(skill.Mutation) + ".";
        string charge = skill.Mutation == "mutation.orruns_patience" ? $"\nHold {binding} / right click to charge; release to strike." : "";
        TooltipText = $"{skill.Name} [{binding}] · {skill.Shape}\n{readiness}\n{resourceDetail}\n" +
            $"Cooldown: {seconds} remaining ({skill.RemainingTicks} ticks of {skill.CooldownTicks}, at 30 ticks/s)." + mutation + charge;
    }

    private void EnsureChildren()
    {
        if (_icon is not null) return;
        ClipText = true;
        AddThemeFontSizeOverride("font_size", 11);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color", "font_outline_color" })
            AddThemeColorOverride(state, Colors.Transparent);
        _normal = Style(new("10212b"), new("496c75")); AddThemeStyleboxOverride("normal", _normal);
        _hover = Style(new("1b3440"), new("9edac6")); AddThemeStyleboxOverride("hover", _hover);
        AddThemeStyleboxOverride("pressed", Style(new("263f48"), new("ecdbb5")));
        AddThemeStyleboxOverride("disabled", Style(new("101b23"), new("344651")));
        var focus = Style(Colors.Transparent, new("f2deaa")); focus.BorderWidthBottom = focus.BorderWidthTop = focus.BorderWidthLeft = focus.BorderWidthRight = 2;
        AddThemeStyleboxOverride("focus", focus);
        _icon = new SkillIcon { Name = "SkillIcon", Position = new(7, 6), Size = new(28, 28) }; AddChild(_icon);
        _name = Caption("SkillName", new(42, 4), new(100, 27), 11); _name.AutowrapMode = TextServer.AutowrapMode.WordSmart; _name.MaxLinesVisible = 2;
        _binding = Caption("SkillBinding", new(5, 35), new(32, 13), 10); _binding.HorizontalAlignment = HorizontalAlignment.Center;
        _binding.AddThemeColorOverride("font_color", new Color("ecd6a9"));
        _state = Caption("SkillState", new(42, 34), new(100, 13), 10); _state.ClipText = true;
        _cooldown = new ProgressBar
        {
            Name = "SkillCooldown",
            Position = new(6, 49),
            Size = new(136, 3),
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None,
            MinValue = 0,
            MaxValue = 1,
            Step = 1
        };
        _cooldown.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new("2b3e48") });
        _cooldown.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new("94bed7") });
        AddChild(_cooldown);
        // Apply the compact size after theme setup removes the default percentage-label minimum.
        _cooldown.Size = new(136, 3);
    }

    private Label Caption(string name, Vector2 position, Vector2 size, int fontSize)
    {
        var label = new Label { Name = name, Position = position, Size = size, MouseFilter = MouseFilterEnum.Ignore, FocusMode = FocusModeEnum.None };
        label.AddThemeFontSizeOverride("font_size", fontSize); AddChild(label);
        // Construction initially clamps Size to the default font's minimum; use our final font here.
        label.Size = size; return label;
    }

    private static StyleBoxFlat Style(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthBottom = 1,
        BorderWidthTop = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 5,
        ContentMarginRight = 5,
        ContentMarginTop = 4,
        ContentMarginBottom = 4
    };

    private static string Readable(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Split('.').Last().Replace('_', ' '));
}
