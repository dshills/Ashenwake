using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class ExpeditionSmoke
{
    private readonly Dictionary<long, (string Key, EndgameSigilDisplay Display)> _sigilDisplays = [];
    private string VisibleText => string.Join('\n', Descendants(_hud).OfType<Control>().Where(c => c.IsVisibleInTree()).Select(c => c switch
    {
        Label label => label.Text,
        Button button => button.Text,
        RichTextLabel label => label.Text,
        _ => ""
    }));

    private void Refresh()
    {
        _sandbox.AdoptSession(_session.Combat); _sandbox.SetPaused(true);
        var snapshot = _session.Capture(); var view = _session.View; var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var definition = _endgame.Capture(); var run = view.Run;
        var presentation = run is null ? null : new EndgameRunDisplay(run.Id, run.Name, run.Kind, run.Status, Region(run.Region), run.Tier,
            run.EncounterIndex + 1, run.EncounterCount, run.AttemptsRemaining, run.Deaths, run.RewardPercent, run.Rules.Select(RuleName).ToArray(), run.Counterplay,
            run.InheritedModifiers, snapshot.Manifest?.Inheritance.Where(i => !i.Selected).Select(i => $"room {i.RoomIndex + 1}: {i.Candidate} · {i.Reason}").ToArray() ?? [],
            run.EncounterCleared, run.CanAdvance, run.CanRetry, run.CanAbandon, snapshot.Manifest?.Rooms.Select(r => r.Name).ToArray());
        var hunts = definition.Hunts.Select(h => new EndgameHuntDisplay(h.Id, h.Name, h.RequiredTier, h.Secret, view.UnlockedHunts.Contains(h.Id),
            $"Requires cleared Fracture tier {h.RequiredTier}" + (h.Secret ? " and victories over all four known God Hunts." : "."), h.Phases, h.Counterplay, h.EvolutionMaterial)).ToArray();
        var reward = snapshot.Endgame.Rewards.Values.LastOrDefault();
        string summary = reward is null ? "Expedition completion commits materials, mastery and any catalyst once, together with the outcome." :
            $"Last completion: +{reward.Materials} common materials · +{reward.Mastery} mastery" + (reward.EvolutionMaterial.Length == 0 ? "." : $" · +{reward.EvolutionCount} {Readable(reward.EvolutionMaterial)}.");
        var rewardDisplay = reward is null ? null : new EndgameRewardDisplay(reward.RunId, reward.Materials, reward.Mastery, reward.EvolutionMaterial, reward.EvolutionCount);
        _hud.SetView(new(view.Unlocked, view.InHub, view.HighestClearedTier, view.Materials, view.Catalysts, view.AvailableSigils.Select(SigilDisplay).ToArray(), hunts,
            presentation, view.CanClaimRecoverySigil, AtGate(), _session.Combat.View.Loot.Count, summary, _revision, rewardDisplay, player.Health > 0, _session.Combat.View.Endgame is not null));
        _sandbox.PresentAuthoredRoom(_session.Room, _session.Combat.View.Endgame?.ContextKey ?? "expedition-hub", "default");
        _effects.Show(_session.Combat.View.Endgame, player.Position, _hud.IsOpen);
    }

    private EndgameSigilDisplay SigilDisplay(FractureSigil sigil)
    {
        string key = string.Join('|', sigil.Modifiers);
        if (_sigilDisplays.TryGetValue(sigil.Id, out var cached) && cached.Key == key) return cached.Display;
        var definition = _endgame.Capture();
        EndgameChoice Choice(FractureModifier modifier) => new(modifier.Id, modifier.Name, modifier.Counterplay);
        var modifiers = sigil.Modifiers.Select(id => Choice(definition.Modifiers.Single(m => m.Id == id))).ToArray();
        var replacements = new Dictionary<string, EndgameChoice[]>();
        foreach (string old in sigil.Modifiers)
        {
            var valid = new List<EndgameChoice>();
            foreach (var modifier in definition.Modifiers.Where(m => m.Id != old))
            {
                try { EndgameContent.ValidateSigil(_endgame, sigil with { Modifiers = sigil.Modifiers.Select(id => id == old ? modifier.Id : id).ToArray() }); valid.Add(Choice(modifier)); }
                catch (InvalidDataException) { }
            }
            replacements[old] = valid.ToArray();
        }
        var preview = _session.PreviewSigil(sigil.Id);
        var display = new EndgameSigilDisplay(sigil.Id, Region(sigil.Region), sigil.Tier, sigil.Seed, sigil.BossFamily, sigil.RewardTendency, modifiers, replacements,
            preview.EncounterNames, preview.InheritedModifiers, preview.SkippedInheritance, sigil.Region);
        _sigilDisplays[sigil.Id] = (key, display); return display;
    }

    private bool AtGate()
    {
        var gate = _session.Interactions.FirstOrDefault(i => i.ActionId == "endgame.gate");
        return _session.InHub && gate is not null && CorePosition.DistanceSquared(_session.Combat.View.Actors.Single(a => a.Id == 1).Position, gate.Position) <= (long)gate.Range * gate.Range;
    }
    private string Region(string id) => _campaign.Capture().Acts.FirstOrDefault(a => a.Id == id)?.Name ?? Readable(id);
    private string RuleName(string id) => _endgame.Capture().Modifiers.FirstOrDefault(m => m.Id == id || m.Rule == id)?.Name ?? Readable(id);
    private static string Readable(string id) => string.Join(' ', id.Split('.').Skip(1).DefaultIfEmpty(id)).Replace('_', ' ');
    private static string SafeId(string id) => new(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");

    private void CheckRunRoute(string label)
    {
        var run = _session.RunView!; var rooms = _session.Capture().Manifest!.Rooms;
        Check(label + "_authored_titles_and_progress_match_core", Enumerable.Range(0, run.EncounterCount).All(i =>
        {
            string state = run.Status == "Completed" || i < run.EncounterIndex || i == run.EncounterIndex && run.EncounterCleared ? "CLEARED" :
                i == run.EncounterIndex ? run.Status is "Failed" or "Abandoned" || run.CanRetry ? "FAILED" : "CURRENT" : "UPCOMING";
            return Find<Label>("ExpeditionRouteTitle" + i).Text == rooms[i].Name && Find<Label>("ExpeditionRouteState" + i).Text == state;
        }));
    }

    private async Task CheckLayouts(string label, string action)
    {
        string hash = _session.StateHash; int requests = _requests, frames = _session.CaptureReplay().Frames.Length;
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1280, 720), new Vector2I(780, 800) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size; await Frames(6);
            var control = Find<Button>(action); await Reveal(control);
            var viewport = GetViewport().GetVisibleRect();
            Check(label + "_layout_fits_" + size.X + "x" + size.Y, viewport.Encloses(Find<Control>("ExpeditionPanel").GetGlobalRect()) &&
                viewport.Encloses(control.GetGlobalRect()) && new[] { "Sigils", "Hunts", "Run", "Rewards" }.All(tab => viewport.Encloses(Find<Button>("ExpeditionTab" + tab).GetGlobalRect())));
            await Capture("expedition-" + label + "-layout-" + size.X + "x" + size.Y + ".png");
        }
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(6);
        Check(label + "_layout_and_render_preserve_state_and_history", _session.StateHash == hash && _requests == requests && _session.CaptureReplay().Frames.Length == frames);
    }

    private void PressKey(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
    }

    private async Task Click(string name)
    {
        var control = Find<Button>(name);
        if (control.Disabled) throw new InvalidDataException("Expedition control is disabled: " + name);
        await Reveal(control); var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }

    private async Task Reveal(Control control)
    {
        if (!control.IsVisibleInTree()) throw new InvalidDataException("Expedition control is hidden: " + control.Name);
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            for (int i = 0; i < 120 && !ContainsVertically(scroll, control); i++)
            {
                var direction = control.GetGlobalRect().Position.Y < scroll.GetGlobalRect().Position.Y ? MouseButton.WheelUp : MouseButton.WheelDown;
                foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Position = scroll.GetGlobalRect().GetCenter(), Pressed = pressed }, true);
                await Frames(1);
            }
            if (!ContainsVertically(scroll, control)) throw new InvalidDataException("Cannot reveal expedition control: " + control.Name);
        }
        if (!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect())) throw new InvalidDataException("Expedition control is outside viewport: " + control.Name);
    }

    private static bool ContainsVertically(Control parent, Control child) => child.GetGlobalRect().Position.Y >= parent.GetGlobalRect().Position.Y - 1 && child.GetGlobalRect().End.Y <= parent.GetGlobalRect().End.Y + 1;
    private T Find<T>(string name) where T : Node => Descendants(_hud).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private async Task Frames(int count = 3) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        await Frames();
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-expedition") || DisplayServer.GetName() == "headless") return;
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Expedition check failed: " + name); }
}
