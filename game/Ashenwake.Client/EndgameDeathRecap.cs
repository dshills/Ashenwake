using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Godot;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private DeathRecapHud _deathRecapHud = null!;
    private DeathRecap? _shownDeathRecap;
    private string _shownRecovery = "";

    private void InitializeDeathRecap()
    {
        _deathRecapHud = new DeathRecapHud(); _sandbox.AddOverlay(_deathRecapHud);
        _deathRecapHud.PrimaryRequested += () => Safely(DeathRecapPrimary);
        _deathRecapHud.HubRequested += () => Safely(() =>
        {
            if (!_deathRecapHud.IsOpen || _session.RunView is { Status: "Active" }) return;
            _deathRecapHud.Close(); ReturnHub();
        });
        _deathRecapHud.CloseRequested += () =>
        {
            _deathRecapHud.Close();
            if (_session.InSecretChamber) OpenSecretChambers();
            else if (_session.InRegionalHunt && _session.RegionalHunts.Run?.Stage == "Failed") OpenRegionalHunts();
            else if (_session.RunView is { AwaitingRetry: true } or { Status: "Failed" }) _board.ShowRun();
        };
    }

    private void ClearDeathRecap()
    {
        _deathRecapHud?.Close(); _shownDeathRecap = null; _shownRecovery = "";
    }

    private void ObserveDeathRecap()
    {
        if (_deathRecapHud is null || _training is not null || _smoke || _echoesSmoke || !_hasActiveCharacter || _frontMenu?.IsOpen == true) return;
        var recap = _session.LastDeathRecap;
        var run = _session.RunView;
        bool defeatedRun = !_session.InHub && run is not null && (run.AwaitingRetry || run.Status == "Failed");
        bool defeatedHunt = _session.InRegionalHunt && _session.RegionalHunts.Run?.Stage == "Failed";
        bool defeatedSecret = _session.InSecretChamber && _session.SecretChambers.Run?.Stage == "Failed";
        string recoveryKey = defeatedSecret ? $"secret:{_session.Capture().SecretChambers!.AttemptSequence}" : defeatedHunt ? $"regional:{_session.RegionalHunts.Run!.Id}" : defeatedRun ? $"{run!.Id}:{run.Deaths}:{run.Status}" : "";
        bool newDeath = recap is not null && !ReferenceEquals(recap, _shownDeathRecap);
        bool restoredDefeat = recap is null && (defeatedRun || defeatedHunt || defeatedSecret) && recoveryKey != _shownRecovery;
        if (!newDeath && !restoredDefeat) return;
        _shownDeathRecap = recap; _shownRecovery = recoveryKey;
        _huntBoard?.SetOpen(false); _secretPanel?.SetOpen(false); _stashPanel?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
        _deathRecapHud.SetView(DeathPresentation(recap, defeatedRun ? run : null));
        _deathRecapHud.Open();
    }

    private DeathRecapPresentation DeathPresentation(DeathRecap? recap, EndgameRunView? run)
    {
        string killing = recap is null ? "This saved run is awaiting recovery. Damage history was not saved; the killing blow is unavailable." :
            $"{recap.KillingBlow.SourceName} · {recap.KillingBlow.AttackName}\n{recap.KillingBlow.Damage:N0} health lost · {FamilyName(recap.KillingBlow.Family)}";
        string damage = recap is null ? "No recorded damage history for this restored attempt." : "Last 8 seconds · up to 64 hits · actual health lost after defense and barriers, excluding overkill.\n\n" + string.Join("\n", recap.RecentDamage.Reverse().Select(hit =>
            $"{(recap.Tick - hit.Tick) / 30d:0.0}s before defeat · {hit.SourceName} · {hit.AttackName}\n{hit.Damage:N0} health lost · {FamilyName(hit.Family)}"));
        string conditions = recap is null ? "Unavailable after loading." : recap.Conditions.Count == 0 ? "No harmful conditions were active before the killing blow." :
            string.Join(" · ", recap.Conditions.Select(status => $"{status.Id} ×{status.Stacks} ({status.RemainingTicks / 30d:0.0}s remaining)"));
        string recovery;
        bool failedHunt = _session.InRegionalHunt && _session.RegionalHunts.Run?.Stage == "Failed";
        bool failedSecret = _session.InSecretChamber && _session.SecretChambers.Run?.Stage == "Failed";
        if (failedSecret) recovery = $"{_session.CurrentSecretChamber!.Name} · Guardian challenge failed.\nReturn through the passage to the room you left. Campaign progress, ground loot, and owned equipment are preserved. You can return and challenge the guardian again; no treasure was awarded.";
        else if (failedHunt) recovery = $"{_session.CurrentRegionalHunt!.Name} · Hunt failed.\nThis attempt paid no bounty. Return to Greyhaven to prepare your build and accept a new contract.";
        else if (run is not null)
        {
            string room = _session.Combat.View.Endgame?.PhaseName ?? run.Name;
            recovery = $"{run.Name} · {room}\n{run.AttemptsRemaining} attempts remaining · {run.EncounterIndex}/{run.EncounterCount} rooms completed.\n" +
                "One attempt was consumed by this defeat. Owned gear, earned XP, materials and cleared rooms are retained.\n" +
                (run.CanRetry ? "Retry resets the current encounter. Uncollected drops in that encounter are left behind." : "No attempts remain. Return to Greyhaven to choose another expedition.");
        }
        else
        {
            string room = _combat.Campaign?.Encounters.FirstOrDefault(encounter => encounter.Id == _session.Campaign.ActiveEncounterId)?.Name ?? _session.Campaign.View.Region;
            recovery = $"Checkpoint restored: {room}.\nOwned gear, earned XP, materials and completed objectives are retained. The unfinished encounter resets; any unfinished optional activity ends.\nContinue when ready, or return to Greyhaven.";
        }
        return new("YOU FELL", killing, damage, conditions, DeathCounterplay(recap), recovery,
            failedSecret ? "Return through the passage" : failedHunt ? "Return to Greyhaven" : run is null ? "Continue from checkpoint" : run.CanRetry ? "Retry encounter" : "Return to Greyhaven", true, run is null && !failedHunt && !failedSecret);
    }

    private string DeathCounterplay(DeathRecap? recap)
    {
        if (recap is null) return (_session.InSecretChamber ? _session.CurrentSecretChamber?.Counterplay : _session.InRegionalHunt ? _session.CurrentRegionalHunt?.Counterplay : null) ?? _session.Combat.View.Endgame?.Counterplay ?? "Inspect the expedition board before retrying.";
        var hit = recap.KillingBlow;
        string tip = hit.DamageOverTime ? "The killing blow was damage over time. Leave its source and use a potion before the remaining damage overwhelms your health." : hit.AttackId switch
        {
            "hunt.vael.solar" => "Move into a cooling gap during the solar attack.",
            "hunt.orrun.numbered_fault" or "hunt.orrun.oathless_slam" or "hunt.nhal.remembered_rhythm" => "The numbered warnings resolve in sequence. Move clear of each marked area before its number resolves.",
            "elite.stormbound" => "Move out of the lightning warning before it resolves.",
            _ when hit.AttackId.StartsWith("hunt.", StringComparison.Ordinal) || hit.AttackId.StartsWith("campaign.", StringComparison.Ordinal) || hit.AttackId.StartsWith("rule.", StringComparison.Ordinal) || hit.AttackId.StartsWith("endgame.", StringComparison.Ordinal) => "Move clear of the marked attack area during its warning, before the hit lands.",
            _ => "Watch the attack windup and move or dodge before impact. Keep a potion available when your health is low."
        };
        string? encounter = (_session.InSecretChamber ? _session.CurrentSecretChamber?.Counterplay : _session.InRegionalHunt ? _session.CurrentRegionalHunt?.Counterplay : null) ?? _session.Combat.View.Endgame?.Counterplay;
        return encounter is { Length: > 0 } ? tip + "\nEncounter guidance: " + encounter : tip;
    }

    private static string FamilyName(DamageFamily family) => family switch
    {
        DamageFamily.PhysicalSlash => "Physical slash",
        DamageFamily.PhysicalPierce => "Physical pierce",
        DamageFamily.PhysicalCrush => "Physical crush",
        _ => family.ToString()
    };

    private void DeathRecapPrimary()
    {
        if (!_deathRecapHud.IsOpen) return;
        _deathRecapHud.Close();
        if (_session.InSecretChamber) Apply(new(EndgameRuntimeAction.ExitSecretChamber));
        else if (_session.InRegionalHunt && _session.RegionalHunts.Run?.Stage == "Failed") ReturnHub();
        else if (_session.RunView is { CanRetry: true }) Apply(new(EndgameRuntimeAction.RetryEncounter));
        else if (!_session.InHub && _session.RunView is { Status: "Failed" }) ReturnHub();
    }
}
