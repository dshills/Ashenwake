using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class HollowSmoke
{
    private readonly HashSet<string> _creatureAttacks = [];

    private async Task ObserveCreatureEvents(IReadOnlyList<CombatEvent> events)
    {
        // Assert immediate dispatch before any capture wait can expire a cosmetic clip.
        var captures = new List<string>();
        foreach (var e in events)
        {
            if (e.Kind is not ("AbilityResolved" or "CampaignHazardResolved" or "EliteAbilityResolved")) continue;
            // Support wards and room hazards do not dispatch weapon attack clips.
            if (e.ContentId is "campaign.oath_ward" or "elite.dirgebound" || e.ContentId.StartsWith("rule.", StringComparison.Ordinal)) continue;
            var actor = _session.Combat.View.Actors.FirstOrDefault(a => a.Id == e.ActorId && a.Health > 0);
            if (actor is null || _creatureAttacks.Contains(actor.DefinitionId)) continue;
            var node = _sandbox.GetNodeOrNull<Node3D>("Actor" + actor.Id);
            var visual = node is null ? null : Descendants(node).OfType<CharacterVisual>().FirstOrDefault();
            if (visual is null || visual.CreatureMotionKind is not ("Shadow" or "Echo" or "Breach")) continue;
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
