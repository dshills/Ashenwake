using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class MouseActionsSmoke
{
    private async Task MechanismActions()
    {
        // This deliberately starts an authored combat phase, not a fabricated unlocked
        // runtime journey. The carried character is the real Act I character earned above.
        string content = Field<string>(_director, "_combatJson");
        var catalog = EndgameCombatContent.FromComposed(content);
        var manifest = catalog.CreateHuntManifest("hunt.orrun_without_oath", 42, 1);
        var combat = catalog.CreateEncounter(manifest, 1, 0, Session.Combat.Capture(), restoreAtAnchor: true);
        RemoveChild(_director);
        _sandbox = new Sandbox { ContentJsonOverride = content, AutomaticStep = false };
        AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(combat);
        _camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
        var effects = new EndgamePresentation(); AddChild(effects); effects.AttachOverlay(_sandbox);
        _sandbox.SetMechanismVisuals(effects.GetMechanismVisual);
        _sandbox.PresentAuthoredRoom(combat.Room, combat.View.Endgame!.ContextKey, "spine_causeway");
        var recorder = new CombatRecorder(combat);
        var inputs = new List<CombatCommand>(); var events = new List<CombatEvent>();
        _sandbox.AdvanceOverride = commands =>
        {
            inputs.AddRange(commands); var batch = recorder.Step(combat, commands); events.AddRange(batch);
            effects.Show(combat.View.Endgame, combat.View.Actors.Single(a => a.Id == 1).Position, false); return batch;
        };
        effects.Show(combat.View.Endgame, combat.View.Actors.Single(a => a.Id == 1).Position, false);
        await Frames(4);
        if (_sandbox.IsPaused) await ClickLocalResume();
        Ticks(2);
        var mechanisms = combat.View.Endgame!.Mechanisms;
        var term = mechanisms.First(m => m.Kind == "BrokenTerm" && m.Available);
        var plinth = mechanisms.First(m => m.Kind == "OathPlinth");
        Check("authored_hunt_exposes_terms_and_locked_plinths", mechanisms.Count(m => m.Kind == "BrokenTerm" && m.Available) == 2 && !plinth.Available);
        var point = await MechanismPoint(term.Id, effects);
        string beforeHover = combat.StateHash;
        await Hover(point);
        Check("mechanism_hover_uses_authored_identity_without_mutation", _sandbox.HoveredWorldActionId == "mechanism:" + term.Id && combat.StateHash == beforeHover);
        await Capture("mouse-mechanism-hover.png");
        await Click(point);
        Check("distant_mechanism_starts_selected_approach", _sandbox.PendingWorldActionId == "mechanism:" + term.Id && _sandbox.ClickMoveDestination is not null && !events.Any(e => e.Kind == "HuntMechanismUsed"));
        await MechanismWalk(combat);
        Check("mouse_approach_uses_exactly_one_selected_mechanism", events.Count(e => e.Kind == "HuntMechanismUsed" && e.TargetId == term.Id) == 1 &&
            inputs.Count(c => c.Kind == CombatCommandKind.InteractMechanism && c.TargetId == term.Id) == 1);
        Check("mechanism_action_executes_inside_core_radius", CorePosition.DistanceSquared(combat.View.Actors.Single(a => a.Id == 1).Position, term.Position) <= (long)term.Radius * term.Radius);
        Check("carrying_term_changes_real_mechanism_availability", combat.View.Endgame!.Mechanisms.Where(m => m.Kind == "BrokenTerm").All(m => !m.Available) && combat.View.Endgame.Mechanisms.Any(m => m.Kind == "OathPlinth" && m.Available));
        await Hover(point);
        Check("spent_mechanism_is_no_longer_pickable", _sandbox.HoveredWorldActionId != "mechanism:" + term.Id);
        await Capture("mouse-mechanism-term-carried.png");
        point = await MechanismPoint(plinth.Id, effects); await Click(point); await KeyPress(Key.X); Ticks(4);
        Check("cancelled_plinth_approach_does_not_deposit_term", NoIntent && !events.Any(e => e.Kind == "HuntMechanismUsed" && e.TargetId == plinth.Id));
        point = await MechanismPoint(plinth.Id, effects); await Click(point); await MechanismWalk(combat);
        Check("second_selected_mechanism_deposits_carried_term", events.Count(e => e.Kind == "HuntMechanismUsed" && e.TargetId == plinth.Id) == 1 &&
            !combat.View.Endgame!.Mechanisms.Single(m => m.Id == plinth.Id).Available);
        Check("mechanism_clicks_never_cast_primary", !inputs.Any(c => c.Kind == CombatCommandKind.Cast));
        Check("mechanism_route_keeps_character_alive", combat.View.Actors.Single(a => a.Id == 1).Health > 0);
        Check("mechanism_command_replay_matches", CombatReplayRunner.Run(content, recorder.Capture()).Success);
        System.IO.File.WriteAllText(Path.Combine(_output, "mouse-mechanism-replay.json"), JsonData.Write(recorder.Capture()));
        await Capture("mouse-mechanism-deposited.png");
    }

    private async Task<Vector2> MechanismPoint(int id, EndgamePresentation effects)
    {
        var visual = effects.GetMechanismVisual(id) ?? throw new InvalidDataException("Missing authored mechanism visual.");
        foreach (var point in PickPoints(visual))
        {
            await Hover(point);
            if (_sandbox.HoveredWorldActionId == "mechanism:" + id) return point;
        }
        throw new InvalidDataException("No actual mechanism body could be picked: " + id);
    }
    private async Task MechanismWalk(CombatSession combat)
    {
        for (int i = 0; i < 500 && !NoIntent; i++)
        {
            Ticks();
            var player = combat.View.Actors.Single(a => a.Id == 1);
            if (player.Health <= 0) throw new InvalidDataException("Authored mechanism route died.");
            if (combat.Room.Obstacles.Any(o => player.Position.X >= o.MinX && player.Position.X <= o.MaxX && player.Position.Z >= o.MinZ && player.Position.Z <= o.MaxZ))
                throw new InvalidDataException("Mechanism approach entered an authoritative obstacle.");
            if (i % 24 == 0) await Frames();
        }
        if (!NoIntent) throw new InvalidDataException("Mechanism approach exceeded its bounded route.");
        Ticks(2); await Frames();
    }
    private async Task ClickLocalResume()
    {
        var resume = Descendants(_sandbox).OfType<Button>().Single(b => b.Text == "Resume playing" && b.IsVisibleInTree());
        await Click(resume.GetGlobalRect().GetCenter());
    }
}
