using Godot;

namespace Ashenwake.Client;

/// <summary>Local preferences and native controls; opening this screen never advances gameplay.</summary>
public partial class Sandbox
{
    private static readonly Dictionary<string, Key> DefaultKeys = new()
    {
        ["left"] = Key.A,
        ["right"] = Key.D,
        ["up"] = Key.W,
        ["down"] = Key.S,
        ["skill1"] = Key.Key1,
        ["skill2"] = Key.Key2,
        ["skill3"] = Key.Key3,
        ["skill4"] = Key.Key4,
        ["skill5"] = Key.Key5,
        ["skill6"] = Key.Key6,
        ["dodge"] = Key.Space,
        ["potion"] = Key.Q,
        ["pickup"] = Key.E,
        ["target"] = Key.Tab,
        ["stop"] = Key.X,
        ["inventory"] = Key.I,
        ["settings"] = Key.Escape,
        ["journey"] = Key.J,
        ["localmap"] = Key.M,
        ["endgame"] = Key.B,
        ["showloot"] = Key.Alt,
        ["interact"] = Key.F,
        ["character"] = Key.C,
        ["corpse"] = Key.V,
        ["echo"] = Key.G,
        ["experiment"] = Key.H,
        ["pause"] = Key.P,
        ["step"] = Key.Period,
        ["reset"] = Key.R,
        ["save"] = Key.F5,
        ["load"] = Key.F9,
        ["replay"] = Key.F6
    };
    private static readonly string[] SettingsPages = ["Controls", "Graphics", "Audio", "Accessibility", "Gameplay"];
    private readonly Dictionary<string, VBoxContainer> _settingsPages = [];
    private readonly Dictionary<string, Button> _settingsTabs = [];
    private readonly Dictionary<string, HSlider> _volumeSliders = [];
    private readonly Dictionary<string, Button> _settingsCharacterActions = [];
    private ColorRect _settingsBackdrop = null!;
    private ScrollContainer _settingsScroll = null!;
    private Label _settingsStatus = null!, _settingsDescription = null!;
    private Button _settingsRestore = null!;
    private CheckButton _settingsEffects = null!, _settingsShake = null!;
    private OptionButton _settingsGraphicsQuality = null!, _settingsRenderScale = null!, _settingsDisplayMode = null!;
    private Label _settingsResolution = null!;
    private Vector2I _settingsResolutionPixels;
    private float _settingsResolutionScale = -1;
    private string _settingsPage = "Controls";
    private Vector2 _settingsViewport = new(-1, -1), _settingsLayoutSize, _settingsLayoutOrigin;
    private bool _syncingSettings, _settingsSaveFailed;
    private float _masterVolume = 1, _musicVolume = 1, _effectsVolume = 1, _interfaceVolume = 1;

    private void BuildSettings()
    {
        _settingsBackdrop = new ColorRect
        {
            Name = "SettingsBackdrop",
            Color = new("050b14e8"),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseForcePassScrollEvents = false
        };
        _hud.AddChild(_settingsBackdrop); _settingsBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _settingsPanel = Panel(Vector2.Zero, new(1000, 680)); _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.MouseForcePassScrollEvents = false;
        _settingsPanel.AddThemeStyleboxOverride("panel", SettingsBox(new("10202b"), new("617b85"), 20));
        var column = SettingsStack(12); _settingsPanel.AddChild(column);
        column.AddChild(SettingsText("SETTINGS & CONTROLS", 30, new("eee1c8")));
        column.AddChild(SettingsText("Make the journey your own. Changes apply immediately and save on this device.", 13));
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 8); column.AddChild(tabs);
        var group = new ButtonGroup();
        foreach (string page in SettingsPages)
        {
            var tab = SettingsButton("SettingsTab" + page, page, () => SelectSettingsPage(page));
            tab.ToggleMode = true; tab.ButtonGroup = group; tabs.AddChild(tab); _settingsTabs[page] = tab;
        }
        _settingsDescription = SettingsText("", 13, new("add7ca")); column.AddChild(_settingsDescription);
        column.AddChild(new HSeparator());
        _settingsScroll = new ScrollContainer
        {
            Name = "SettingsScroll",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            MouseForcePassScrollEvents = false
        };
        column.AddChild(_settingsScroll);
        var pages = SettingsStack(0); pages.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _settingsScroll.AddChild(pages);
        foreach (string page in SettingsPages)
        { var body = SettingsStack(12); body.Name = "SettingsPage" + page; pages.AddChild(body); _settingsPages[page] = body; }
        BuildSettingsControls(_settingsPages["Controls"]); BuildSettingsGraphics(_settingsPages["Graphics"]); BuildSettingsAudio(_settingsPages["Audio"]);
        BuildSettingsAccessibility(_settingsPages["Accessibility"]); BuildSettingsGameplay(_settingsPages["Gameplay"]);
        _settingsStatus = SettingsText(_settingsRecoveryNotice, 13, new("e8c286")); _settingsStatus.Name = "SettingsStatus";
        _settingsStatus.CustomMinimumSize = new(0, 42); column.AddChild(_settingsStatus);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 12); column.AddChild(footer);
        _settingsRestore = SettingsButton("SettingsRestore", "", RestoreSettingsPage); footer.AddChild(_settingsRestore);
        var close = SettingsButton("SettingsClose", "Close settings", () => TogglePanel(_settingsPanel)); footer.AddChild(close);
        _settingsPanel.VisibilityChanged += SettingsVisibilityChanged;
        SelectSettingsPage("Controls", false); LayoutSettings();
    }

    private void BuildSettingsControls(VBoxContainer body)
    {
        body.AddChild(SettingsText("Mouse & keyboard", 22, new("eee1c8")));
        body.AddChild(SettingsText("Left-click to move, attack, interact or collect. Shift-click attacks while standing. Right-click uses your secondary skill. Keyboard movement takes over from a click destination.", 14));
        body.AddChild(SettingsText("Choose a key below, then press its replacement. Esc cancels. Arrow keys and Enter navigate menus; Shift is reserved for standing attacks. Controller bindings stay available.", 13));
        foreach (var entry in DefaultKeys)
        {
            string action = entry.Key;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); body.AddChild(row);
            var label = SettingsText(SettingsActionName(action), 15); label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(label);
            var button = SettingsButton("SettingsBind_" + action, KeyDisplay(_keys[action]), () => BeginSettingsBinding(action));
            button.SizeFlagsHorizontal = Control.SizeFlags.Fill; button.CustomMinimumSize = new(180, 40);
            button.TooltipText = "Change " + SettingsActionName(action); row.AddChild(button); _keyButtons[action] = button;
            if (action is "step" or "reset") _sandboxControls.Add(row);
        }
    }

    private void BuildSettingsAudio(VBoxContainer body)
    {
        body.AddChild(SettingsText("Sound of the world", 22, new("eee1c8")));
        body.AddChild(SettingsText("Balance the environmental beds, combat sounds and menu feedback. Zero mutes a channel; Master affects them all.", 14));
        foreach (var entry in new[] { ("Master", "Master volume", "Overall sound level."),
            ("Music", "Music & ambience", "Regional wind, water and other environmental beds."),
            ("Effects", "Effects", "Combat impacts, abilities and warning cues."),
            ("UI", "Interface", "Feedback when using these settings controls.") })
        {
            string bus = entry.Item1;
            var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", SettingsBox(new("142631"), new("385563"), 14)); body.AddChild(card);
            var stack = SettingsStack(6); card.AddChild(stack);
            var row = new HBoxContainer(); stack.AddChild(row);
            var name = SettingsText(entry.Item2, 16, new("eee1c8")); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(name);
            var value = SettingsText("", 14, new("add7ca")); value.Name = "SettingsVolumeValue" + bus;
            value.AutowrapMode = TextServer.AutowrapMode.Off; value.CustomMinimumSize = new(65, 0); value.HorizontalAlignment = HorizontalAlignment.Right; row.AddChild(value);
            var slider = new HSlider
            {
                Name = "SettingsVolume" + bus,
                MinValue = 0,
                MaxValue = 100,
                Step = 1,
                CustomMinimumSize = new(0, 28),
                FocusMode = Control.FocusModeEnum.All,
                Value = ReadSettingsVolume(bus) * 100
            };
            _volumeSliders[bus] = slider; stack.AddChild(slider); value.Text = $"{slider.Value:0}%";
            slider.ValueChanged += number =>
            {
                value.Text = number == 0 ? "Muted" : $"{number:0}%";
                if (_syncingSettings) return;
                switch (bus)
                {
                    case "Master": _masterVolume = (float)number / 100; break;
                    case "Music": _musicVolume = (float)number / 100; break;
                    case "Effects": _effectsVolume = (float)number / 100; break;
                    case "UI": _interfaceVolume = (float)number / 100; break;
                }
                ApplySettingsAudio(); if (!Input.IsMouseButtonPressed(MouseButton.Left)) SavePreferences();
            };
            slider.DragEnded += changed => { if (changed) SavePreferences(); };
            stack.AddChild(SettingsText(entry.Item3, 12));
        }
        body.AddChild(SettingsButton("SettingsTestSound", "Test interface sound", () => { }));
    }

    private void BuildSettingsGraphics(VBoxContainer body)
    {
        body.AddChild(SettingsText("Light, shadow & atmosphere", 22, new("eee1c8")));
        body.AddChild(SettingsText("Choose the detail level that feels smooth on your device. Changes apply immediately.", 14));
        body.AddChild(SettingsText("Graphics quality", 16, new("eee1c8")));
        _settingsGraphicsQuality = new OptionButton
        {
            Name = "SettingsGraphicsQuality",
            CustomMinimumSize = new(0, 44),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "High uses 8x edge smoothing and detailed shadows. Performance uses 2x smoothing and native resolution."
        };
        _settingsGraphicsQuality.AddItem("High"); _settingsGraphicsQuality.AddItem("Performance");
        _settingsGraphicsQuality.Select(_graphicsQuality == "Performance" ? 1 : 0);
        _settingsGraphicsQuality.ItemSelected += index =>
        {
            if (_syncingSettings) return;
            SetGraphicsQuality(index == 1 ? "Performance" : "High"); SavePreferences();
        };
        body.AddChild(_settingsGraphicsQuality);
        body.AddChild(SettingsText("HIGH · 8x edge smoothing, detailed shadows and gentle bloom bring out characters, equipment and the world.", 14));
        body.AddChild(SettingsText("PERFORMANCE · 2x edge smoothing and native resolution with simpler shadows and fewer effects. Your preferred resolution is kept for High.", 14));
        body.AddChild(new HSeparator());
        body.AddChild(SettingsText("3D resolution", 16, new("eee1c8")));
        _settingsRenderScale = new OptionButton { Name = "SettingsRenderScale", CustomMinimumSize = new(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (string label in new[] { "Native · 100%", "Enhanced · 125%", "Maximum · 150%" }) _settingsRenderScale.AddItem(label);
        _settingsRenderScale.Select(_renderScale == 1 ? 0 : _renderScale == 1.5f ? 2 : 1);
        _settingsRenderScale.ItemSelected += index => { if (_syncingSettings) return; SetRenderScale(index == 0 ? 1 : index == 2 ? 1.5f : 1.25f); SavePreferences(); };
        body.AddChild(_settingsRenderScale);
        body.AddChild(SettingsText("Enhanced and Maximum render extra detail before fitting the picture to your screen. They use about 56% and 125% more 3D pixels. Choose Native if the game feels slow. Text stays sharp at every setting.", 13));
        body.AddChild(SettingsText("Display", 16, new("eee1c8")));
        _settingsDisplayMode = new OptionButton { Name = "SettingsDisplayMode", CustomMinimumSize = new(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _settingsDisplayMode.AddItem("Windowed · fit display"); _settingsDisplayMode.AddItem("Fullscreen · native display resolution");
        _settingsDisplayMode.Select(_displayMode == "Fullscreen" ? 1 : 0);
        _settingsDisplayMode.ItemSelected += index => { if (_syncingSettings) return; SetDisplayMode(index == 1 ? "Fullscreen" : "Windowed"); SavePreferences(); };
        body.AddChild(_settingsDisplayMode);
        body.AddChild(SettingsText("Windowed opens up to 1600 × 1000 and fits smaller displays. Resize or maximize the window for more screen space, or choose Fullscreen to use your whole display. Interface text scales with the window.", 13));
        _settingsResolutionScale = -1;
        _settingsResolution = SettingsText("", 13, new("add7ca")); _settingsResolution.Name = "SettingsResolution"; body.AddChild(_settingsResolution);
        body.AddChild(new HSeparator());
        body.AddChild(SettingsText("Reduced visual effects in Accessibility also suppresses bloom, whichever quality you choose.", 13, new("add7ca")));
    }

    private void BuildSettingsAccessibility(VBoxContainer body)
    {
        body.AddChild(SettingsText("Comfort & clarity", 22, new("eee1c8")));
        _settingsEffects = new CheckButton { Name = "SettingsReducedEffects", Text = "Reduced visual effects", ButtonPressed = _reduceEffects };
        _settingsEffects.Toggled += enabled => { if (_syncingSettings) return; _reduceEffects = enabled; ApplyGraphicsQuality(); SavePreferences(); };
        body.AddChild(_settingsEffects);
        body.AddChild(SettingsText("Reduces optional particles, debris and atmospheric effects, and suppresses bloom. Enemy warning areas, attack poses and combat information remain visible.", 14));
        _settingsShake = new CheckButton { Name = "SettingsReducedShake", Text = "Reduced camera shake", ButtonPressed = _reduceShake };
        _settingsShake.Toggled += enabled => { if (_syncingSettings) return; _reduceShake = enabled; SavePreferences(); };
        body.AddChild(_settingsShake);
        body.AddChild(SettingsText("Suppresses combat camera shake while keeping movement, targeting and impact feedback intact.", 14));
        body.AddChild(new HSeparator());
        body.AddChild(SettingsText("Play at your pace", 19, new("eee1c8")));
        body.AddChild(SettingsText("Menus pause solo play. Losing focus or disconnecting a controller keeps the game paused until you choose Resume. Keyboard focus follows controls as you navigate with Tab and the arrow keys.", 14));
    }

    private void BuildSettingsGameplay(VBoxContainer body)
    {
        body.AddChild(SettingsText("Loot & character tools", 22, new("eee1c8")));
        BuildLootSettings(body);
        body.AddChild(new HSeparator());
        body.AddChild(SettingsText("Character actions", 18, new("eee1c8")));
        body.AddChild(SettingsText("These actions are available while a character is open in the world.", 13));
        _settingsCharacterActions["save"] = AddButton(body, "Save character [" + KeyDisplay(_keys["save"]) + "]", Save);
        _settingsCharacterActions["load"] = AddButton(body, "Load character [" + KeyDisplay(_keys["load"]) + "]", Load);
        _settingsCharacterActions["replay"] = AddButton(body, "Verify & save replay [" + KeyDisplay(_keys["replay"]) + "]", VerifyReplay);
        BuildReleaseSettings(body);
        var arenas = SettingsStack(8); body.AddChild(arenas); _sandboxControls.Add(arenas);
        arenas.AddChild(SettingsText("Sandbox arenas · starts a fresh session", 15));
        foreach (string preset in Presets) AddButton(arenas, preset.ToUpperInvariant(), () => Reset(preset));
    }

    private void SelectSettingsPage(string page, bool playSound = true)
    {
        CancelSettingsBinding(); _settingsPage = page;
        foreach (string other in SettingsPages)
        { _settingsPages[other].Visible = other == page; _settingsTabs[other].SetPressedNoSignal(other == page); }
        _settingsDescription.Text = page switch
        {
            "Controls" => "FIND YOUR RHYTHM · movement, combat and menus",
            "Graphics" => "SHAPE THE WORLD · lighting, detail and performance",
            "Audio" => "SET THE BALANCE · independent sound channels",
            "Accessibility" => "PLAY COMFORTABLY · effects, motion and pause",
            _ => "YOUR PREFERENCES · loot and local tools"
        };
        _settingsRestore.Text = "Restore " + page + " defaults"; _settingsScroll.ScrollVertical = 0;
        ContainSettingsFocus();
        if (playSound) _settingsTabs[page].GrabFocus();
    }

    private void RestoreSettingsPage()
    {
        CancelSettingsBinding(); _syncingSettings = true;
        try
        {
            switch (_settingsPage)
            {
                case "Controls": foreach (var pair in DefaultKeys) SetKey(pair.Key, pair.Value); RefreshSettingsBindings(); break;
                case "Graphics": SetGraphicsQuality("High"); SetRenderScale(1.25f); SetDisplayMode("Windowed"); break;
                case "Audio":
                    _masterVolume = _musicVolume = _effectsVolume = _interfaceVolume = 1;
                    foreach (var slider in _volumeSliders.Values) slider.Value = 100;
                    ApplySettingsAudio(); break;
                case "Accessibility":
                    _reduceEffects = _reduceShake = false; _settingsEffects.SetPressedNoSignal(false); _settingsShake.SetPressedNoSignal(false); ApplyGraphicsQuality(); break;
                case "Gameplay":
                    _minimumLootRarity = 0; _compatibleLootOnly = false;
                    ((OptionButton)_settingsPanel.FindChild("LootRarityFilter", true, false)).Select(0);
                    ((CheckButton)_settingsPanel.FindChild("SettingsCompatibleLoot", true, false)).SetPressedNoSignal(false);
                    _lootSignature = ""; if (_view is not null) SynchronizeLootVisuals(); break;
            }
        }
        finally { _syncingSettings = false; }
        SavePreferences();
    }

    private void BeginSettingsBinding(string action)
    {
        CancelSettingsBinding(); _awaitingKey = action;
        _keyButtons[action].Text = "Press a key…";
        SettingsNotice("Choose a key for " + SettingsActionName(action) + ". Esc cancels.");
    }

    private void CancelSettingsBinding()
    {
        if (_awaitingKey is null) return;
        _awaitingKey = null; RefreshSettingsBindings();
        if (!_settingsSaveFailed) SettingsNotice("Key change cancelled.");
    }

    private void HandleSettingsInput(InputEvent input)
    {
        if (_smoke || _settingsPanel is not { Visible: true }) return;
        if (_awaitingKey is not null && input is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled(); Key chosen = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            if (chosen == Key.Escape) { CancelSettingsBinding(); return; }
            if (!CanBindSettingsKey(chosen) || (key.CtrlPressed || key.MetaPressed || key.ShiftPressed || key.AltPressed) && chosen != Key.Alt)
            { SettingsNotice("Choose a single key. Arrows, Enter and modifier combinations are reserved for navigation and mouse controls."); return; }
            string action = _awaitingKey;
            var conflict = _keys.FirstOrDefault(pair => pair.Key != action && pair.Value == chosen);
            if (conflict.Key is not null)
            { SettingsNotice(KeyDisplay(chosen) + " is already assigned to " + SettingsActionName(conflict.Key) + ". Choose another key or press Esc."); return; }
            Input.ActionRelease("aw_" + action); SetKey(action, chosen); _awaitingKey = null;
            RefreshSettingsBindings(); SavePreferences(); return;
        }
        if (input is not (InputEventKey or InputEventJoypadButton)) return;
        if (input.IsActionPressed("ui_cancel") || input.IsActionPressed("aw_settings"))
        { TogglePanel(_settingsPanel); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventKey navigation && (navigation.PhysicalKeycode is Key.Home or Key.End or Key.Pageup or Key.Pagedown ||
            navigation.Keycode is Key.Home or Key.End or Key.Pageup or Key.Pagedown)) return;
        if (!new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }

    private static bool CanBindSettingsKey(Key key) => Enum.IsDefined(key) && key is not (Key.None or Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.KpEnter or Key.Shift or Key.Ctrl or Key.Meta);
    private void RefreshSettingsBindings()
    {
        foreach (var pair in _keyButtons) pair.Value.Text = KeyDisplay(_keys[pair.Key]);
        foreach (var pair in _settingsCharacterActions)
            pair.Value.Text = (pair.Key == "replay" ? "Verify & save replay" : SettingsActionName(pair.Key)) + " [" + KeyDisplay(_keys[pair.Key]) + "]";
        _combatHudViewport = new(-1, -1);
    }
    private static string KeyDisplay(Key key) => key.ToString().Replace("Key", "", StringComparison.Ordinal).Replace("Escape", "Esc", StringComparison.Ordinal);
    private static string SettingsActionName(string action) => action switch
    {
        "left" => "Move left",
        "right" => "Move right",
        "up" => "Move forward",
        "down" => "Move backward",
        "skill1" => "Primary ability",
        "skill2" => "Secondary ability",
        "skill3" => "Ability 3",
        "skill4" => "Ability 4",
        "skill5" => "Ability 5",
        "skill6" => "Ultimate ability",
        "dodge" => "Dodge",
        "potion" => "Use potion",
        "pickup" => "Collect nearest loot",
        "target" => "Cycle target",
        "stop" => "Stop moving",
        "inventory" => "Inventory",
        "settings" => "Settings",
        "journey" => "Journey map",
        "localmap" => "Local exploration map",
        "endgame" => "Expeditions",
        "showloot" => "Show all loot (hold)",
        "interact" => "Interact",
        "character" => "Character",
        "corpse" => "Consume corpse",
        "echo" => "Cast Echo",
        "experiment" => "Echoes: Borrowed Memory",
        "pause" => "Pause / resume",
        "step" => "Single step (sandbox)",
        "reset" => "Restart arena (sandbox)",
        "save" => "Save character",
        "load" => "Load character",
        "replay" => "Verify and save replay",
        _ => action
    };
    private void SettingsNotice(string text) { if (_settingsStatus is not null) _settingsStatus.Text = text; }
    private void ApplySettingsAudio() => ClientAudio.ApplyVolumes(_masterVolume, _musicVolume, _effectsVolume, _interfaceVolume);
    private float ReadSettingsVolume(string bus) => bus switch { "Master" => _masterVolume, "Music" => _musicVolume, "Effects" => _effectsVolume, _ => _interfaceVolume };
    private void SettingsVisibilityChanged()
    {
        bool open = _settingsPanel.Visible; _settingsBackdrop.Visible = open; CancelSettingsBinding();
        if (open)
        { _hud.MoveChild(_settingsBackdrop, _hud.GetChildCount() - 1); _hud.MoveChild(_settingsPanel, _hud.GetChildCount() - 1); LayoutSettings(); ContainSettingsFocus(); }
    }
    private void ContainSettingsFocus()
    {
        static IEnumerable<Control> Focusable(Node root)
        {
            foreach (var node in root.GetChildren())
            {
                if (node is Control { FocusMode: Control.FocusModeEnum.All } control && control.IsVisibleInTree() && node is not BaseButton { Disabled: true }) yield return control;
                foreach (var child in Focusable(node)) yield return child;
            }
        }
        var controls = Focusable(_settingsPanel).ToArray();
        for (int i = 0; i < controls.Length; i++)
        {
            var control = controls[i]; var previous = controls[(i + controls.Length - 1) % controls.Length].GetPath(); var next = controls[(i + 1) % controls.Length].GetPath();
            control.FocusPrevious = control.FocusNeighborTop = control.FocusNeighborLeft = previous;
            control.FocusNext = control.FocusNeighborBottom = control.FocusNeighborRight = next;
        }
    }
    private void LayoutSettings()
    {
        if (_settingsPanel is not { Visible: true }) return;
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2I pixels = GetWindow().Size;
        float scale = GetViewport().Scaling3DScale;
        if (_settingsResolutionPixels != pixels || _settingsResolutionScale != scale)
        {
            _settingsResolutionPixels = pixels; _settingsResolutionScale = scale;
            _settingsResolution.Text = $"Window · {pixels.X} × {pixels.Y}    3D detail · {scale * 100:0}%";
        }
        if (_settingsViewport != viewport)
        {
            _settingsViewport = viewport;
            _settingsLayoutSize = new(Math.Min(1000, viewport.X - 36), Math.Min(700, viewport.Y - 36));
            _settingsLayoutOrigin = (viewport - _settingsLayoutSize) / 2;
        }
        // Wrapping can change the panel's minimum while a tab settles, even at the same viewport size.
        if (_settingsPanel.Size != _settingsLayoutSize) _settingsPanel.Size = _settingsLayoutSize;
        if (_settingsPanel.Position != _settingsLayoutOrigin) _settingsPanel.Position = _settingsLayoutOrigin;
    }
    private Button SettingsButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeStyleboxOverride("normal", SettingsBox(new("1a303e"), new("45616e"), 10));
        button.AddThemeStyleboxOverride("hover", SettingsBox(new("244654"), new("add7ca"), 10));
        button.AddThemeStyleboxOverride("pressed", SettingsBox(new("2a514d"), new("add7ca"), 10));
        button.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new("e8c286"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        button.Pressed += () => { ClientAudio.PlayInterface(this); action(); }; return button;
    }
    private static StyleBoxFlat SettingsBox(Color background, Color border, int padding) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 7,
        CornerRadiusTopRight = 7,
        CornerRadiusBottomLeft = 7,
        CornerRadiusBottomRight = 7,
        ContentMarginLeft = padding,
        ContentMarginRight = padding,
        ContentMarginTop = padding,
        ContentMarginBottom = padding
    };
    private static VBoxContainer SettingsStack(int gap) { var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", gap); return box; }
    private static Label SettingsText(string text, int size, Color? color = null)
    { var label = TextLabel(text, size); label.AddThemeColorOverride("font_color", color ?? new Color("abc6d6")); return label; }
}
