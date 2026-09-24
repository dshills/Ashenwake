using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class CinderSmoke
{
    private readonly HashSet<string> _creatureAttacks = [];

    private async Task ObserveCreatureEvents(IReadOnlyList<CombatEvent> events)
    {
        // Check dispatch before awaiting rendered captures: cosmetics may naturally expire
        // while the diagnostic holds Core still to photograph the scene.
        var captures = new List<string>();
        foreach (var e in events)
        {
            if (e.Kind is not ("AbilityResolved" or "CampaignHazardResolved")) continue;
            var actor = _session.Combat.View.Actors.FirstOrDefault(a => a.Id == e.ActorId);
            if (actor is null || _creatureAttacks.Contains(actor.DefinitionId)) continue;
            // Rusher detonation kills its source in the same authoritative tick. Its
            // terminal collapse must retain priority over the resolved attack clip.
            bool detonation = actor.DefinitionId == "enemy.emberling" && e.ContentId == "enemy.detonate" && actor.Health <= 0;
            if (actor.Health <= 0 && !detonation) continue;
            var node = _sandbox.GetNodeOrNull<Node3D>("Actor" + actor.Id);
            var visual = node is null ? null : Descendants(node).OfType<CharacterVisual>().FirstOrDefault();
            if (visual is null || visual.CreatureMotionKind is not ("Emberling" or "Brute" or "Sentinel" or "Spindle")) continue;
            Check(actor.DefinitionId + "_authoritative_resolution_reaches_creature", visual.ActiveCue == (detonation ? "death" : "attack"));
            _creatureAttacks.Add(actor.DefinitionId);
            captures.Add(actor.DefinitionId.Replace('.', '-') + (detonation ? "-live-detonation.png" : "-live-attack.png"));
        }
        if (captures.Count == 0) return;
        string hash = _session.StateHash;
        foreach (string capture in captures) await Capture(capture);
        Check("creature_capture_preserves_authoritative_state", _session.StateHash == hash);
    }
}
