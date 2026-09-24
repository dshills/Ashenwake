using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class VerdantSmoke
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
            var actor = _session.Combat.View.Actors.FirstOrDefault(a => a.Id == e.ActorId && a.Health > 0);
            if (actor is null || _creatureAttacks.Contains(actor.DefinitionId)) continue;
            var node = _sandbox.GetNodeOrNull<Node3D>("Actor" + actor.Id);
            var visual = node is null ? null : Descendants(node).OfType<CharacterVisual>().FirstOrDefault();
            if (visual is null || visual.CreatureMotionKind == "None") continue;
            Check(actor.DefinitionId + "_authoritative_resolution_reaches_creature", visual.ActiveCue == "attack");
            _creatureAttacks.Add(actor.DefinitionId);
            captures.Add(actor.DefinitionId.Replace('.', '-') + "-live-attack.png");
        }
        if (captures.Count == 0) return;
        string hash = _session.StateHash;
        foreach (string capture in captures) await Capture(capture);
        Check("creature_capture_preserves_authoritative_state", _session.StateHash == hash);
    }
}
