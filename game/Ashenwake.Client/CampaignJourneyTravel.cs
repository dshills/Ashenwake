using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class CampaignHud
{
    private enum JourneyTravelKind { Act, Continue, Hub, Exploration, LeaveExploration }
    private sealed record JourneyTravelRequest(JourneyTravelKind Kind, int Act = 0, string Id = "");
    private ConfirmationDialog _travelDialog = null!;
    private JourneyTravelRequest? _pendingTravel;
    private long _confirmationRevision = -1;
    private object? _journeySession;
    private bool _journeyPaused;
    private bool JourneyAlive => _combat is not null && _combat.Actors.Any(a => a.Id == 1 && a.Health > 0);

    private void BuildTravelConfirmation()
    {
        _travelDialog = new ConfirmationDialog { Name = "JourneyTravelConfirmation", Title = "Leave uncollected loot?", OkButtonText = "Leave loot & travel", CancelButtonText = "Stay and collect", DialogAutowrap = true };
        _travelDialog.Canceled += CancelJourneyConfirmations;
        _travelDialog.Confirmed += () =>
        {
            var request = _pendingTravel; long revision = _confirmationRevision;
            CancelJourneyConfirmations();
            if (request is not null && revision == _revision && IsVisibleInTree() && _panel.Visible && JourneyTravelReason(request).Length == 0) DispatchJourneyTravel(request);
        };
        AddChild(_travelDialog);
    }

    private string JourneyTravelReason(JourneyTravelRequest request)
    {
        if (!JourneyAlive) return "Travel is unavailable while defeated.";
        return request.Kind switch
        {
            JourneyTravelKind.Hub => _state.InHub ? "You are already in Greyhaven." : "",
            JourneyTravelKind.Act when !_view.AvailableActs.Contains(request.Act) => "This region is not unlocked yet.",
            JourneyTravelKind.Act when _engaged || _state.Exploration is not null => "Clear this encounter or return to Greyhaven before changing regions.",
            JourneyTravelKind.Continue when _state.InHub || _engaged || _state.Exploration is not null => "Complete the current encounter first.",
            JourneyTravelKind.Continue when ChoicePending => "Resolve this region's decision before its final confrontation.",
            JourneyTravelKind.Continue when _view.EncounterId is null => "This region is complete. Choose another region or return to Greyhaven.",
            JourneyTravelKind.Exploration when _state.InHub || _engaged || _state.Exploration is not null || _state.CompletedExploration.Contains(request.Id) || !_content.Exploration.Any(e => e.Id == request.Id && e.Act == _state.CurrentAct) => "This exploration is not available here now.",
            JourneyTravelKind.LeaveExploration when _state.Exploration is null => "There is no active exploration.",
            _ => ""
        };
    }

    private void RequestJourneyTravel(JourneyTravelRequest request)
    {
        string reason = JourneyTravelReason(request);
        if (reason.Length > 0) { Notice(reason); return; }
        CancelJourneyConfirmations();
        if (_combat.Loot.Count == 0) { DispatchJourneyTravel(request); return; }
        string destination = request.Kind switch
        {
            JourneyTravelKind.Act => _content.Acts.Single(a => a.Number == request.Act).Name,
            JourneyTravelKind.Hub => "Greyhaven",
            JourneyTravelKind.Continue => Readable(_view.EncounterId!),
            JourneyTravelKind.Exploration => _content.Exploration.Single(e => e.Id == request.Id).Name,
            _ => "the regional route"
        };
        _travelDialog.DialogText = $"Travel to {destination}?\n\n{_combat.Loot.Count} uncollected ground drops will be left behind. Earned items, discoveries and character progress are preserved.\n\nStay to collect your loot, or confirm departure.";
        // A direct objective action can also reach this guard; make its owning modal visible first.
        if (!_panel.Visible) OpenTab("Map");
        _pendingTravel = request; _confirmationRevision = _revision;
        _travelDialog.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 40), 240));
    }

    private void DispatchJourneyTravel(JourneyTravelRequest request)
    {
        SetOpen(false);
        switch (request.Kind)
        {
            case JourneyTravelKind.Act: ActRequested?.Invoke(request.Act); break;
            case JourneyTravelKind.Continue: ContinueRequested?.Invoke(); break;
            case JourneyTravelKind.Hub: HubRequested?.Invoke(); break;
            case JourneyTravelKind.Exploration: ExplorationRequested?.Invoke(request.Id); break;
            case JourneyTravelKind.LeaveExploration: LeaveExplorationRequested?.Invoke(); break;
        }
    }

    private void CancelJourneyConfirmations()
    {
        _pendingTravel = null; _pendingChoice = _pendingOutcome = ""; _confirmationRevision = -1;
        _travelDialog?.Hide(); _choiceDialog?.Hide();
    }

    private void ConfirmJourneyChoice()
    {
        string choice = _pendingChoice, outcome = _pendingOutcome; long revision = _confirmationRevision;
        CancelJourneyConfirmations();
        if (choice.Length == 0 || revision != _revision || !IsVisibleInTree() || !_panel.Visible || !JourneyAlive || _engaged || !ChoicePending) return;
        ChoiceRequested?.Invoke(choice, outcome);
    }

    private void SynchronizeJourneySession()
    {
        if (_anatomySandbox is null || !GodotObject.IsInstanceValid(_anatomySandbox)) return;
        if (!ReferenceEquals(_journeySession, _anatomySandbox.Session))
        { _journeySession = _anatomySandbox.Session; CancelJourneyConfirmations(); }
        bool open = IsVisibleInTree() && _panel.Visible && _tab != "Anatomy";
        if (open || _journeyPaused) _anatomySandbox.SetModalPaused("journey-panel", open);
        _journeyPaused = open;
    }

    public override void _Notification(int what)
    { if (what == NotificationApplicationFocusOut && IsInsideTree()) CancelJourneyConfirmations(); }
}
