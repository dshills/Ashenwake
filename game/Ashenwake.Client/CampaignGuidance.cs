using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Contextual directions derived from authoritative campaign and combat projections.</summary>
public partial class CampaignHud
{
    private enum NextAction { Map, Story, Continue, Travel, FinishExploration, Interaction, Hub, Anatomy }
    private Button _nextStep = null!;
    private string _nextTab = "Map", _nextInteraction = "";
    private int _nextAct;
    private NextAction _nextAction;
    public string NextStepLabel => _nextStep?.Text ?? "";
    public bool CanRequestNextStep => _view is not null && !_engaged && _nextStep is { Visible: true } &&
        _combat.Actors.Any(actor => actor.Id == 1 && actor.Health > 0);
    public void RequestNextStep() { if (CanRequestNextStep) ActivateNextStep(); }

    private void BuildNextStep()
    {
        _nextStep = new Button
        {
            Name = "CampaignNextStep",
            CustomMinimumSize = new(0, 34),
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = "Your next action. Journey map [J] lists all destinations and options."
        };
        _nextStep.AddThemeFontSizeOverride("font_size", 14);
        _nextStep.Pressed += ActivateNextStep;
        _objectiveRows.AddChild(_nextStep);
    }

    private void OpenTab(string tab)
    { CancelJourneyConfirmations(); _tab = tab; Visible = true; _panel.Visible = true; Rebuild(true); FocusCurrentTab(); }

    private void FocusCurrentTab() => _tabButtons.GetValueOrDefault(_tab, _firstTab).GrabFocus();

    private void RequestInteraction(string id)
    { SetOpen(false); InteractionRequested?.Invoke(id); }

    private void ActivateNextStep()
    {
        if (_engaged) return;
        // Re-evaluate from the latest projection before dispatching. A remaining drop always keeps travel behind its explicit map label.
        RefreshNextStep();
        switch (_nextAction)
        {
            case NextAction.Story: OpenTab("Story"); break;
            case NextAction.Continue: RequestJourneyTravel(new(JourneyTravelKind.Continue)); break;
            case NextAction.Travel: RequestJourneyTravel(new(JourneyTravelKind.Act, _nextAct)); break;
            case NextAction.FinishExploration: RequestJourneyTravel(new(JourneyTravelKind.LeaveExploration)); break;
            case NextAction.Interaction: RequestInteraction(_nextInteraction); break;
            case NextAction.Hub: RequestJourneyTravel(new(JourneyTravelKind.Hub)); break;
            case NextAction.Anatomy: OpenAnatomyReward(); break;
            default: OpenTab("Map"); break;
        }
    }

    private bool ChoicePending => !_state.InHub && _state.Exploration is null && _content.Choices.Any(c =>
        c.Act == _state.CurrentAct && _state.CompletedEncounters.Contains(c.RequiredEncounter) && !_state.Choices.ContainsKey(c.Id));

    private void SetNextStep(NextAction action, string text)
    { _nextAction = action; _nextStep.Text = text; }

    private CampaignAct? NextAvailableAct() => _content.Acts.FirstOrDefault(a =>
        _view.AvailableActs.Contains(a.Number) && !_state.CompletedActs.Contains(a.Number));

    private void RefreshNextStep()
    {
        _nextTab = ChoicePending ? "Story" : "Map";
        _nextStep.Visible = !_engaged;
        SetNextStep(NextAction.Map, "Open the Journey map");
        if (_state.InHub)
        {
            if (FirstHeartAvailable)
            {
                _objective.Text = "The Heart of Serath is yours. Visit Mara to inspect and implant your first boss reward.";
                _nextInteraction = "service.mara";
                bool near = _interactions.Any(i => i.Id == _nextInteraction && i.Distance <= i.Range);
                SetNextStep(near ? NextAction.Anatomy : NextAction.Interaction, near ? "Inspect the Heart of Serath" : "Bring the Heart of Serath to Mara");
            }
            else if (NextAvailableAct() is { } act)
            {
                _nextAct = act.Number;
                _objective.Text = _maraDialogue.Length > 0 && !_state.CompletedActs.Contains(1)
                    ? "Follow Mara's lead to Last Mercy. The Bell Saint guards what she needs."
                    : $"Continue your journey in {act.Name}.";
                SetNextStep(NextAction.Travel, $"Travel to Act {act.Number} · {act.Name}");
            }
            else
            {
                _objective.Text = "The Breach is stable. Greyhaven remembers your choices.";
                SetNextStep(NextAction.Story, "Read the ending");
            }
            return;
        }
        if (_state.Exploration?.Id == "event.wake_hunt" && _interactions.FirstOrDefault(i => i.Id.StartsWith("clue.", StringComparison.Ordinal)) is { } clue)
        {
            int total = _content.Exploration.Single(e => e.Id == "event.wake_hunt").Clues.Length;
            _objective.Text = $"THE ANTLER THAT WALKS · Trace {_state.Exploration.TrackedClues + 1}/{total}: {clue.Name.Replace("Track ", "", StringComparison.Ordinal)}. Follow the marked trace.";
            _nextInteraction = clue.Id;
            SetNextStep(NextAction.Interaction, clue.Distance <= clue.Range ? clue.Name : "Walk to " + clue.Name);
            return;
        }
        if (_combat.Actors.Where(a => a.DefinitionId == "boss.rootheart").OrderBy(a => a.Id).FirstOrDefault() is { Health: > 0 } rootheart)
        {
            int roots = _combat.Actors.Count(a => a.DefinitionId == "enemy.feeding_root" && a.Health > 0);
            _objective.Text = rootheart.Shielded ? "ROOTHEART PROTECTED · Sever a feeding root to expose the moving core. Click a root or use Tab to target it."
                : $"ROOTHEART EXPOSED · Attack the moving core. {roots} feeding root{(roots == 1 ? "" : "s")} remain; keep clear of the spore warnings.";
            return;
        }
        if (_combat.Actors.Where(a => a.DefinitionId == "boss.bell_saint").OrderBy(a => a.Id).FirstOrDefault() is { Health: > 0 } bellSaint && _combat.BossPhase == 2)
        {
            int anchors = _combat.Actors.Count(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0);
            _objective.Text = bellSaint.Shielded
                ? $"BELL SAINT PROTECTED · Destroy {anchors} ritual anchor{(anchors == 1 ? "" : "s")}. Click an anchor or use Tab to change target."
                : "RITUAL BROKEN · The Bell Saint can be hurt again. Attack it to end the resurrection.";
            return;
        }
        if (_engaged) { _objective.Text = _view.Objective; return; }
        string loot = _combat.Loot.Count > 0 ? $"{_combat.Loot.Count} dropped item{(_combat.Loot.Count == 1 ? "" : "s")} remain. " : "";
        if (_state.Exploration is not null)
        {
            _objective.Text = loot + "Exploration complete. Collect your rewards and return to the region.";
            SetNextStep(_combat.Loot.Count > 0 ? NextAction.Map : NextAction.FinishExploration,
                _combat.Loot.Count > 0 ? "Review rewards & finish exploration" : "Finish exploration");
        }
        else if (ChoicePending)
        {
            _objective.Text = loot + "Area secured. Decide the region's future.";
            SetNextStep(NextAction.Story, "Choose the region's outcome");
        }
        else if (_view.Ending is not null)
        {
            _objective.Text = loot + "The Breach is stable. Return to the people of Greyhaven.";
            SetNextStep(_combat.Loot.Count > 0 ? NextAction.Map : NextAction.Hub,
                _combat.Loot.Count > 0 ? "Review rewards & return to Greyhaven" : "Return to Greyhaven");
        }
        else if (_state.CompletedActs.Contains(_state.CurrentAct))
        {
            _objective.Text = loot + (FirstHeartAvailable ? "Region complete. Heart of Serath secured. Inspect it and visit Mara for your first boss implant." : "Region complete. A new route is open.");
            if (FirstHeartAvailable)
                SetNextStep(_combat.Loot.Count > 0 ? NextAction.Map : NextAction.Anatomy, _combat.Loot.Count > 0 ? "Review rewards & inspect the heart" : "Inspect the Heart of Serath");
            else if (_combat.Loot.Count == 0 && NextAvailableAct() is { } act)
            { _nextAct = act.Number; SetNextStep(NextAction.Travel, $"Travel to Act {act.Number} · {act.Name}"); }
            else SetNextStep(NextAction.Map, "Review rewards & choose the next region");
        }
        else
        {
            _objective.Text = loot + "Area secured. Next: " + Readable(_view.EncounterId ?? "encounter.the road ahead") + ".";
            SetNextStep(_combat.Loot.Count > 0 ? NextAction.Map : NextAction.Continue,
                _combat.Loot.Count > 0 ? "Review rewards & continue" : "Continue onward");
        }
    }

    private void MapNextStep()
    {
        _rows.AddChild(new HSeparator());
        _rows.AddChild(Label("NEXT STEP", 14));
        string droppedLoot = _combat.Loot.Count > 0 ? $" · leave {_combat.Loot.Count} uncollected drops" : "";
        if (_combat.Loot.Count > 0)
            _rows.AddChild(Label("Click a dropped item to walk over and collect it. E picks up nearby loot; hold Alt to reveal filtered drops. Travel leaves uncollected drops behind.", 12));
        if (_state.Exploration is null)
        {
            if (ChoicePending) Button("Resolve the region's choice", () => OpenTab("Story")).Disabled = _engaged;
            else if (_view.EncounterId is not null)
                Button("Continue onward" + droppedLoot, () => RequestJourneyTravel(new(JourneyTravelKind.Continue))).Disabled = _engaged;
            else
            {
                var nextAct = _content.Acts.FirstOrDefault(a => a.Number > _state.CurrentAct && _view.AvailableActs.Contains(a.Number));
                if (nextAct is not null) Button($"Travel to Act {nextAct.Number} · {nextAct.Name}" + droppedLoot, () => RequestJourneyTravel(new(JourneyTravelKind.Act, nextAct.Number))).Disabled = _engaged;
            }
        }
        Button("Return to Greyhaven" + droppedLoot, () => RequestJourneyTravel(new(JourneyTravelKind.Hub)));
        if (_engaged) _rows.AddChild(Label("Clear the encounter to continue. Returning to Greyhaven leaves this fight and any ground loot behind.", 12));
        else if (_combat.Loot.Count == 0) _rows.AddChild(Label("Your earned progression is preserved when you travel.", 12));
        _rows.AddChild(new HSeparator());
    }
}
