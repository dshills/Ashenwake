using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private Panel _hudDock = null!;
    private Label _hudTitle = null!, _barrierText = null!, _potionText = null!, _dodgeText = null!, _controlHint = null!;
    private ProgressBar _barrier = null!;
    private Button _inventoryHudButton = null!, _settingsHudButton = null!, _pauseHudButton = null!, _resetHudButton = null!;
    private CombatStatusStrip _statusStrip = null!;
    private Control? _campaignObjective, _expeditionObjective;
    private Vector2 _combatHudViewport = new(-1, -1);
    private string _hudResourceName = "";
    private string ControlsHelp => $"CLICK Move / attack / interact / collect · {MovementKeys} Move · SHIFT+CLICK Stand & attack · RIGHT CLICK Secondary · {KeyDisplay(_keys["stop"])} Stop · {SkillKeys} Skills · {KeyDisplay(_keys["dodge"])} Dodge · {KeyDisplay(_keys["potion"])} Potion · {KeyDisplay(_keys["interact"])}/{KeyDisplay(_keys["pickup"])} Interact / loot";
    private string MovementKeys => string.Join("/", new[] { "up", "left", "down", "right" }.Select(a => KeyDisplay(_keys[a])));
    private string SkillKeys => string.Join("/", Enumerable.Range(1, 6).Select(i => KeyDisplay(_keys["skill" + i])));

    private void BuildCombatDock()
    {
        _hudDock = new Panel { Name = "HudDock", MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = ControlsHelp };
        _hudDock.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("0b1723ee"),
            BorderColor = new("52646b"),
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        });
        _hud.AddChild(_hudDock);
        _healthText = LabelAt("", Vector2.Zero, 14, _mint); _healthText.Name = "HudHealth";
        _health = Bar(Vector2.Zero, new(290, 8), _mint); _health.Name = "HudHealthBar";
        _barrierText = LabelAt("", Vector2.Zero, 10, new("a9d5ef")); _barrierText.Name = "HudBarrier";
        _barrier = Bar(Vector2.Zero, new(290, 3), new("9dcbe5")); _barrier.Name = "HudBarrierBar";
        _momentumText = LabelAt("", Vector2.Zero, 14, _ember); _momentumText.Name = "HudResource";
        _momentum = Bar(Vector2.Zero, new(260, 8), _ember); _momentum.Name = "HudResourceBar";
        _potionText = LabelAt("", Vector2.Zero, 12, new("eac1bd")); _potionText.Name = "HudPotion";
        _dodgeText = LabelAt("", Vector2.Zero, 12, new("c0d5df")); _dodgeText.Name = "HudDodge";
        _inventoryHudButton = ButtonAt("Inventory [I]", Vector2.Zero, new(94, 28), ShowInventory);
        _settingsHudButton = ButtonAt("Settings [Esc]", Vector2.Zero, new(104, 28), () => TogglePanel(_settingsPanel));
        _pauseHudButton = ButtonAt("Pause [P]", Vector2.Zero, new(84, 28), ToggleManualPause);
        foreach (var button in new[] { _inventoryHudButton, _settingsHudButton, _pauseHudButton }) button.AddThemeFontSizeOverride("font_size", 11);
        _inventoryHudButton.Size = new(94, 28); _settingsHudButton.Size = new(104, 28); _pauseHudButton.Size = new(84, 28);
        _statusStrip = new CombatStatusStrip { Name = "HudStatuses" }; _hud.AddChild(_statusStrip);
    }

    private void LayoutCombatHud()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        if (_resumePanel is not null) _resumePanel.Position = (viewport - _resumePanel.Size) / 2;
        if (_combatHudViewport == viewport) return;
        _combatHudViewport = viewport;
        _inventoryHudButton.Text = $"Inventory [{KeyDisplay(_keys["inventory"])}]";
        _settingsHudButton.Text = $"Settings [{KeyDisplay(_keys["settings"])}]";
        _pauseHudButton.Text = $"Pause [{KeyDisplay(_keys["pause"])}]";
        float margin = viewport.X < 900 ? 16 : 22, width = viewport.X - margin * 2;
        float top = viewport.Y - 196, left = margin + 12, inside = width - 24;
        _hudDock.Position = new(margin, top); _hudDock.Size = new(width, 184);
        _hudTitle.Position = new(24, 17); _hudTitle.AddThemeFontSizeOverride("font_size", viewport.X < 900 ? 23 : 27);
        _subtitleLabel.Position = new(25, 54); _subtitleLabel.ClipText = true; _subtitleLabel.Size = new(viewport.X - 325, 20);
        float healthWidth = inside * .37f, resourceWidth = inside * .31f, resourceLeft = left + healthWidth + 18;
        float recoveryLeft = resourceLeft + resourceWidth + 18;
        _healthText.Position = new(left, top + 8); _healthText.Size = new(healthWidth, 20);
        _health.Position = new(left, top + 30); _health.Size = new(healthWidth, 7);
        _barrierText.Position = new(left, top + 39); _barrierText.Size = new(healthWidth, 14);
        _barrier.Position = new(left, top + 37); _barrier.Size = new(healthWidth, 2);
        _momentumText.Position = new(resourceLeft, top + 8); _momentumText.Size = new(resourceWidth, 20);
        _momentum.Position = new(resourceLeft, top + 30); _momentum.Size = new(resourceWidth, 7);
        _potionText.Position = new(recoveryLeft, top + 8); _potionText.Size = new(Math.Max(100, inside - recoveryLeft + left), 18);
        _dodgeText.Position = new(recoveryLeft, top + 30); _dodgeText.Size = new(_potionText.Size.X, 18);
        foreach (var label in new[] { _healthText, _momentumText }) label.AddThemeFontSizeOverride("font_size", viewport.X < 900 ? 12 : 14);
        foreach (var label in new[] { _potionText, _dodgeText }) label.AddThemeFontSizeOverride("font_size", viewport.X < 900 ? 11 : 12);
        float skillWidth = (inside - 30) / 6;
        for (int i = 0; i < _skillButtons.Count; i++)
        { _skillButtons[i].Position = new(left + i * (skillWidth + 6), top + 59); _skillButtons[i].Size = new(skillWidth, 54); }
        _experiencePanel.Position = new(left, top + 118); _experiencePanel.Size = new(inside, 22);
        _inventoryHudButton.Position = new(margin + width - 302, top + 147);
        _settingsHudButton.Position = new(margin + width - 202, top + 147);
        _pauseHudButton.Position = new(margin + width - 92, top + 147);
        _controlHint.Position = new(left, top + 144); _controlHint.Size = new(Math.Max(200, inside - 312), 34);
        _controlHint.ClipText = true; _controlHint.TooltipText = ControlsHelp;
        _controlHint.Text = viewport.X < 1000 ? $"CLICK move / interact · {MovementKeys} move\nSHIFT+CLICK stand & attack · {SkillKeys} skills" :
            $"CLICK move / attack / interact / collect · {MovementKeys} move · SHIFT+CLICK stand & attack\nRIGHT CLICK secondary · {KeyDisplay(_keys["stop"])} stop · {KeyDisplay(_keys["interact"])} interact · {KeyDisplay(_keys["pickup"])} collect · {SkillKeys} skills";
        _statusStrip.Position = new(left, top - 49); _statusStrip.Size = new(Math.Min(600, inside), 44);
        if (_navigationNotice is not null) { _navigationNotice.Position = new(left, top - 71); _navigationNotice.Size = new(inside, 20); _navigationNotice.ClipText = true; }
        _rewardFeed.Position = new(viewport.X - 302, 182); _rewardFeed.Size = new(280, 190);
        _resetHudButton.Position = new(viewport.X - 126, top - 38);
        foreach (var panel in new[] { _inventoryPanel })
            if (panel is not null) panel.Position = new(Math.Max(22, viewport.X - panel.Size.X - 22), Math.Max(22, Math.Min(105, viewport.Y - panel.Size.Y - 22)));
        LayoutCombatTarget();
    }

    private void LayoutCombatTarget()
    {
        if (_targetDetail is null) return;
        float width = GetViewport().GetVisibleRect().Size.X;
        float top = 218;
        top = Math.Max(top, TrainingSummaryBottom() + 10);
        if (_campaignObjective is not null && _campaignObjective.IsVisibleInTree()) top = Math.Max(top, _campaignObjective.GetGlobalRect().End.Y + 10);
        if (_expeditionObjective is not null && _expeditionObjective.IsVisibleInTree()) top = Math.Max(top, _expeditionObjective.GetGlobalRect().End.Y + 10);
        if (_endgameRulePanel?.IsVisibleInTree() == true) top = Math.Max(top, _endgameRulePanel.GetGlobalRect().End.Y + 10);
        _targetDetail.Position = width >= 1220 && !_trainingPresentation ? new(615, 88) : new(22, top);
        _targetDetail.Size = new(Math.Min(width < 900 && _localMap is not null ? 250 : 286, width - 340), 104);
    }

    private void RefreshCombatDock(CombatActorView player)
    {
        _barrierText.Text = _view.Barrier > 0 ? $"BARRIER  {_view.Barrier}" : "BARRIER  —";
        _barrier.Value = Math.Clamp(_view.Barrier * 100d / Math.Max(1, player.MaxHealth), 0, 100);
        _barrier.Visible = _view.Barrier > 0;
        _statusStrip.SetView(player);
        string KeyName(string action) => _keys[action].ToString().Replace("Key", "", StringComparison.Ordinal);
        _potionText.Text = $"[{KeyName("potion")}] Potion  {_view.PotionCharges} · " + (_view.PotionCharges == 0 ? "Empty" : _view.PotionCooldownTicks > 0 ? $"{_view.PotionCooldownTicks / 30d:0.0}s" : "Ready");
        _dodgeText.Text = $"[{KeyName("dodge")}] Dodge · " + (_view.DodgeCooldownTicks > 0 ? $"{_view.DodgeCooldownTicks / 30d:0.0}s" : "Ready");
        if (_hudResourceName == _view.ResourceName) return;
        _hudResourceName = _view.ResourceName;
        Color color = _view.ResourceName switch
        {
            "Instability" => new("f5a06d"),
            "Exposure" => new("b9a0df"),
            "Remains" => new("abd6a6"),
            "Adaptation" => new("a4c9e8"),
            _ => _ember
        };
        _momentumText.AddThemeColorOverride("font_color", color);
        _momentum.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color });
    }
}
