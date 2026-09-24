using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private OpeningAudio _openingAudio = null!;
    internal OpeningAudio AudioDirector => _openingAudio;

    private bool PlayOpeningTell(CombatEvent e, Vector3 position)
    {
        var actor = _view.Actors.FirstOrDefault(a => a.Id == e.ActorId);
        if (actor?.DefinitionId == "boss.breach_heart")
        {
            if (OpeningAudio.PrimaryBreach(_view)?.Id != actor.Id)
                return _openingAudio.Play("echo_tell", position);
            if (e.Kind == "BossPatternStarted" || e.ContentId is "campaign.returning_echo" or "campaign.seal_sweep") return true;
            return e.ContentId == "campaign.breach_echo" && _openingAudio.Play("breach_echo", position);
        }
        if (actor?.DefinitionId == "boss.covenant_warden")
        {
            // The oath and fault are scheduled together. Announce the fault when
            // the oath resolves, provided the follow-up hazard is still active.
            if (e.ContentId == "campaign.covenant_fault" || e.Kind == "BossPatternStarted") return true;
            return e.ContentId == "campaign.oath_mark" && _openingAudio.Play("warden_oath", position);
        }
        string cue = actor?.DefinitionId switch
        {
            "enemy.funeral_guard" => "guard_tell",
            "enemy.memory_archer" => "archer_tell",
            "boss.bell_saint" or "enemy.bell_saint" or "enemy.bell_beast" => "saint_tell",
            "enemy.carnivorous_vine" => "vine_tell",
            "enemy.needle_swarm" => "swarm_tell",
            "enemy.bloom_carrier" => "carrier_tell",
            "boss.antler" => "antler_tell",
            "boss.rootheart" => "rootheart_tell",
            "enemy.emberling" => "emberling_tell",
            "enemy.furnace_brute" => "brute_tell",
            "enemy.forge_sentinel" => "sentinel_tell",
            "boss.furnace_spindle" => "furnace_tell",
            "enemy.oath_giant" => "giant_tell",
            "enemy.contract_keeper" => "keeper_tell",
            "enemy.bone_sentinel" => "bone_tell",
            "enemy.doubled_shadow" => "shadow_tell",
            "enemy.breach_echo" => "echo_tell",
            _ => "crypt_tell"
        };
        return _openingAudio.Play(cue, position);
    }

    private bool PlayHollowRuleTell(CombatEvent e)
        => e.ContentId == "rule.causalechoes" && HollowAmbience.CueForStyle(_environmentStyle).Length > 0 &&
            _openingAudio.Play("causal_tell", Vector3.Zero);

    private void PlayHollowFollowup(CombatEvent e)
    {
        if (_environmentStyle != "hollow_breach" || !_view.Actors.Any(a => a.Id == 1 && a.Health > 0) ||
            OpeningAudio.PrimaryBreach(_view) is not { Health: > 0 } boss || boss.Id != e.ActorId) return;
        bool Pending(string id) => _view.CampaignHazards?.Any(h => h.SourceId == boss.Id &&
            h.ContentId == id && h.RemainingTicks > 0) == true;
        // Warn emits the entire sequence together. Advance its audible warning only
        // after a real resolution and only while the next hazard still exists.
        if (e.ContentId == "campaign.breach_echo")
        {
            if (Pending("campaign.seal_sweep")) _openingAudio.Play("breach_sweep", Vector3.Zero);
            else if (Pending("campaign.returning_echo")) _openingAudio.Play("breach_return", Vector3.Zero);
        }
        else if (e.ContentId == "campaign.seal_sweep" && Pending("campaign.returning_echo"))
            _openingAudio.Play("breach_return", Vector3.Zero);
    }

    private bool PlaySpineRuleTell(CombatEvent e)
    {
        if (SpineAmbience.CueForStyle(_environmentStyle).Length == 0 ||
            e.ContentId is not ("rule.fault.1" or "rule.fault.2" or "rule.fault.3")) return false;
        // All three warnings arrive together. Later beats follow real resolutions,
        // so pauses, memory lane reversal and cancelled hazards need no audio timer.
        if (e.ContentId == "rule.fault.1") _openingAudio.Play("spine_fault", Vector3.Zero);
        return true;
    }

    private void PlaySpineFollowup(CombatEvent e)
    {
        if (SpineAmbience.CueForStyle(_environmentStyle).Length == 0 ||
            !_view.Actors.Any(a => a.Id == 1 && a.Health > 0)) return;
        string next = e.ContentId switch
        {
            "rule.fault.1" => "rule.fault.2",
            "rule.fault.2" => "rule.fault.3",
            "campaign.oath_mark" when _view.Actors.Any(a => a.Id == e.ActorId &&
                a.DefinitionId == "boss.covenant_warden" && a.Health > 0) => "campaign.covenant_fault",
            _ => ""
        };
        if (next.Length > 0 && _view.CampaignHazards?.Any(h => h.SourceId == e.ActorId &&
            h.ContentId == next && h.RemainingTicks > 0) == true)
            _openingAudio.Play(next == "campaign.covenant_fault" ? "warden_fault" : "spine_fault", Vector3.Zero);
    }

    private bool PlayOpeningImpact(CombatEvent e, Vector3 position)
    {
        var skill = _content.Skills.FirstOrDefault(s => s.Id == e.ContentId);
        bool magic = e.ContentId is "campaign.memoryarrow" or "campaign.chain" or "campaign.sonic" or
            "campaign.venompod" or "campaign.swarm" or "campaign.poisonburst" or "campaign.root_tangle" or "campaign.root_spores" ||
            e.ContentId is "campaign.heatvent" or "campaign.forgesweep" or "campaign.furnace_vent" or "campaign.slag" or "enemy.detonate" or "rule.storm" ||
            e.ContentId is "campaign.shadowdouble" or "campaign.causalecho" or "campaign.breach_echo" or
                "campaign.returning_echo" or "campaign.seal_sweep" or "rule.causalechoes" ||
            skill?.Family is DamageFamily.Fire or DamageFamily.Frost or DamageFamily.Storm or
                DamageFamily.Decay or DamageFamily.Venom or DamageFamily.Void;
        return _openingAudio.Play(magic ? "impact_spell" : "impact_weapon", position);
    }

    private bool PlayRegionalPhase(CombatEvent e, Vector3 position)
    {
        string? cue = e.ContentId switch
        {
            "boss.rootheart" when e.Amount == 2 => "rootheart_phase2",
            "boss.furnace_spindle" when e.Amount == 2 => "furnace_phase2",
            "boss.covenant_warden" when e.Amount == 2 => "warden_phase2",
            "boss.breach_heart" when OpeningAudio.PrimaryBreach(_view)?.Id == e.ActorId && e.Amount is 2 or 3
                => e.Amount == 2 ? "breach_phase2" : "breach_phase3",
            "boss.bell_saint" or "enemy.bell_saint" or "enemy.bell_beast" => e.Amount >= 3 ? "bell_phase3" : "bell_phase2",
            _ => null
        };
        return cue is not null && _openingAudio.Play(cue, position);
    }

    private bool PlayRegionalDeath(int targetId, Vector3 position)
    {
        // Consume real death events only. Reset/load projection never replays these tails.
        string? cue = _view.Actors.FirstOrDefault(a => a.Id == targetId)?.DefinitionId switch
        {
            "enemy.feeding_root" => "root_severed",
            "boss.rootheart" => "rootheart_fall",
            "boss.furnace_spindle" => "furnace_shutdown",
            "boss.covenant_warden" => "warden_defeat",
            "enemy.seal_channel" => "seal_broken",
            "boss.breach_heart" when OpeningAudio.PrimaryBreach(_view)?.Id == targetId => "breach_containment",
            _ => null
        };
        return cue is not null && _openingAudio.Play(cue, position);
    }
}
