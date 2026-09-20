using Ashenwake.Core.Coop;
using Godot;

namespace Ashenwake.Client;

public partial class CoopClient
{
    private void BuildHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); layer.AddChild(hud);
        Text("A S H E N W A K E  /  CO-OP", new(30, 22), new(740, 36), 25);
        header = Text("THE BASILICA · A SHARED EXPEDITION", new(31, 69), new(820, 60), 17);
        party = Text("Waiting for your party", new(31, 139), new(510, 114), 15);
        targetDetails = Text("", new(31, 285), new(235, 245), 16);
        network = Text("", new(922, 23), new(327, 65), 12);
        var rewardPanel = Panel(new(926, 106), new(321, 303));
        var rewardColumn = new VBoxContainer(); rewardPanel.AddChild(rewardColumn);
        rewardColumn.AddChild(new Label { Text = "YOUR EXPEDITION REWARDS" });
        var scroll = new ScrollContainer { CustomMinimumSize = new(294, 246), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; rewardColumn.AddChild(scroll);
        rewards = new Label { Text = "Clear encounters together to earn personal rewards.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(280, 0) }; scroll.AddChild(rewards);
        resources = Text("", new(31, 636), new(875, 50), 17);
        for (int i = 0; i < 6; i++) { int index = i; skills[i] = Button("Ability " + (i + 1), new(31 + i * 155, 696), new(148, 55), () => Cast(index)); skills[i].AddThemeFontSizeOverride("font_size", 12); }
        ready = Button("Ready [E]", new(969, 650), new(279, 44), () => Queue(Ashenwake.Core.Coop.CoopInputAction.Ready));
        Button("Session [Esc]", new(969, 705), new(279, 44), ToggleConnection);
        Text("LEFT CLICK  Move / attack enemy  ·  SHIFT+CLICK  Primary  ·  RIGHT CLICK  Secondary  ·  WASD / STICK  Move  ·  X  Stop  ·  1–6  Skills  ·  SPACE / B  Dodge  ·  Q  Potion  ·  E  Ready", new(31, 762), new(1215, 34), 11);
        connection = Panel(new(290, 184), new(700, 439));
        var column = new VBoxContainer(); connection.AddChild(column);
        var title = new Label { Text = "JOIN YOUR PARTY" }; title.AddThemeFontSizeOverride("font_size", 24); column.AddChild(title);
        column.AddChild(new Label { Text = "Use the session details issued by the local party service. Each character has a single-use join ticket; reconnecting needs a fresh ticket.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        address = Field(column, "Session address", "ws://127.0.0.1:5180");
        allocation = Field(column, "Allocation (optional for a complete session address)", "Party allocation identifier");
        ticket = Field(column, "Join ticket · kept only for this connection", "Paste a fresh ticket"); ticket.Secret = true;
        var row = new HBoxContainer(); column.AddChild(row);
        var join = new Button { Text = "Join session", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; join.Pressed += Connect; row.AddChild(join);
        var disconnect = new Button { Text = "Disconnect", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        disconnect.Pressed += () => { local?.Transport.Dispose(); actions.Clear(); CancelMouseMovement(); connectionStatus.Text = "Disconnected. Your server-owned character can rejoin with a fresh ticket."; }; row.AddChild(disconnect);
        var close = new Button { Text = "Back to battle", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; close.Pressed += () => connection.Visible = false; row.AddChild(close);
        connectionStatus = new Label { Text = "", CustomMinimumSize = new(640, 45), AutowrapMode = TextServer.AutowrapMode.WordSmart }; column.AddChild(connectionStatus);
        column.AddChild(new Label { Text = "The shared battle continues while this panel is open. Closing the application does not reset the party's progress.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        ticket.GrabFocus();
    }
    private void RefreshHud(CoopView view)
    {
        var peer = local!;
        var own = view.Players.Single(p => p.Id == peer.Id);
        header.Text = new[] { "OSSUARY APPROACH", "CLOISTER OF LAST MERCY", "THE BELL SAINT · FIRST RINGING", "THE BELL SAINT · RITUAL ANCHORS", "THE BELL SAINT · FINAL TOLL" }[view.EncounterIndex] +
            $"  ·  {view.EncounterIndex + 1}/5  ·  Attempt {view.Attempt + 1}" + (view.Completed ? "\nEXPEDITION COMPLETE" : view.AwaitingRetry ? "\nParty defeated. Both players must ready to retry." : view.Cleared ? "\nArea cleared. Both players must ready to continue." : "");
        party.Text = string.Join('\n', view.Players.Select(p =>
        {
            var actor = view.Actors.Single(a => a.PlayerId == p.Id);
            // The coop.1 protocol supplies these fixed preset titles, not item IDs.
            // Preserve unknown server descriptions instead of guessing their equipment.
            string loadout = p.Loadout switch
            {
                "Ash axe Vanguard" => EquipmentNames.For("item.ash_axe") + " · Vanguard",
                "Oath hammer Vanguard" => EquipmentNames.For("item.oath_hammer") + " · Vanguard",
                _ => p.Loadout
            };
            return $"PLAYER {p.Id}{(p.Id == peer.Id ? " · YOU" : "")}  ·  {loadout}\nHealth {actor.Health}/{actor.MaxHealth}  ·  Barrier {actor.Barrier}  ·  {(p.Connected ? p.Ready ? "READY" : "Connected" : "Disconnected")}";
        }));
        var selected = view.Actors.FirstOrDefault(a => a.Id == target && a.Health > 0);
        string counterplay = view.Actors.Any(a => a.Shielded) ? "Destroy both ritual anchors to break the Saint's shield." :
            view.EncounterIndex == 4 && !view.Cleared ? "Break the bells. Leave the marked circles before the final toll." :
            !view.Cleared ? "Leave the marked ground before the countdown ends." : "";
        targetDetails.Text = (selected is null ? "" : $"TARGET\n{selected.DefinitionId.Split('.').Last().Replace('_', ' ').ToUpperInvariant()}\nHealth {selected.Health}/{selected.MaxHealth}" + (selected.Shielded ? " · SHIELDED" : "") + "\n\n") + counterplay;
        double age = Math.Max(0, Now - peer.ReceivedAt);
        network.Text = $"{peer.Transport.Status}\nRound trip {peer.RoundTripMs:F0} ms · snapshot age {age * 1000:F0} ms\nServer tick {view.Tick} · input {own.ProcessedSequence}/{own.AcceptedSequence}";
        resources.Text = $"MOMENTUM  {own.Momentum}/100     POTIONS  {own.PotionCharges}  {(own.PotionCooldownTicks > 0 ? $"({own.PotionCooldownTicks / 30d:F1}s)" : "[Q]")}     DODGE  {(own.DodgeCooldownTicks > 0 ? $"{own.DodgeCooldownTicks / 30d:F1}s" : "READY")}";
        for (int i = 0; i < skills.Length; i++)
        {
            var skill = own.Skills[i]; skills[i].Text = $"{i + 1}  {skill.Name}\n" + (skill.RemainingTicks > 0 ? $"{skill.RemainingTicks / 30d:F1}s" : skill.Generate > 0 ? $"+{skill.Generate} Momentum" : $"{skill.Cost} Momentum");
            skills[i].Disabled = !peer.Transport.Connected || skill.RemainingTicks > 0 || own.Momentum < skill.Cost || view.AwaitingRetry || view.Completed;
        }
        ready.Text = own.Ready ? "Ready · waiting for teammate" : view.AwaitingRetry ? "Ready to retry [E]" : view.Completed ? "Expedition completed" : "Ready for next encounter [E]";
        ready.Disabled = !peer.Transport.Connected || own.Ready || view.Completed || !view.Cleared && !view.AwaitingRetry;
        var receipts = view.Rewards.Where(r => r.PlayerId == peer.Id).ToArray();
        rewards.Text = receipts.Length == 0 ? "No rewards yet. Clear an encounter together." : string.Join("\n\n", receipts.Select(r => $"{EquipmentNames.For(r.Item)} · {r.Item.Rarity}\n+{r.Experience} XP · +{r.Ash} ash"));
    }
    private static LineEdit Field(VBoxContainer parent, string label, string placeholder)
    { parent.AddChild(new Label { Text = label }); var field = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new(0, 32), MaxLength = 8192 }; parent.AddChild(field); return field; }
    private Label Text(string text, Vector2 at, Vector2 size, int font)
    { var label = new Label { Text = text, Position = at, Size = size, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", font); hud.AddChild(label); return label; }
    private Button Button(string text, Vector2 at, Vector2 size, Action action)
    { var button = new Button { Text = text, Position = at, Size = size }; button.Pressed += action; hud.AddChild(button); return button; }
    private PanelContainer Panel(Vector2 at, Vector2 size)
    {
        var panel = new PanelContainer { Position = at, Size = size };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("101b27"), BorderColor = new Color("82978f"), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 13, ContentMarginRight = 13, ContentMarginTop = 12, ContentMarginBottom = 12 });
        hud.AddChild(panel); return panel;
    }
    private static void BindInputs()
    {
        var keys = new Dictionary<string, Key> { ["left"] = Key.A, ["right"] = Key.D, ["up"] = Key.W, ["down"] = Key.S, ["dodge"] = Key.Space, ["potion"] = Key.Q, ["ready"] = Key.E, ["target"] = Key.Tab, ["menu"] = Key.Escape, ["stop"] = Key.X };
        for (int i = 0; i < 6; i++) keys["skill" + (i + 1)] = Key.Key1 + i;
        foreach (var pair in keys)
        { string action = "coop_" + pair.Key; if (!InputMap.HasAction(action)) InputMap.AddAction(action); InputMap.ActionEraseEvents(action); InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = pair.Value }); }
        foreach (var pair in new[] { ("left", JoyAxis.LeftX, -1f), ("right", JoyAxis.LeftX, 1f), ("up", JoyAxis.LeftY, -1f), ("down", JoyAxis.LeftY, 1f) })
            InputMap.ActionAddEvent("coop_" + pair.Item1, new InputEventJoypadMotion { Axis = pair.Item2, AxisValue = pair.Item3 });
        foreach (var pair in new[] { ("skill1", JoyButton.A), ("skill2", JoyButton.X), ("skill3", JoyButton.Y), ("dodge", JoyButton.B), ("skill4", JoyButton.LeftShoulder), ("skill5", JoyButton.RightShoulder), ("skill6", JoyButton.DpadUp), ("potion", JoyButton.DpadDown), ("ready", JoyButton.DpadRight), ("target", JoyButton.RightStick), ("menu", JoyButton.Start) })
            InputMap.ActionAddEvent("coop_" + pair.Item1, new InputEventJoypadButton { ButtonIndex = pair.Item2 });
    }
}
