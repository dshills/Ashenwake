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
            _ => "crypt_tell"
        };
        return _openingAudio.Play(cue, position);
    }

    private bool PlayOpeningImpact(CombatEvent e, Vector3 position)
    {
        var skill = _content.Skills.FirstOrDefault(s => s.Id == e.ContentId);
        bool magic = e.ContentId is "campaign.memoryarrow" or "campaign.chain" or "campaign.sonic" or
            "campaign.venompod" or "campaign.swarm" or "campaign.poisonburst" or "campaign.root_tangle" or "campaign.root_spores" ||
            skill?.Family is DamageFamily.Fire or DamageFamily.Frost or DamageFamily.Storm or
                DamageFamily.Decay or DamageFamily.Venom or DamageFamily.Void;
        return _openingAudio.Play(magic ? "impact_spell" : "impact_weapon", position);
    }

    private bool PlayRegionalPhase(CombatEvent e, Vector3 position)
    {
        string? cue = e.ContentId switch
        {
            "boss.rootheart" when e.Amount == 2 => "rootheart_phase2",
            "boss.bell_saint" or "enemy.bell_saint" or "enemy.bell_beast" => e.Amount >= 3 ? "bell_phase3" : "bell_phase2",
            _ => null
        };
        return cue is not null && _openingAudio.Play(cue, position);
    }

    private bool PlayVerdantDeath(int targetId, Vector3 position)
    {
        // Consume real death events only. Reset/load projection never replays these tails.
        string? cue = _view.Actors.FirstOrDefault(a => a.Id == targetId)?.DefinitionId switch
        {
            "enemy.feeding_root" => "root_severed",
            "boss.rootheart" => "rootheart_fall",
            _ => null
        };
        return cue is not null && _openingAudio.Play(cue, position);
    }
}
