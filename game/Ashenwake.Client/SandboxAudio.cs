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
            _ => "crypt_tell"
        };
        return _openingAudio.Play(cue, position);
    }

    private bool PlayOpeningImpact(CombatEvent e, Vector3 position)
    {
        var skill = _content.Skills.FirstOrDefault(s => s.Id == e.ContentId);
        bool magic = e.ContentId is "campaign.memoryarrow" or "campaign.chain" or "campaign.sonic" ||
            skill?.Family is DamageFamily.Fire or DamageFamily.Frost or DamageFamily.Storm or
                DamageFamily.Decay or DamageFamily.Venom or DamageFamily.Void;
        return _openingAudio.Play(magic ? "impact_spell" : "impact_weapon", position);
    }
}
