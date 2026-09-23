using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private RegionalHuntBoard _huntBoard = null!;
    private RegionalHuntPresentation _huntPresentation = null!;
    private Button _huntNavigation = null!;
    private PanelContainer _huntObjective = null!;
    private Label _huntObjectiveText = null!;
    private string _huntNotice = "", _shownHuntOutcome = "";

    private void InitializeRegionalHunts()
    {
        _sandbox.EnemyNameOverride = id => _training is null && _session.InSecretChamber && _session.CurrentSecretChamber is { } guardian && id == guardian.PrimaryEnemyId ? guardian.GuardianName : _training is null && _session.InRegionalHunt && _session.CurrentRegionalHunt is { } quarry && id == quarry.PrimaryEnemyId ? quarry.Name : null;
        _huntPresentation = new RegionalHuntPresentation(); AddChild(_huntPresentation);
        _huntBoard = new RegionalHuntBoard(); _sandbox.AddOverlay(_huntBoard);
        _huntNavigation = new Button { Name = "RegionalHuntNavigation", Text = "Regional hunts", TooltipText = "Inspect the three regional contracts at Greyhaven's hunt board." };
        _huntNavigation.Pressed += OpenRegionalHunts; _sandbox.AddOverlay(_huntNavigation); CombatHudLayout.Navigation(_huntNavigation, 4);
        _huntObjective = new PanelContainer { Name = "RegionalHuntObjective", MouseFilter = Control.MouseFilterEnum.Ignore };
        _huntObjective.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new("17212ce8"), BorderColor = new("ae9368"), BorderWidthLeft = 3, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 });
        _huntObjectiveText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        _huntObjectiveText.AddThemeFontSizeOverride("font_size", 14); _huntObjective.AddChild(_huntObjectiveText); _sandbox.AddOverlay(_huntObjective);
        _huntBoard.HuntRequested += id => RunRegionalAction(new(EndgameRuntimeAction.StartRegionalHunt, Id: id));
        _huntBoard.AdvanceRequested += () =>
        {
            var clue = _session.Interactions.FirstOrDefault(i => i.ActionId.StartsWith("hunt.regional.", StringComparison.Ordinal));
            if (clue is null) return;
            _huntBoard.SetOpen(false); _sandbox.RequestWorldInteraction(clue.ActionId);
        };
        _huntBoard.ClaimRequested += () => RunRegionalAction(new(EndgameRuntimeAction.ClaimRegionalHuntReward));
        _huntBoard.AbandonRequested += () => RunRegionalAction(new(EndgameRuntimeAction.AbandonRegionalHunt));
        _huntBoard.ReturnRequested += () => RunRegionalAction(new(EndgameRuntimeAction.ReturnRegionalHunt));
        _huntBoard.BoardApproachRequested += () => _sandbox.RequestWorldInteraction(RegionalHuntCatalog.BoardInteraction);
        _huntBoard.VisibilityChangedByPlayer += open =>
        {
            if (open) { _secretPanel?.SetOpen(false); _stashPanel?.SetOpen(false); _championPanel?.SetOpen(false); _collection?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel(); }
            UpdatePanelVisibility();
        };
        _huntBoard.MenuRequested += action =>
        {
            _huntBoard.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (action == "aw_endgame") _board.SetOpen(true);
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
    }

    private void OpenRegionalHunts()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        RefreshRegionalHunts(); _huntBoard.SetOpen(true);
    }
    private void RunRegionalAction(EndgameRuntimeCommand command)
    {
        long before = _session.Tick; Apply(command);
        if (_session.Tick != before && command.Action is EndgameRuntimeAction.StartRegionalHunt or EndgameRuntimeAction.AbandonRegionalHunt)
            _huntBoard.SetOpen(false);
        else OpenRegionalHunts();
    }
    private bool InteractRegionalHunt(string id)
    {
        if (id == RegionalHuntCatalog.BoardInteraction) { OpenRegionalHunts(); return true; }
        if (!id.StartsWith("hunt.regional.", StringComparison.Ordinal)) return false;
        Apply(new(EndgameRuntimeAction.TrackRegionalHuntClue, Id: id)); return true;
    }
    private string RegionalObjective(RegionalHuntRunView run) => run.Stage switch
    {
        "Tracking" => $"TRACK THE QUARRY · {run.TrackedClues}/3 clues\n{run.NextClue}\nClick the marked evidence, or approach and press F. J opens your contract.",
        "Combat" => "DEFEAT THE QUARRY\n" + run.Counterplay,
        "Victory" when _session.InRegionalHunt => "QUARRY DEFEATED\nReturn to Greyhaven, then claim your bounty at the hunt board.",
        "Victory" when !_session.RegionalHunts.NearBoard => "BOUNTY READY\nApproach Greyhaven's hunt board to claim your legendary item and materials.",
        "Victory" when !run.CanClaim => "BOUNTY READY\nMake space in your permanent inventory before claiming this reward.",
        "Victory" => "BOUNTY READY\nClaim your legendary item and materials. Each completed hunt pays once.",
        "Failed" => "HUNT FAILED\nReturn to Greyhaven. This attempt paid no reward; accept a new contract to try again.",
        _ => "Inspect another regional contract at Greyhaven's hunt board."
    };
    private void RefreshRegionalHunts()
    {
        if (_huntBoard is null) return;
        var board = _session.RegionalHunts;
        var run = board.Run is { Stage: not ("Claimed" or "Abandoned") } active ? active : null;
        var contracts = board.Contracts.Select(c => new RegionalHuntContractDisplay(c.Id, c.Name, c.Region, c.Description,
            c.Unlocked, c.Requirement, c.Unlocked ? [c.Counterplay] : [], c.Unlocked ? $"{EquipmentNames.For(c.RewardItemId)} · {c.Materials} materials" : "Undiscovered",
            c.Unlocked ? RegionalHuntCatalog.Find(c.Id)!.Clues : [], run?.ContractId == c.Id ? run.TrackedClues : 0, c.Unlocked && board.CanStart,
            !_session.InHub ? "Return to Greyhaven to accept a contract." : run is not null ? "Resolve your current contract first." : !board.NearBoard ? "Approach the hunt board to depart." : !board.CanStart ? "Make room for the bounty. If your inventory has space, this character has reached its contract limit." : c.Requirement,
            c.Act switch { 2 => "8db875", 3 => "d99761", _ => "b6accc" })).ToArray();
        _huntBoard.SetView(new(contracts, run is null ? null : new(run.ContractId, run.Name, run.Stage, RegionalObjective(run),
            _session.InRegionalHunt && run.Stage == "Tracking", run.CanClaim, false, run.CanAbandon, run.CanReturn),
            _session.InHub, board.NearBoard, _session.Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0), _session.Combat.View.Loot.Count, _revision, _huntNotice));
        _huntPresentation.Present(_session.Interactions, _session.InRegionalHunt ? run!.Act : 0);
        _huntNavigation.Visible = _hasActiveCharacter && !_frontMenu.IsOpen && _training is null && (_session.InHub || _session.InRegionalHunt) && _experiment?.View.Memory is null;
        _sandbox.AdditionalNavigationRows = _huntNavigation.Visible ? 1 : 0;
        _huntObjective.Visible = run is not null && !_huntBoard.IsOpen && !_frontMenu.IsOpen && _deathRecapHud?.IsOpen != true;
        if (run is not null)
        {
            CombatHudLayout.Objective(_huntObjective, GetViewport().GetVisibleRect().Size);
            _huntObjectiveText.Text = run.Name.ToUpperInvariant() + "\n" + RegionalObjective(run);
            string outcome = run.Id + ":" + run.Stage;
            if (run.Stage == "Victory" && _shownHuntOutcome != outcome) { _shownHuntOutcome = outcome; OpenRegionalHunts(); }
        }
    }
}
