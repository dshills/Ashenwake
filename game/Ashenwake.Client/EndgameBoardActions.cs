using Godot;

namespace Ashenwake.Client;

public partial class EndgameHud
{
    private enum BoardAction { Fracture, Hunt, Attune, Recovery, Advance, Retry, Abandon, Hub }
    private sealed record BoardRequest(BoardAction Kind, long Sigil = 0, string Id = "", string Value = "");
    private BoardRequest? _pending;
    private long _pendingRevision = -1;
    private void BuildConfirmation()
    {
        _confirmation = new ConfirmationDialog { Name = "ExpeditionConfirmation", DialogAutowrap = true };
        AddChild(_confirmation); _confirmation.Canceled += CancelConfirmation;
        _confirmation.Confirmed += () =>
        {
            var request = _pending; long revision = _pendingRevision; CancelConfirmation();
            if (request is not null && revision == _view?.Revision && IsOpen && IsVisibleInTree() && Availability(request).Length == 0) Dispatch(request);
        };
    }
    private void CancelConfirmation()
    {
        bool pending = _pending is not null || _confirmation is { Visible: true };
        _pending = null; _pendingRevision = -1; _confirmation?.Hide();
        if (pending) ModalChanged?.Invoke(false);
    }
    private string Availability(BoardRequest request)
    {
        var view = _view;
        if (view is null || !view.Unlocked) return "Complete the campaign to unlock expeditions.";
        if (!view.Alive && request.Kind is not (BoardAction.Retry or BoardAction.Abandon or BoardAction.Hub)) return "Retry or return to Greyhaven before taking this action.";
        if (request.Kind is BoardAction.Fracture or BoardAction.Hunt or BoardAction.Attune or BoardAction.Recovery)
        {
            if (view.Run is { Status: "Active" }) return "Finish or abandon the active expedition first.";
            if (!view.InHub) return "Return to Greyhaven to prepare an expedition.";
            if (!view.AtGate) return "Approach the Fracture gate in eastern Greyhaven.";
        }
        var sigil = view.Sigils.FirstOrDefault(s => s.Id == request.Sigil);
        return request.Kind switch
        {
            BoardAction.Fracture when sigil is null => "This Sigil is no longer available.",
            BoardAction.Hunt when !view.Hunts.Any(h => h.Id == request.Id && h.Unlocked) => "Meet this hunt's unlock requirements first.",
            BoardAction.Attune when sigil is null || !sigil.Modifiers.Any(m => m.Id == request.Id) || !sigil.Replacements.GetValueOrDefault(request.Id, []).Any(m => m.Id == request.Value) => "Choose a compatible replacement rule.",
            BoardAction.Attune when view.Materials < 5 => "Attunement requires 5 common materials.",
            BoardAction.Recovery when !view.CanRecover => "Recovery is available when no unconsumed Sigils or active expedition remain.",
            BoardAction.Advance when view.Run is not { Status: "Active", CanAdvance: true } => "Clear the current encounter before continuing.",
            BoardAction.Retry when view.Run is not { Status: "Active", CanRetry: true } => "No retry is available for this encounter.",
            BoardAction.Abandon when view.Run is not { Status: "Active", CanAbandon: true } => "There is no active expedition to abandon.",
            BoardAction.Hub when view.InHub => "You are already in Greyhaven.",
            BoardAction.Hub when view.Run is { Status: "Active" } => "Finish or explicitly abandon the active expedition first.",
            _ => ""
        };
    }
    private Button RequestButton(string text, BoardRequest request, string name)
    {
        var button = ActionButton(text, () => Request(request), name);
        string reason = Availability(request); button.Disabled = reason.Length > 0; button.TooltipText = reason;
        return button;
    }
    private void Request(BoardRequest request)
    {
        string reason = Availability(request);
        if (reason.Length > 0) { Notice(reason); return; }
        CancelConfirmation();
        bool leavesArena = request.Kind == BoardAction.Hub || request.Kind == BoardAction.Advance && _view!.Run is { } run && run.Room < run.RoomCount;
        bool confirm = request.Kind is BoardAction.Fracture or BoardAction.Hunt or BoardAction.Abandon || leavesArena && _view!.GroundDrops > 0;
        if (!confirm) { Dispatch(request); return; }
        string message;
        switch (request.Kind)
        {
            case BoardAction.Fracture:
                var sigil = _view!.Sigils.Single(s => s.Id == request.Sigil);
                _confirmation.Title = "Consume this Sigil?"; _confirmation.OkButtonText = "Consume & enter";
                message = $"Enter {sigil.Region} · tier {sigil.Tier}?\n\nSigil #{sigil.Id} will be consumed. This expedition allows three attempts across {sigil.Rooms.Length} rooms. Abandoning does not return the Sigil."; break;
            case BoardAction.Hunt:
                _confirmation.Title = "Begin God Hunt?"; _confirmation.OkButtonText = "Begin hunt";
                message = $"Confront {_view!.Hunts.Single(h => h.Id == request.Id).Name}?\n\nTwo attempts remain for this new hunt. Defeat all three phases to earn its evolution catalyst."; break;
            case BoardAction.Abandon:
                _confirmation.Title = "Abandon this expedition?"; _confirmation.OkButtonText = "Abandon expedition";
                message = "End this expedition without its final reward? Earned character progress is preserved." +
                    (_view!.Run?.Kind == "Fracture" ? " The consumed Sigil will not be returned." : ""); break;
            default:
                _confirmation.Title = "Leave uncollected loot?"; _confirmation.OkButtonText = "Leave loot & continue";
                message = request.Kind == BoardAction.Hub ? "Return to Greyhaven?" : "Continue to the next encounter?"; break;
        }
        if (_view!.GroundDrops > 0) message += $"\n\n{_view.GroundDrops} uncollected ground drops will be left behind. Collected items and earned character progress are preserved.";
        _confirmation.DialogText = message; _confirmation.CancelButtonText = "Keep inspecting";
        _pending = request; _pendingRevision = _view.Revision; ModalChanged?.Invoke(true);
        _confirmation.PopupCentered(new(Math.Min(560, (int)GetViewportRect().Size.X - 44), 280));
    }
    private void Dispatch(BoardRequest request)
    {
        CancelConfirmation();
        if (request.Kind is not (BoardAction.Attune or BoardAction.Recovery)) SetOpen(false);
        switch (request.Kind)
        {
            case BoardAction.Fracture: FractureRequested?.Invoke(request.Sigil); break;
            case BoardAction.Hunt: HuntRequested?.Invoke(request.Id); break;
            case BoardAction.Attune: AttuneRequested?.Invoke(request.Sigil, request.Id, request.Value); break;
            case BoardAction.Recovery: RecoveryRequested?.Invoke(); break;
            case BoardAction.Advance: AdvanceRequested?.Invoke(); break;
            case BoardAction.Retry: RetryRequested?.Invoke(); break;
            case BoardAction.Abandon: AbandonRequested?.Invoke(); break;
            case BoardAction.Hub: HubRequested?.Invoke(); break;
        }
    }
}
