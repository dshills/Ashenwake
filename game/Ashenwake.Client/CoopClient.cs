using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Core.Serialization;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Network peer presentation. The server owns actors, collisions, resources, combat, encounters and receipts.</summary>
public partial class CoopClient : Node3D
{
    private sealed class Peer
    {
        public CoopClientTransport Transport { get; } = new();
        public CoopView? View, Previous;
        public int Id;
        public long LastSentTick = -1, Sequence, Revision;
        public string Hash = "";
        public double ReceivedAt, LastSendAt, RoundTripMs;
        public volatile bool Sending;
        public int Accepted, Rejected;
        public readonly Dictionary<long, double> SentAt = [];
    }
    private Peer? local, companion;
    private CoopClientPresentation stage = null!;
    private Control hud = null!;
    private PanelContainer connection = null!;
    private LineEdit address = null!, allocation = null!, ticket = null!;
    private Label connectionStatus = null!, header = null!, party = null!, network = null!, resources = null!, rewards = null!, targetDetails = null!;
    private Button ready = null!;
    private readonly Button[] skills = new Button[6];
    private readonly Queue<(CoopInputAction Action, string Skill, int Target)> actions = [];
    private readonly Dictionary<long, (string? Local, string? Companion)> hashes = [];
    private readonly HashSet<int> capturedEncounters = [];
    private readonly HashSet<int> capturedMechanics = [];
    private string contentHash = "", output = "";
    private int target, matchedTicks, mismatchedTicks;
    private bool smoke, finished, foreground = true;
    private double started;
    private Vector2 movement;
    private static double Now => Time.GetTicksMsec() * .001;

    public override void _Ready()
    {
        try
        {
            smoke = OS.GetCmdlineUserArgs().Contains("--coop-smoke");
            output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://coop");
            contentHash = CombatContent.Parse(FileAccess.GetFileAsString("res://combat.json")).Identity;
            stage = new CoopClientPresentation(); AddChild(stage); BuildHud(); BindInputs();
            Input.JoyConnectionChanged += ControllerChanged;
            address.Text = Argument("--coop-server=") ?? "ws://127.0.0.1:5180";
            allocation.Text = Argument("--allocation=") ?? "";
            ticket.Text = Argument("--ticket=") ?? "";
            started = Now;
            if (ticket.Text.Length > 0) Connect();
            else if (smoke) Fail("Co-op smoke requires two valid join tickets and a session address.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        { Fail("Co-op content or connection configuration is unavailable."); }
    }
    private void Connect()
    {
        try
        {
            var uri = SessionUri(address.Text.Trim(), allocation.Text.Trim());
            string credential = ticket.Text.Trim();
            if (credential.Length is < 16 or > 8192) { connectionStatus.Text = "Paste a fresh join ticket for your character."; return; }
            local?.Transport.Dispose(); companion?.Transport.Dispose(); companion = null; actions.Clear(); hashes.Clear();
            local = new(); _ = local.Transport.Connect(uri, contentHash, credential); ticket.Text = "";
            if (smoke)
            {
                string peerTicket = Argument("--peer-ticket=") ?? "";
                if (peerTicket.Length < 16) { Fail("Co-op smoke requires a second player's join ticket."); return; }
                companion = new(); _ = companion.Transport.Connect(uri, contentHash, peerTicket);
            }
            connectionStatus.Text = "Connecting to the party…";
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        { connectionStatus.Text = "Use a ws:// or wss:// session address and a valid allocation identifier."; }
    }
    private static Uri SessionUri(string server, string id)
    {
        var uri = new Uri(server, UriKind.Absolute);
        if (uri.Scheme is not ("ws" or "wss") || uri.Query.Length > 0 || uri.UserInfo.Length > 0) throw new ArgumentException("Invalid session address.");
        if (uri.AbsolutePath.StartsWith("/v1/matches/", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith("/socket", StringComparison.Ordinal)) return uri;
        if (id.Length is < 1 or > 128 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new ArgumentException("Invalid allocation.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/v1/matches/" + id + "/socket");
    }
    public override void _Process(double delta)
    {
        if (finished) return;
        if (local is not null) Receive(local, true);
        if (companion is not null) Receive(companion, false);
        movement = foreground && !connection.Visible && !smoke ? Input.GetVector("coop_left", "coop_right", "coop_up", "coop_down", .28f) : Vector2.Zero;
        if (local?.View is { } view)
        {
            if (!view.Actors.Any(a => a.Id == target && a.PlayerId == 0 && a.Health > 0)) target = Nearest(view);
            stage.Render(view, local.Previous, local.Id, target, movement, Math.Max(0, Now - local.ReceivedAt), delta);
            RefreshHud(view);
            Send(local, true);
            if (OS.GetCmdlineUserArgs().Contains("--capture-coop") && DisplayServer.GetName() != "headless" && view.Warnings.Length > 0 && capturedEncounters.Add(view.EncounterIndex))
                CaptureEncounter(view.EncounterIndex);
            if (OS.GetCmdlineUserArgs().Contains("--capture-coop") && DisplayServer.GetName() != "headless" && view.Actors.Any(a => a.Shielded) && capturedMechanics.Add(view.EncounterIndex))
                CaptureEncounter(view.EncounterIndex, "-shield");
        }
        else connectionStatus.Text = local?.Transport.Status ?? "Create a party with the local online services, then paste its session details here.";
        if (companion is not null) Send(companion, false);
        if (smoke)
        {
            if (Now - started > 600) Fail("Co-op smoke exceeded its ten-minute input-only route.");
            else if (local?.View?.Completed == true && companion?.View?.Completed == true && matchedTicks > 20) CompleteSmoke();
        }
    }
    private void Receive(Peer peer, bool isLocal)
    {
        while (peer.Transport.TryRead(out var frame) && frame is not null)
        {
            if (frame.View is not { } view || view.ContentHash != contentHash || frame.PlayerId is < 1 or > 2) { Fail("The session uses incompatible content."); return; }
            peer.Id = frame.PlayerId; peer.Revision = frame.Revision; peer.Hash = frame.StateHash;
            if (frame.Kind == "joined" && isLocal) connection.Visible = false;
            if (peer.View is null || view.Tick > peer.View.Tick || frame.Kind == "joined")
            { peer.Previous = peer.View; peer.View = view; peer.ReceivedAt = Now; }
            else if (frame.Kind == "snapshot") peer.ReceivedAt = Now;
            if (frame.InputResult is { } receipt)
            {
                if (receipt.Accepted) peer.Accepted++; else peer.Rejected++;
                if (peer.SentAt.Remove(receipt.Sequence, out double sent)) peer.RoundTripMs = (Now - sent) * 1000;
            }
            peer.Sequence = Math.Max(peer.Sequence, view.Players.Single(p => p.Id == peer.Id).AcceptedSequence);
            if (smoke && frame.Kind == "snapshot" && view.Players.All(p => p.Connected))
            {
                hashes.TryGetValue(view.Tick, out var pair);
                pair = isLocal ? (frame.StateHash, pair.Companion) : (pair.Local, frame.StateHash);
                if (pair.Local is not null && pair.Companion is not null)
                { if (pair.Local == pair.Companion) matchedTicks++; else mismatchedTicks++; hashes.Remove(view.Tick); }
                else hashes[view.Tick] = pair;
                while (hashes.Count > 128) hashes.Remove(hashes.Keys.Min());
            }
        }
    }
    private void Send(Peer peer, bool isLocal)
    {
        if (peer.View is not { } view || !peer.Transport.Connected || peer.Sending || Now - peer.LastSendAt < 1d / 35) return;
        bool heartbeat = view.Tick <= peer.LastSentTick;
        if (heartbeat && Now - peer.LastSendAt < 5) return;
        CoopInput command;
        long sequence = Math.Max(peer.Sequence, view.Players.Single(p => p.Id == peer.Id).AcceptedSequence) + 1;
        if (heartbeat) command = new(sequence, view.Tick);
        else if (smoke) command = CoopSmoke.Input(view, peer.Id, sequence);
        else
        {
            var action = actions.Count > 0 && isLocal ? actions.Dequeue() : (CoopInputAction.None, "", 0);
            command = new(sequence, view.Tick, Math.Abs(movement.X) < .28f ? 0 : Math.Sign(movement.X), Math.Abs(movement.Y) < .28f ? 0 : Math.Sign(movement.Y), action.Item1, action.Item2, action.Item3);
        }
        peer.Sequence = sequence; peer.LastSentTick = view.Tick; peer.LastSendAt = Now;
        if (peer.SentAt.Count >= 128) peer.SentAt.Remove(peer.SentAt.Keys.Min()); peer.SentAt[sequence] = Now;
        peer.Sending = true; _ = SendAsync(peer, command);
    }
    private static async Task SendAsync(Peer peer, CoopInput command)
    { try { await peer.Transport.Send(command); } finally { peer.Sending = false; } }
    private void Queue(CoopInputAction action, string skill = "", int targetId = 0)
    { if (actions.Count < 8 && local?.Transport.Connected == true) actions.Enqueue((action, skill, targetId)); }
    private void Cast(int index)
    {
        if (local?.View is not { } view) return;
        var player = view.Players.Single(p => p.Id == local.Id);
        Queue(CoopInputAction.Cast, player.Skills[index].Id, target);
    }
    private int Nearest(CoopView view)
    {
        var self = view.Actors.FirstOrDefault(a => a.PlayerId == local!.Id);
        return self is null ? 0 : view.Actors.Where(a => a.PlayerId == 0 && a.Health > 0)
            .OrderBy(a => Ashenwake.Core.Simulation.Position.DistanceSquared(a.Position, self.Position)).FirstOrDefault()?.Id ?? 0;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (smoke || finished) return;
        if (input.IsActionPressed("coop_menu")) { connection.Visible = !connection.Visible; actions.Clear(); GetViewport().SetInputAsHandled(); return; }
        if (connection.Visible || local?.View is not { } view) return;
        for (int i = 0; i < 6; i++) if (input.IsActionPressed("coop_skill" + (i + 1))) Cast(i);
        if (input.IsActionPressed("coop_dodge")) Queue(CoopInputAction.Dodge);
        if (input.IsActionPressed("coop_potion")) Queue(CoopInputAction.Potion);
        if (input.IsActionPressed("coop_ready")) Queue(CoopInputAction.Ready);
        if (input.IsActionPressed("coop_target"))
        {
            var ids = view.Actors.Where(a => a.PlayerId == 0 && a.Health > 0).Select(a => a.Id).ToArray();
            if (ids.Length > 0) target = ids[(Array.IndexOf(ids, target) + 1) % ids.Length];
        }
        if (input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp) stage.Zoom(mouse.ButtonIndex == MouseButton.WheelUp ? -2 : 2);
            else if (mouse.ButtonIndex is MouseButton.Left or MouseButton.Right) { int selected = stage.TargetAt(mouse.Position, view); if (selected > 0) target = selected; Cast(mouse.ButtonIndex == MouseButton.Left ? 0 : 1); }
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) { foreground = false; actions.Clear(); movement = Vector2.Zero; }
        else if (what == NotificationApplicationFocusIn) foreground = true;
    }
    private void ControllerChanged(long device, bool connected)
    { if (!connected) { actions.Clear(); movement = Vector2.Zero; connectionStatus.Text = "Controller disconnected. The shared battle continues; use the keyboard or reconnect."; } }
    public override void _ExitTree()
    { Input.JoyConnectionChanged -= ControllerChanged; local?.Transport.Dispose(); companion?.Transport.Dispose(); }
    private async void CompleteSmoke()
    {
        if (finished) return; finished = true;
        var view = local!.View!; var other = companion!.View!;
        bool success = mismatchedTicks == 0 && local.Id != companion.Id && view.Rewards.Count(r => r.PlayerId == local.Id) >= 5 && other.Rewards.Count(r => r.PlayerId == companion.Id) >= 5;
        var report = new
        {
            kind = success ? "CoopClientSmokePassed" : "CoopClientSmokeFailed",
            passed = success,
            tick = view.Tick,
            playerId = local.Id,
            peerPlayerId = companion.Id,
            matchedSnapshots = matchedTicks,
            mismatchedSnapshots = mismatchedTicks,
            serverStateHash = local.Hash,
            rewards = view.Rewards,
            acceptedInputs = local.Accepted + companion.Accepted,
            rejectedInputs = local.Rejected + companion.Rejected,
            receivedFrames = local.Transport.ReceivedFrames + companion.Transport.ReceivedFrames,
            contentHash,
            note = "Two actual authenticated WebSocket peers; authoritative shared combat and personal rewards. No credentials recorded."
        };
        AtomicFile.Write(Path.Combine(output, "coop-client-report.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (OS.GetCmdlineUserArgs().Contains("--capture-coop") && DisplayServer.GetName() != "headless")
        { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "coop-complete.png")); }
        GetTree().Quit(success ? 0 : 1);
    }
    private async void CaptureEncounter(int encounter, string suffix = "")
    {
        Directory.CreateDirectory(output);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (local?.View is not { } view || view.EncounterIndex != encounter) return;
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"coop-encounter-{encounter + 1}{suffix}.png"));
        AtomicFile.Write(Path.Combine(output, $"coop-encounter-{encounter + 1}{suffix}.frame.json"), JsonData.Write(view));
    }
    private void Fail(string reason)
    { if (smoke) { finished = true; GD.PushError(reason); GetTree().Quit(1); } else if (connectionStatus is not null) { connectionStatus.Text = reason; connection.Visible = true; } }
    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
