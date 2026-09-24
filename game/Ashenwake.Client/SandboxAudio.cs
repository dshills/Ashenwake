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
            _ => "crypt_tell"
        };
        return _openingAudio.Play(cue, position);
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
            _ => null
        };
        return cue is not null && _openingAudio.Play(cue, position);
    }
}
