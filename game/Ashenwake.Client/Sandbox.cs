using System.Diagnostics;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Input and presentation adapter. Every gameplay change goes through CombatSession commands.</summary>
public partial class Sandbox : Node3D
{
    private sealed record Preferences(bool ReducedEffects, bool ReducedShake, Dictionary<string, long> Keys, int MinimumLootRarity = 0, bool CompatibleLootOnly = false);
    public CombatSession Session => _session;
    public string CombatContentJson => _contentJson;
    public string? ContentJsonOverride { get; set; }
    public event Action<IReadOnlyList<CombatEvent>>? CombatAdvanced;
    public Func<CombatCommand[], IReadOnlyList<CombatEvent>>? AdvanceOverride { get; set; }
    public Func<CombatSession>? SessionOverride { get; set; }
    public Action? SaveOverride { get; set; }
    public Action? LoadOverride { get; set; }
    public Action? ReplayOverride { get; set; }
    public Action? InventoryOverride { get; set; }
    public bool AutomaticStep { get; set; }
    private bool _campaignMode;
    private readonly List<Control> _sandboxControls = [];
    private CombatSession _session = null!;
    private CombatContent _content = null!;
    private CombatView _view = null!;
    private string _contentJson = "", _output = "", _preferencesPath = "";
    private CombatRecorder _recorder = null!;
    private readonly List<CombatCommand> _pending = [];
    private readonly List<CombatEvent> _eventLog = [];
    private readonly List<double> _tickCosts = [], _frameCosts = [], _frameIntervals = [];
    private readonly Queue<string> _messages = new();
    private readonly Dictionary<string, Key> _keys = [];
    private readonly Dictionary<string, Button> _keyButtons = [];
    private FixedStepClock _clock = new();
    private Control _hud = null!;
    private PanelContainer _inventoryPanel = null!, _settingsPanel = null!;
    private VBoxContainer _inventoryRows = null!;
    private readonly Dictionary<AnatomySlot, OptionButton> _fragmentChoices = [];
    private OptionButton _mutation = null!;
    private Label _healthText = null!, _momentumText = null!, _stateText = null!, _log = null!, _metrics = null!, _comparison = null!;
    private Label _subtitleLabel = null!;
    private ProgressBar _health = null!, _momentum = null!;
    private readonly List<Button> _skillButtons = [];
    private int _target, _moveX, _moveZ, _frames, _initialInventoryCount;
    private long _lastTickBytes;
    private double _lastTickCost;
    private bool _smoke, _rebuildingUi;
    private string? _capturePath, _awaitingKey;
    private string _inventorySignature = "";
    private const int ReplayLimit = 3600;
    private static readonly string[] Presets = ["standard", "dense", "projectiles", "summons", "chain"];

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--sandbox-smoke");
            _output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://sandbox");
            _capturePath = Argument("--capture=");
            _preferencesPath = ResolveReleasePreferencesPath();
            _contentJson = ContentJsonOverride ?? FileAccess.GetFileAsString("res://combat.json");
            _content = CombatContent.Parse(_contentJson);
            BindInputs(); LoadPreferences();
            BuildArena(_content.Room.HalfWidth, _content.Room.HalfDepth);
            foreach (var obstacle in _content.Room.Obstacles) AddObstacle(obstacle.MinX, obstacle.MinZ, obstacle.MaxX, obstacle.MaxZ);
            BuildHud();
            SetSession(CombatSession.Create(_contentJson, 42, Argument("--preset=") ?? "standard"));
            InitializeReleaseSupport();
            Message("Vanguard · build Momentum with Cleave, spend it to control the pack.");
            Message("Enemy circles signal windup. Move away or dodge before the hit.");
        }
        catch (Exception ex) { Fail(ex); }
    }

    public void AddOverlay(Control overlay) => _hud.AddChild(overlay);
    public void EnableCampaign()
    {
        _campaignMode = true;
        foreach (var control in _sandboxControls) control.Visible = false;
        _stateText.Visible = false; _log.Visible = false; _metrics.Visible = false;
        _subtitleLabel.Text = "GREYHAVEN  /  BASILICA OF LAST MERCY";
    }
    public void Notify(string text) => Message(text);
    public void SetWorldSubtitle(string text) => _subtitleLabel.Text = text;
    public void SetPaused(bool paused) => SetModalPaused("session", paused);
    private void ShowInventory()
    { if (InventoryOverride is not null) InventoryOverride(); else TogglePanel(_inventoryPanel); }

    public void SetSession(CombatSession session)
    {
        _session = session; _clock = new(); _pending.Clear();
        _view = session.View; _recorder = new(session); _eventLog.Clear();
        var player = session.Capture().Actors.Single(a => a.Id == 1);
        _moveX = player.MoveX; _moveZ = player.MoveZ; _target = 0;
        _initialInventoryCount = _view.Inventory.Count; _inventorySignature = "";
        ClearPresentation(); SynchronizeWorld();
        if (_inventoryPanel is not null)
        { _inventoryPanel.Visible = false; _settingsPanel.Visible = false; if (_lootPanel is not null) _lootPanel.Visible = false; _lootSignature = ""; _inspectedLoot = 0; RefreshHud(); }
        if (_releaseEnabled) ResetPause();
    }

    /// <summary>Adopts authoritative projections without restarting input or visual feedback in the same arena.</summary>
    public void AdoptSession(CombatSession session)
    {
        AdoptSessionCore(session); _view = session.View; SynchronizeWorld(); RefreshHud();
    }
    private void AdoptSessionCore(CombatSession session)
    {
        bool changedArena = CrossedCombatBoundary(_session, session);
        _session = session; _recorder = new(session);
        if (changedArena)
        { _pending.Clear(); ClearPresentation(); _target = 0; _moveX = _moveZ = int.MinValue; }
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        try
        {
            var watch = Stopwatch.GetTimestamp();
            _clock.Advance(_smoke || AutomaticStep ? FixedStepClock.SecondsPerTick * 5 : delta, StepCombat);
            bool showAllLootHeld = Input.IsActionPressed("aw_showloot");
            if (showAllLootHeld != _showAllLootHeld)
            { _showAllLootHeld = showAllLootHeld; SynchronizeLootVisuals(); _lootSignature = ""; }
            AnimatePresentation(delta, _clock.Alpha, _target); UpdateEnvironmentAtmosphere(delta); UpdateNavigationNotice(delta); RefreshHud(); RefreshLootInspector();
            _frames++; Sample(_frameCosts, Stopwatch.GetElapsedTime(watch).TotalMilliseconds);
            if (_frames > 10) Sample(_frameIntervals, delta * 1000);
            if (_capturePath is not null && _frames == 30 && DisplayServer.GetName() != "headless")
                Callable.From(() => GetViewport().GetTexture().GetImage().SavePng(_capturePath)).CallDeferred();
            if (_smoke && _session.Tick > 120 && !_view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) && _view.Loot.Count == 0)
                CompleteSmoke();
            else if (_smoke && (_session.Tick > 2400 || _view.Actors.Single(a => a.Id == 1).Health <= 0))
                throw new InvalidDataException("Sandbox smoke failed to survive and clear its encounter.");
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void _Input(InputEvent input)
    {
        if (!_smoke && _clickMove?.Destination is not null && (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } ||
            NavigationInterruptActions.Any(a => input.IsActionPressed(a))))
            CancelMouseMovement(true);
        if (_smoke || _awaitingKey is null || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        GetViewport().SetInputAsHandled();
        try
        {
            if (key.PhysicalKeycode != Key.Escape)
            {
                var binding = _awaitingKey;
                if (_keys.Any(p => p.Key != binding && p.Value == key.PhysicalKeycode))
                { Message("That key already has an action. Choose an unused key, or Esc to cancel."); return; }
                SetKey(binding, key.PhysicalKeycode); SavePreferences();
                _keyButtons[binding].Text = $"{binding}: {key.PhysicalKeycode}";
                Message($"{binding} rebound to {key.PhysicalKeycode}.");
            }
            _awaitingKey = null;
        }
        catch (Exception ex) { Message(ex.Message); GD.PushWarning(ex.Message); }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_smoke || _session is null) return;
        try
        {
            if (input.IsActionPressed("aw_inventory")) { ShowInventory(); return; }
            if (input.IsActionPressed("aw_settings")) { TogglePanel(_settingsPanel); return; }
            if (input.IsActionPressed("aw_pause")) { ToggleManualPause(); GetViewport().SetInputAsHandled(); return; }
            if (input.IsActionPressed("aw_step")) { _clock.SingleStep(StepCombat); return; }
            if (input.IsActionPressed("aw_save")) { Save(); return; }
            if (input.IsActionPressed("aw_load")) { Load(); return; }
            if (input.IsActionPressed("aw_replay")) { VerifyReplay(); return; }
            if (input.IsActionPressed("aw_reset")) { Reset(_view.Preset); return; }
            if (input.IsActionPressed("aw_target")) { CycleTarget(); return; }
            if (!_clock.Paused && _view.Skills.Any(s => s.Mutation == "mutation.orruns_patience") &&
                (input.IsActionReleased("aw_skill2") || input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }))
            { Enqueue(new(CombatCommandKind.ReleaseCharge)); return; }
            if (input is InputEventMouseButton { Pressed: true } mouse)
            {
                if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                { _camera.Size = Math.Clamp(_camera.Size + (mouse.ButtonIndex == MouseButton.WheelUp ? -2 : 2), 18, 55); return; }
                if (!_clock.Paused && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
                {
                    bool enemy = SelectAt(mouse.Position);
                    if (mouse.ButtonIndex == MouseButton.Left && !mouse.ShiftPressed && !enemy) BeginMouseMovement(mouse.Position);
                    else Cast(mouse.ButtonIndex == MouseButton.Left ? 0 : 1);
                    GetViewport().SetInputAsHandled(); return;
                }
            }
            if (_clock.Paused) return;
            for (int i = 0; i < 6; i++) if (input.IsActionPressed($"aw_skill{i + 1}")) { Cast(i); return; }
            if (input.IsActionPressed("aw_dodge"))
            {
                int x = _moveX is >= -1 and <= 1 ? _moveX : 0, z = _moveZ is >= -1 and <= 1 ? _moveZ : 0;
                CancelMouseMovement(true);
                Enqueue(new(CombatCommandKind.Dodge, X: x == 0 && z == 0 ? 1 : x, Z: z));
            }
            if (input.IsActionPressed("aw_potion")) Enqueue(new(CombatCommandKind.Potion));
            if (input.IsActionPressed("aw_pickup")) PickupNearest();
            if (input.IsActionPressed("aw_corpse"))
            {
                var player = _view.Actors.Single(a => a.Id == 1);
                var corpse = _view.Actors.Where(a => a.Health == 0 && a.Faction == CombatFaction.Enemy && !a.CorpseConsumed)
                    .OrderBy(a => DistanceSquared(a.Position, player.Position)).ThenBy(a => a.Id).FirstOrDefault();
                if (corpse is not null) Enqueue(new(CombatCommandKind.ConsumeCorpse, TargetId: corpse.Id));
            }
            if (input.IsActionPressed("aw_echo")) Enqueue(new(CombatCommandKind.CastEcho, TargetId: _target));
            if (input.IsActionPressed("aw_stop")) { CancelMouseMovement(false); Enqueue(new(CombatCommandKind.Stop)); _moveX = _moveZ = 0; }
        }
        catch (Exception ex) { Message(ex.Message); GD.PushWarning(ex.Message); }
    }

    private void StepCombat()
    {
        if (_recorder.FrameCount >= ReplayLimit) _recorder = new(_session);
        if (_smoke) ScriptSmoke();
        else if (!AutomaticStep && !_clock.Paused) UpdateMovementInput();
        var commands = _pending.ToArray(); _pending.Clear();
        var advancedSession = _session;
        long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.GetTimestamp();
        var events = AdvanceOverride is null ? _recorder.Step(_session, commands) : AdvanceOverride(commands);
        if (SessionOverride is not null && !ReferenceEquals(_session, SessionOverride()))
        {
            AdoptSessionCore(SessionOverride());
        }
        _lastTickCost = Stopwatch.GetElapsedTime(timer).TotalMilliseconds;
        _lastTickBytes = GC.GetAllocatedBytesForCurrentThread() - before; Sample(_tickCosts, _lastTickCost);
        PresentCombatEventsCore(events, CrossedCombatBoundary(advancedSession, _session));
        CombatAdvanced?.Invoke(events);
    }

    private void SynchronizeWorld()
    {
        RefreshAppearance();
        _mechanicLabels.Clear();
        var actorIds = _view.Actors.Select(a => a.Id).ToHashSet();
        foreach (var id in _actors.Keys.Where(id => !actorIds.Contains(id)).ToArray())
        { _actors[id].Root.QueueFree(); _actors.Remove(id); }
        foreach (var actor in _view.Actors)
        {
            string role = actor.Role.Replace(" Elite", "", StringComparison.Ordinal);
            string name = actor.Faction == CombatFaction.Ally ? "Serath spirit" : role switch
            {
                "Melee" => "Ash Ghoul",
                "Ranged" => "Cinder Acolyte",
                "Armored" => "Iron Penitent",
                "Support" => "Ritual Cantor",
                "Rusher" => "Chain Hound",
                "BellSaint" => "Bell Saint",
                "Anchor" => "Ritual Anchor",
                "Bell" => "Ringing Fragment",
                "Beast" => "Unbound Creature",
                _ => actor.Role
            };
            if (actor.Role.EndsWith(" Elite", StringComparison.Ordinal)) name += " Elite";
            if (actor.Faction == CombatFaction.Enemy && actor.DefinitionId.Length > 0)
                name = Readable(actor.DefinitionId[(actor.DefinitionId.IndexOf('.') + 1)..]) + (actor.Role.EndsWith(" Elite", StringComparison.Ordinal) ? " Elite" : "");
            var hazard = (_view.CampaignHazards ?? []).Where(h => h.SourceId == actor.Id && h.RemainingTicks > 0 && !h.ContentId.StartsWith("rule.", StringComparison.Ordinal))
                .OrderBy(h => h.RemainingTicks).FirstOrDefault();
            var conditions = actor.Statuses.Select(s => s.Id).Concat(actor.EliteModifiers ?? []);
            string? mechanic = CampaignActorLabel(actor) ?? EndgameActorLabel(actor.State);
            if (mechanic is not null) _mechanicLabels.Add(actor.Id);
            if (mechanic is not null) conditions = conditions.Append(mechanic);
            if (hazard is not null) conditions = conditions.Append("ATTACK INCOMING");
            else if (mechanic is null && (actor.State is "Guarded" or "Recover")) conditions = conditions.Append(actor.State == "Guarded" ? "GUARDED" : "RECOVERY WINDOW");
            SynchronizeActor(actor.Id, name, role.ToLowerInvariant(), actor.Position.X, actor.Position.Z,
                actor.Health, actor.MaxHealth, string.Join(" / ", conditions),
                actor.TelegraphTicks > 0 && actor.TelegraphRadius == 0, actor.Faction != CombatFaction.Enemy,
                actor.DefinitionId, actor.State, actor.TelegraphTicks > 0 || hazard is not null || actor.State.EndsWith("Windup", StringComparison.Ordinal));
            _actors[actor.Id].AuthoredVisible = actor.Visible;
            _actors[actor.Id].Root.Visible &= actor.Visible;
            var aim = actor.TelegraphPosition ?? (hazard is null ? null : hazard.Kind == "Circle" ? hazard.Position : hazard.End);
            var presentation = _actors[actor.Id];
            if (aim is { } destination) presentation.Facing = PositionOf(destination.X, destination.Z) - presentation.Current;
            else if (presentation.Body.ActiveCue is not ("attack" or "dodge")) presentation.Facing = null;
            if (actor.Id != 1) _actors[actor.Id].Body.SetAccent(actor.State == "MarkedEcho" ? new Color("ffe297") : actor.State == "FalseEcho" ? new Color("69818f") : _actors[actor.Id].Body.BaseAccentColor);
        }
        BeginEffects();
        foreach (var actor in _view.Actors.Where(a => a.Health > 0 && a.Visible && a.State is "MarkedEcho" or "FalseEcho"))
            PresentEffect($"identity{actor.Id}", actor.Position.X, actor.Position.Z, .75f, actor.State == "MarkedEcho" ? new Color(1, .85f, .3f, .4f) : new Color(.4f, .6f, .7f, .15f));
        if (_view.Discipline == "Gravecaller" || _manifestations.Contains("manifestation.voracious_renewal"))
            foreach (var corpse in _view.Actors.Where(a => a.Health == 0 && a.Faction == CombatFaction.Enemy && !a.CorpseConsumed))
                PresentEffect($"corpse{corpse.Id}", corpse.Position.X, corpse.Position.Z, .45f, new Color(.6f, .4f, .8f, .6f));
        if (_view.Illusions is not null)
            for (int i = 0; i < _view.Illusions.Count; i++)
                PresentEffect($"illusion{i}", _view.Illusions[i].X, _view.Illusions[i].Z, .3f, new Color(.65f, .55f, .8f, .3f), silhouette: true);
        foreach (var actor in _view.Actors.Where(a => a.Health > 0 && a.TelegraphTicks > 0 && a.TelegraphRadius > 0 && a.TelegraphPosition is not null))
            PresentEffect($"t{actor.Id}", actor.TelegraphPosition!.Value.X, actor.TelegraphPosition.Value.Z, actor.TelegraphRadius * .001f, new Color(1, .18f, .12f, .35f));
        if (_view.CampaignHazards is not null)
            foreach (var hazard in _view.CampaignHazards)
                PresentCampaignWarning(hazard.Id, hazard.Kind, hazard.Position.X, hazard.Position.Z, hazard.End.X, hazard.End.Z, hazard.Radius, hazard.RemainingTicks, hazard.ContentId);
        foreach (var projectile in _view.Projectiles)
            PresentEffect($"p{projectile.Id}", projectile.Position.X, projectile.Position.Z, .15f, new("ffc178"), true);
        foreach (var area in _view.Areas)
            PresentEffect($"a{area.Id}", area.Position.X, area.Position.Z, area.Radius * .001f, new Color(1, .38f, .13f, .24f));
        SynchronizeLootVisuals();
        EndEffects();
        if (!_view.Actors.Any(a => a.Id == _target && a.Health > 0 && a.Visible)) _target = NearestEnemy()?.Id ?? 0;
    }

    private void Enqueue(CombatCommand command) => _pending.Add(command);
    private void PickupNearest()
    {
        var player = _view.Actors.Single(a => a.Id == 1);
        var loot = _view.Loot.Where(IsLootVisible).OrderBy(l => DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
        if (loot is not null) Enqueue(new(CombatCommandKind.Pickup, ItemId: loot.Id));
        else Message(_view.Loot.Count > 0 ? "Loot is hidden by your filter. Hold the Show loot key or inspect ground loot in Settings." : "No loot nearby.");
    }
    private void Cast(int index)
    {
        CancelMouseMovement(true);
        if (index < _view.Skills.Count)
        { if (_target == 0) _target = NearestEnemy()?.Id ?? 0; Enqueue(new(CombatCommandKind.Cast, SkillId: _view.Skills[index].Id, TargetId: _target)); }
    }
    private CombatActorView? NearestEnemy() => _view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible)
        .OrderBy(a => DistanceSquared(a.Position, _view.Actors.Single(p => p.Id == 1).Position)).ThenBy(a => a.Id).FirstOrDefault();
    private void CycleTarget()
    {
        var ids = _view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible).Select(a => a.Id).Order().ToArray();
        if (ids.Length > 0) _target = ids[(Array.IndexOf(ids, _target) + 1) % ids.Length];
    }
    private bool SelectAt(Vector2 mouse)
    {
        var target = _view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible)
            .Where(a => _actors.TryGetValue(a.Id, out var visual) && MouseHitsBody(visual, mouse))
            .Select(a => (Actor: a, Distance: _camera.UnprojectPosition(PositionOf(a.Position.X, a.Position.Z) + Vector3.Up).DistanceTo(mouse)))
            .OrderBy(p => p.Distance).ThenBy(p => p.Actor.Id).FirstOrDefault();
        if (target.Actor is not null) _target = target.Actor.Id;
        return target.Actor is not null;
    }
    private static long DistanceSquared(Position a, Position b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Z - b.Z) * (a.Z - b.Z);
    private static string Readable(string id) => id.Replace("fragment.", "").Replace("skill.", "").Replace("item.", "").Replace('_', ' ').Replace('.', ' ');
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    private static void Sample(List<double> samples, double value) { samples.Add(value); if (samples.Count > 3600) samples.RemoveAt(0); }
    private static double P95(List<double> samples) => samples.Count == 0 ? 0 : samples.Order().ElementAt(Math.Min(samples.Count - 1, (int)(samples.Count * .95)));

    private void BuildHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; layer.AddChild(_hud);
        _hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        LabelAt("A S H E N W A K E", new(30, 22), 27, new("f0dfc4"));
        _subtitleLabel = LabelAt("VANGUARD  /  THE PROVING GROUND", new(32, 62), 13, new("9eb1c0"));
        _stateText = LabelAt("", new(32, 94), 15, _ember);
        _log = LabelAt("", new(32, 143), 12, new("d7d9d6"));
        _metrics = LabelAt("", new(921, 24), 12, new("afc4d1"));
        _healthText = LabelAt("", new(32, 641), 16, _mint);
        _health = Bar(new(32, 669), new(290, 9), _mint);
        _momentumText = LabelAt("", new(350, 641), 16, _ember);
        _momentum = Bar(new(350, 669), new(260, 9), _ember);
        ButtonAt("Inventory [I]", new(916, 650), new(152, 30), ShowInventory);
        ButtonAt("Settings [Esc]", new(1081, 650), new(166, 30), () => TogglePanel(_settingsPanel));
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            var button = ButtonAt($"{i + 1}", new(32 + i * 156, 697), new(148, 54), () => { if (!_clock.Paused) Cast(index); });
            button.AddThemeFontSizeOverride("font_size", 13); _skillButtons.Add(button);
        }
        _navigationNotice = LabelAt("", new(32, 615), 13, new("ecd4ac"));
        LabelAt("CLICK ground / WASD  Move · CLICK foe  Attack · SHIFT+CLICK  Stand & attack · RIGHT CLICK  Secondary · X Stop · 1–6 Skills · SPACE Dodge · Q Potion · E Loot", new(32, 765), 11, new("abc0cb"));
        _sandboxControls.Add(ButtonAt("Reset [R]", new(985, 697), new(122, 54), () => Reset(_view.Preset)));
        ButtonAt("Pause [P]", new(1118, 697), new(129, 54), ToggleManualPause);
        BuildInventory(); BuildSettings();
    }
    private Label LabelAt(string text, Vector2 position, int size, Color color, Node? parent = null)
    {
        var label = new Label { Text = text, Position = position, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        (parent ?? _hud).AddChild(label); return label;
    }
    private Button ButtonAt(string text, Vector2 position, Vector2 size, Action action)
    {
        var button = new Button { Text = text, Position = position, Size = size };
        button.Pressed += action; _hud.AddChild(button); return button;
    }
    private ProgressBar Bar(Vector2 position, Vector2 size, Color color)
    {
        var bar = new ProgressBar { Position = position, Size = size, ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeFontSizeOverride("font_size", 1);
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color("243540") });
        _hud.AddChild(bar); return bar;
    }
    private PanelContainer Panel(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer { Position = position, Size = size, Visible = false };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.045f, .068f, .093f, .98f),
            BorderColor = new Color("637c88"),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12
        });
        _hud.AddChild(panel); return panel;
    }
    private static Label TextLabel(string text, int size = 14)
    { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Button AddButton(VBoxContainer parent, string text, Action action)
    { var button = new Button { Text = text }; button.Pressed += action; parent.AddChild(button); return button; }

    private void BuildInventory()
    {
        _inventoryPanel = Panel(new(841, 105), new(407, 518));
        var scroll = new ScrollContainer { CustomMinimumSize = new(371, 490) }; _inventoryPanel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(column);
        column.AddChild(TextLabel("INVENTORY & DIVINE ANATOMY", 18));
        column.AddChild(TextLabel("Time pauses while inspecting. Changes apply through Core.", 12));
        column.AddChild(TextLabel("EQUIPMENT · select a roll to compare", 14));
        _inventoryRows = new VBoxContainer(); column.AddChild(_inventoryRows);
        _comparison = TextLabel("", 12); column.AddChild(_comparison);
        var anatomy = new VBoxContainer(); column.AddChild(anatomy); _sandboxControls.Add(anatomy);
        anatomy.AddChild(new HSeparator()); anatomy.AddChild(TextLabel("ANATOMY · six compatible implant slots", 14));
        foreach (var slot in Enum.GetValues<AnatomySlot>())
        {
            anatomy.AddChild(TextLabel(slot.ToString(), 12));
            var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            choice.ItemSelected += index =>
            {
                if (_rebuildingUi) return;
                string id = choice.GetItemMetadata((int)index).AsString();
                if (id.Length == 0)
                {
                    var equipped = _view.Fragments.FirstOrDefault(f => f.Slot == slot && f.Equipped);
                    if (equipped is not null) ApplyWhilePaused(new(CombatCommandKind.UnequipFragment, ContentId: equipped.Id));
                }
                else ApplyWhilePaused(new(CombatCommandKind.EquipFragment, ContentId: id));
            };
            _fragmentChoices[slot] = choice; anatomy.AddChild(choice);
        }
        column.AddChild(new HSeparator()); column.AddChild(TextLabel("SHIELD BREAKER MUTATION", 14));
        _mutation = new OptionButton(); _mutation.ItemSelected += index =>
        {
            if (!_rebuildingUi) ApplyWhilePaused(new(CombatCommandKind.SetMutation, SkillId: "skill.shield_breaker", ContentId: _mutation.GetItemMetadata((int)index).AsString()));
        };
        column.AddChild(_mutation);
        _sandboxControls.Add(AddButton(column, "Build: Ember chorus · fire / spirits / poison", () => SetBuild(true)));
        _sandboxControls.Add(AddButton(column, "Build: Iron resolve · no fragments / defense", () => SetBuild(false)));
        AddButton(column, "Close inventory", () => TogglePanel(_inventoryPanel));
    }

    private void BuildSettings()
    {
        _settingsPanel = Panel(new(841, 105), new(407, 518));
        var scroll = new ScrollContainer { CustomMinimumSize = new(371, 490) }; _settingsPanel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(column);
        column.AddChild(TextLabel("SETTINGS & ARENA", 18));
        var effects = new CheckButton { Text = "Reduced visual effects", ButtonPressed = _reduceEffects };
        effects.Toggled += value => { _reduceEffects = value; SavePreferences(); }; column.AddChild(effects);
        var shake = new CheckButton { Text = "Reduced camera shake", ButtonPressed = _reduceShake };
        shake.Toggled += value => { _reduceShake = value; SavePreferences(); }; column.AddChild(shake);
        column.AddChild(TextLabel("MOUSE MOVEMENT", 14));
        column.AddChild(TextLabel("Left-click ground to walk around obstacles. Click an enemy to attack; Shift-click attacks while standing. Right-click uses your secondary skill. WASD / left stick takes over; X stops. Menus and pause cancel the destination.", 12));
        BuildLootSettings(column);
        var arenas = new VBoxContainer(); column.AddChild(arenas); _sandboxControls.Add(arenas);
        arenas.AddChild(TextLabel("Choose an arena (starts a fresh session)", 13));
        foreach (var preset in Presets) AddButton(arenas, preset.ToUpperInvariant(), () => Reset(preset));
        AddButton(column, "Save character [F5]", Save); AddButton(column, "Load character [F9]", Load);
        AddButton(column, "Verify & save replay [F6]", VerifyReplay);
        BuildReleaseSettings(column);
        column.AddChild(TextLabel("KEY BINDINGS · select, then press a key", 14));
        foreach (var pair in _keys)
        {
            string action = pair.Key;
            _keyButtons[action] = AddButton(column, $"{action}: {pair.Value}", () =>
            { _awaitingKey = action; Message($"Press a key for {action}, or Esc to cancel."); });
        }
        AddButton(column, "Close settings", () => TogglePanel(_settingsPanel));
    }

    private void TogglePanel(PanelContainer panel)
    {
        bool open = !panel.Visible; _inventoryPanel.Visible = false; _settingsPanel.Visible = false;
        if (_lootPanel is not null) _lootPanel.Visible = false;
        panel.Visible = open; ChangePause(open);
        if (open) FocusFirstAction(panel); else FocusResumeOrRelease();
        RefreshHud();
    }
    private void ApplyWhilePaused(CombatCommand command)
    { Enqueue(command); if (_clock.Paused) _clock.SingleStep(StepCombat); RefreshHud(); }
    private void SetBuild(bool fragments)
    {
        foreach (var fragment in _view.Fragments)
            Enqueue(new(fragments ? CombatCommandKind.EquipFragment : CombatCommandKind.UnequipFragment, ContentId: fragment.Id));
        var mutation = _view.Mutations.FirstOrDefault(m => m.Name.Contains(fragments ? "Avalanche" : "No Ground", StringComparison.OrdinalIgnoreCase));
        if (mutation is not null) Enqueue(new(CombatCommandKind.SetMutation, SkillId: mutation.SkillId, ContentId: mutation.Id));
        if (_clock.Paused) _clock.SingleStep(StepCombat);
        Message(fragments ? "Ember chorus: critical ignition → death spirits → summon poison." : "Iron resolve: fragment-free defense with No Ground Given.");
    }
    private void Reset(string preset)
    {
        if (_campaignMode) { Message("Return to Greyhaven to begin another expedition."); return; }
        SetSession(CombatSession.Create(_contentJson, 42, preset)); Message($"Arena reset · {preset} · seed 42.");
    }

    private void RefreshHud()
    {
        var player = _view.Actors.Single(a => a.Id == 1);
        _healthText.Text = $"HEALTH  {player.Health}/{player.MaxHealth}    BARRIER {_view.Barrier}";
        _health.Value = player.Health * 100d / player.MaxHealth;
        _momentumText.Text = $"{_view.ResourceName.ToUpperInvariant()}  {_view.Resource}/{_view.MaxResource}"; _momentum.Value = _view.Resource * 100d / _view.MaxResource;
        int enemies = _view.Actors.Count(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        _stateText.Text = player.Health <= 0 ? "YOU HAVE FALLEN · R to reset the arena" : enemies == 0 ? "ENCOUNTER CLEARED · collect the spoils with E" :
            $"{_view.Preset.ToUpperInvariant()} · {enemies} hostiles · {(_clock.Paused ? "PAUSED" : "LIVE")} · target {_target}";
        _metrics.Text = $"30 HZ CORE  /  TICK {_view.Tick}\nTick {_lastTickCost:F3} ms · {_lastTickBytes:N0} B\n" +
            $"Actors {_view.Actors.Count(a => a.Health > 0)} · shots {_view.Projectiles.Count} · areas {_view.Areas.Count}\n" +
            $"Summons {_view.Actors.Count(a => a.Faction == CombatFaction.Ally && a.Health > 0)} · statuses {_view.Actors.Sum(a => a.Statuses.Count)}\n" +
            $"Effects {_view.PendingEffects} · peak {_view.PeakEffects} · cap hits {_view.RejectedEffects}\n" +
            $"GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} · lost ticks {_clock.DroppedTicks}\n" +
            $"Potion {_view.PotionCharges} ({_view.PotionCooldownTicks / 30d:F1}s) · dodge {_view.DodgeCooldownTicks / 30d:F1}s\nResonance {_view.Resonance} · {_view.ContentVersion}";
        for (int i = 0; i < _view.Skills.Count && i < _skillButtons.Count; i++)
        {
            var skill = _view.Skills[i];
            bool insufficient = skill.ResourceMode == "Heat" ? _view.Resource + skill.Cost > _view.MaxResource : skill.Cost > _view.Resource;
            string state = !skill.Available ? "Locked" : skill.RemainingTicks > 0 ? $"{skill.RemainingTicks / 30d:F1}s" :
                skill.ResourceMode == "Heat" ? $"+{skill.Cost} {_view.ResourceName}" : skill.Cost > 0 ? $"{skill.Cost} {_view.ResourceName}" : $"+{skill.Generate} {_view.ResourceName}";
            _skillButtons[i].Text = $"{_keys[$"skill{i + 1}"].ToString().Replace("Key", "", StringComparison.Ordinal)}  {skill.Name}\n{state}";
            _skillButtons[i].Disabled = !skill.Available;
            _skillButtons[i].Modulate = skill.RemainingTicks > 0 || insufficient ? new Color(.65f, .7f, .75f) : Colors.White;
            _skillButtons[i].TooltipText = skill.Mutation == "mutation.orruns_patience" ? "Hold 2 / right click to charge; release to strike." : $"{skill.Shape} · {skill.Mutation}";
        }
        if (_inventoryPanel.Visible) RefreshInventory();
    }

    private void RefreshInventory()
    {
        string signature = JsonData.Hash(new { _view.Inventory, _view.Equipment, _view.Fragments, Mutations = _view.Skills.Select(s => s.Mutation).ToArray() });
        if (signature == _inventorySignature) return; _inventorySignature = signature;
        _rebuildingUi = true;
        foreach (var child in _inventoryRows.GetChildren()) { _inventoryRows.RemoveChild(child); child.QueueFree(); }
        foreach (var item in _view.Inventory)
        {
            bool equipped = _view.Equipment.TryGetValue(item.Slot, out long selected) && selected == item.Id;
            var button = AddButton(_inventoryRows, $"{(equipped ? "◆ " : "")}#{item.Id} {item.Name} · {item.Rarity}", () =>
            { CompareItem(item); ApplyWhilePaused(new(CombatCommandKind.Equip, ItemId: item.Id)); });
            button.AddThemeFontSizeOverride("font_size", 12);
            button.MouseEntered += () => CompareItem(item);
            button.FocusEntered += () => CompareItem(item);
        }
        foreach (var pair in _fragmentChoices)
        {
            pair.Value.Clear(); pair.Value.AddItem("Empty"); pair.Value.SetItemMetadata(0, ""); int selected = 0;
            foreach (var fragment in _view.Fragments.Where(f => f.Slot == pair.Key))
            {
                int index = pair.Value.ItemCount; pair.Value.AddItem($"{fragment.Name} · {fragment.Resonance} R");
                pair.Value.SetItemMetadata(index, fragment.Id); pair.Value.SetItemTooltip(index, $"{fragment.Lineage}\n{fragment.Description}");
                if (fragment.Equipped) selected = index;
            }
            pair.Value.Select(selected);
        }
        _mutation.Clear(); _mutation.AddItem("Unmutated"); _mutation.SetItemMetadata(0, "");
        var chosen = _view.Skills.First(s => s.Id == "skill.shield_breaker").Mutation; int selection = 0;
        foreach (var mutation in _view.Mutations.Where(m => m.SkillId == "skill.shield_breaker"))
        {
            int index = _mutation.ItemCount; _mutation.AddItem(mutation.Name); _mutation.SetItemMetadata(index, mutation.Id);
            _mutation.SetItemTooltip(index, mutation.Description); if (chosen == mutation.Id) selection = index;
        }
        _mutation.Select(selection); _rebuildingUi = false;
    }
    private void CompareItem(CombatItem item)
    {
        var current = _view.Equipment.TryGetValue(item.Slot, out long id) ? _view.Inventory.FirstOrDefault(i => i.Id == id) : null;
        _comparison.Text = $"{item.Slot} · {item.Name} · click to equip\n" +
            $"Damage {item.Damage} ({item.Damage - (current?.Damage ?? 0):+0;-0;0})  Armor {item.Armor} ({item.Armor - (current?.Armor ?? 0):+0;-0;0})\n" +
            $"Critical {item.CriticalBasisPoints / 100d:F1}% ({(item.CriticalBasisPoints - (current?.CriticalBasisPoints ?? 0)) / 100d:+0.0;-0.0;0.0}%)";
    }

    private void BindInputs()
    {
        var defaults = new Dictionary<string, Key>
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
            ["endgame"] = Key.B,
            ["showloot"] = Key.Alt,
            ["interact"] = Key.F,
            ["character"] = Key.C,
            ["corpse"] = Key.V,
            ["echo"] = Key.G,
            ["pause"] = Key.P,
            ["step"] = Key.Period,
            ["reset"] = Key.R,
            ["save"] = Key.F5,
            ["load"] = Key.F9,
            ["replay"] = Key.F6
        };
        foreach (var pair in defaults) SetKey(pair.Key, pair.Value);
        foreach (var pair in new[] { ("left", JoyAxis.LeftX, -1f), ("right", JoyAxis.LeftX, 1f), ("up", JoyAxis.LeftY, -1f), ("down", JoyAxis.LeftY, 1f) })
            InputMap.ActionAddEvent("aw_" + pair.Item1, new InputEventJoypadMotion { Axis = pair.Item2, AxisValue = pair.Item3 });
        foreach (var pair in new[] { ("skill1", JoyButton.A), ("skill2", JoyButton.X), ("skill3", JoyButton.Y), ("dodge", JoyButton.B),
            ("skill4", JoyButton.LeftShoulder), ("skill5", JoyButton.RightShoulder), ("skill6", JoyButton.DpadUp), ("potion", JoyButton.DpadDown),
            ("pickup", JoyButton.DpadLeft), ("journey", JoyButton.DpadRight), ("interact", JoyButton.LeftStick),
            ("target", JoyButton.RightStick), ("inventory", JoyButton.Back), ("pause", JoyButton.Start) })
            InputMap.ActionAddEvent("aw_" + pair.Item1, new InputEventJoypadButton { ButtonIndex = pair.Item2 });
    }
    private void SetKey(string action, Key key)
    {
        string name = "aw_" + action;
        if (!InputMap.HasAction(name)) InputMap.AddAction(name);
        foreach (var input in InputMap.ActionGetEvents(name).OfType<InputEventKey>()) InputMap.ActionEraseEvent(name, input);
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = key }); _keys[action] = key;
    }
    private void LoadPreferences() => LoadReleasePreferences();
    private void SavePreferences() => SaveReleasePreferences();

    private void Save()
    {
        if (SaveOverride is not null) { SaveOverride(); return; }
        var snapshot = _session.Capture(); string path = Path.Combine(_output, "sandbox.save.json");
        CombatSaveStore.Write(path, _contentJson, snapshot);
        Message("Saved inventory, mutations, fragments, enemies, effects and independent RNG streams.");
    }
    private void Load()
    {
        if (LoadOverride is not null) { LoadOverride(); return; }
        var restored = CombatSaveStore.Load(Path.Combine(_output, "sandbox.save.json"), _contentJson);
        SetSession(CombatSession.Restore(_contentJson, restored.State));
        Message(restored.RecoveredBackup ? "Recovered the backup sandbox save." : "Sandbox save restored.");
    }
    private void VerifyReplay()
    {
        if (ReplayOverride is not null) { ReplayOverride(); return; }
        var replay = _recorder.Capture(); var result = CombatReplayRunner.Run(_contentJson, replay);
        if (!result.Success) throw new InvalidDataException($"Sandbox replay diverged at tick {result.DivergentTick}: {result.Detail}");
        AtomicWrite(Path.Combine(_output, "session.awc"), JsonData.Write(replay));
        Message($"Replay verified · {replay.Frames.Length} ticks · state and event hashes agree.");
    }
    private static void AtomicWrite(string path, string data) => AtomicFile.Write(path, data);

    private void ScriptSmoke()
    {
        var player = _view.Actors.Single(a => a.Id == 1); var enemy = NearestEnemy();
        if (player.Health < player.MaxHealth / 2 && _view.PotionCharges > 0 && _view.PotionCooldownTicks == 0) Enqueue(new(CombatCommandKind.Potion));
        if (enemy is null)
        {
            var loot = _view.Loot.OrderBy(l => DistanceSquared(l.Position, player.Position)).FirstOrDefault();
            if (loot is not null)
            {
                Enqueue(new(CombatCommandKind.Move, X: Math.Sign(loot.Position.X - player.Position.X), Z: Math.Sign(loot.Position.Z - player.Position.Z)));
                Enqueue(new(CombatCommandKind.Pickup, ItemId: loot.Id));
            }
            return;
        }
        _target = enemy.Id;
        bool close = DistanceSquared(player.Position, enemy.Position) < 1900L * 1900;
        Enqueue(new(CombatCommandKind.Move, X: close ? 0 : Math.Sign(enemy.Position.X - player.Position.X), Z: close ? 0 : Math.Sign(enemy.Position.Z - player.Position.Z)));
        if (enemy.TelegraphTicks is > 0 and <= 5 && close && _view.DodgeCooldownTicks == 0)
            Enqueue(new(CombatCommandKind.Dodge, X: Math.Sign(player.Position.X - enemy.Position.X), Z: Math.Sign(player.Position.Z - enemy.Position.Z)));
        var skill = _view.Skills.Where(s => s.RemainingTicks == 0 && s.Cost <= _view.Momentum)
            .OrderBy(s => s.Id == "skill.iron_guard" && player.Barrier == 0 ? -1 : s.Id == "skill.cataclysm" ? 0 : s.Id == "skill.seismic_wave" ? 1 : s.Id == "skill.shield_breaker" ? 2 : s.Id == "skill.cleave" ? 3 : 4).FirstOrDefault();
        if (skill is not null) Enqueue(new(CombatCommandKind.Cast, SkillId: skill.Id, TargetId: enemy.Id));
        var nearestLoot = _view.Loot.OrderBy(l => DistanceSquared(l.Position, player.Position)).FirstOrDefault();
        if (nearestLoot is not null) Enqueue(new(CombatCommandKind.Pickup, ItemId: nearestLoot.Id));
    }
    private void CompleteSmoke()
    {
        VerifyReplay(); Save();
        var restored = CombatSaveStore.Load(Path.Combine(_output, "sandbox.save.json"), _contentJson);
        if (JsonData.Hash(restored.State) != _session.StateHash || _view.Inventory.Count <= _initialInventoryCount || !_eventLog.Any(e => e.Kind == "EntityKilled") || !_eventLog.Any(e => e.Kind == "DamageApplied"))
            throw new InvalidDataException("Sandbox smoke did not verify combat, loot, save and replay.");
        var report = new
        {
            kind = "SandboxSmokePassed",
            tick = _session.Tick,
            stateHash = _session.StateHash,
            contentVersion = _view.ContentVersion,
            preset = _view.Preset,
            display = DisplayServer.GetName(),
            kills = _eventLog.Count(e => e.Kind == "EntityKilled"),
            inventory = _view.Inventory.Count,
            fragmentTriggers = _eventLog.Count(e => e.Kind == "FragmentTriggered"),
            tickP95Ms = P95(_tickCosts),
            clientProcessP95Ms = P95(_frameCosts),
            frameIntervalP95Ms = P95(_frameIntervals),
            peakEffects = _view.PeakEffects,
            rejectedEffects = _view.RejectedEffects,
            managedMemoryBytes = GC.GetTotalMemory(false),
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            note = "Scripted client integration; headless/frame samples are not a qualitative playtest or hardware certification."
        };
        AtomicWrite(Path.Combine(_output, "report.json"), JsonData.Write(report));
        AtomicWrite(Path.Combine(_output, "sandbox-events.jsonl"), string.Join('\n', _eventLog.Select(JsonData.Write)));
        GD.Print(JsonData.Write(report)); GetTree().Quit(); SetProcess(false);
    }
    private void Message(string message)
    {
        if (_messages.LastOrDefault() == message) return;
        _messages.Enqueue(message); while (_messages.Count > 6) _messages.Dequeue();
        if (_log is not null) _log.Text = string.Join('\n', _messages);
    }
    private void Fail(Exception ex)
    {
        if (_smoke && _session is not null)
        {
            AtomicWrite(Path.Combine(_output, "failed-snapshot.json"), JsonData.Write(_session.Capture()));
            AtomicWrite(Path.Combine(_output, "failed-events.jsonl"), string.Join('\n', _eventLog.Select(JsonData.Write)));
        }
        GD.PushError(ex.ToString()); GetTree().Quit(1); SetProcess(false);
    }
}
