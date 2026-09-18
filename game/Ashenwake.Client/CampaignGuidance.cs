using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Contextual directions derived from authoritative campaign and combat projections.</summary>
public partial class CampaignHud
{
    private Button _nextStep = null!;
    private string _nextTab = "Map";

    private void BuildNextStep()
    {
        _nextStep = new Button { Name = "CampaignNextStep", Position = new(22, 210), Size = new(565, 34), Visible = false };
        _nextStep.Pressed += () => OpenTab(_nextTab);
        AddChild(_nextStep);
    }

    private void OpenTab(string tab)
    { _tab = tab; Visible = true; _panel.Visible = true; Rebuild(true); _firstTab.GrabFocus(); }

    private bool ChoicePending => !_state.InHub && _state.Exploration is null && _content.Choices.Any(c =>
        c.Act == _state.CurrentAct && _state.CompletedEncounters.Contains(c.RequiredEncounter) && !_state.Choices.ContainsKey(c.Id));

    private void RefreshNextStep()
    {
        _nextTab = ChoicePending ? "Story" : "Map";
        _nextStep.Visible = !_state.InHub && !_engaged;
        _nextStep.Text = _state.Exploration is not null ? "Review exploration progress [J]" : ChoicePending
            ? "Choose the region's outcome [J]" : _state.CompletedActs.Contains(_state.CurrentAct)
            ? "Region complete · choose where to go next [J]" : "Area secured · review loot and continue [J]";
        if (_state.InHub)
        { _objective.Text = "Choose an unlocked act on the Journey map [J] to leave Greyhaven."; return; }
        if (_state.Exploration?.Id == "event.wake_hunt" && _interactions.FirstOrDefault(i => i.Id.StartsWith("clue.", StringComparison.Ordinal)) is { } clue)
        {
            int total = _content.Exploration.Single(e => e.Id == "event.wake_hunt").Clues.Length;
            _objective.Text = $"THE ANTLER THAT WALKS · Trace {_state.Exploration.TrackedClues + 1}/{total}: {clue.Name.Replace("Track ", "", StringComparison.Ordinal)}. Approach the marked trace and interact to follow it.";
            return;
        }
        if (_combat.Actors.Any(a => a.DefinitionId == "boss.rootheart" && a.Health > 0))
        {
            int roots = _combat.Actors.Count(a => a.DefinitionId == "enemy.feeding_root" && a.Health > 0);
            _objective.Text = roots >= 3 ? "ROOTHEART PROTECTED · Sever a feeding root to expose the moving core. Click a root or use Tab to target it."
                : $"ROOTHEART EXPOSED · Attack the moving core. {roots} feeding root{(roots == 1 ? "" : "s")} remain; keep clear of the spore warnings.";
            return;
        }
        if (_combat.Actors.Any(a => a.DefinitionId == "boss.bell_saint" && a.Health > 0) && _combat.BossPhase == 2)
        {
            int anchors = _combat.Actors.Count(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0);
            _objective.Text = anchors > 0
                ? $"BELL SAINT PROTECTED · Destroy {anchors} ritual anchor{(anchors == 1 ? "" : "s")}. Click an anchor or use Tab to change target."
                : "RITUAL BROKEN · The Bell Saint can be hurt again. Attack it to end the resurrection.";
            return;
        }
        if (_engaged) { _objective.Text = _view.Objective; return; }
        string loot = _combat.Loot.Count > 0 ? $"{_combat.Loot.Count} dropped item{(_combat.Loot.Count == 1 ? "" : "s")} remain. Approach and press E; hold Alt to reveal filtered loot. " : "";
        _objective.Text = _state.Exploration is not null ? loot + "Open the map to review clues or finish this exploration."
            : ChoicePending ? loot + "The area is secure. Open Story to settle this region's choice."
            : _state.CompletedActs.Contains(_state.CurrentAct) ? loot + "Region complete. Return to Greyhaven or choose another unlocked act."
            : loot + "Area secured. Open the Journey map and choose Continue onward.";
    }

    private void MapNextStep()
    {
        _rows.AddChild(new HSeparator());
        _rows.AddChild(Label("NEXT STEP", 14));
        string droppedLoot = _combat.Loot.Count > 0 ? $" · leave {_combat.Loot.Count} uncollected drops" : "";
        if (_state.Exploration is null)
        {
            if (ChoicePending) Button("Resolve the region's choice", () => OpenTab("Story")).Disabled = _engaged;
            else if (_view.EncounterId is not null)
                Button("Continue onward" + droppedLoot, () => ContinueRequested?.Invoke()).Disabled = _engaged;
            else
            {
                var nextAct = _content.Acts.FirstOrDefault(a => a.Number > _state.CurrentAct && _view.AvailableActs.Contains(a.Number));
                if (nextAct is not null) Button($"Travel to Act {nextAct.Number} · {nextAct.Name}" + droppedLoot, () => ActRequested?.Invoke(nextAct.Number)).Disabled = _engaged;
            }
        }
        Button("Return to Greyhaven" + droppedLoot, () => HubRequested?.Invoke());
        _rows.AddChild(Label(_engaged ? "Clear the encounter to continue. Returning to Greyhaven leaves this fight and any ground loot behind."
            : _combat.Loot.Count > 0 ? "Collect rewards before travelling. Uncollected ground loot is left behind."
            : "Your earned progression is preserved when you travel.", 12));
        _rows.AddChild(new HSeparator());
    }
}
