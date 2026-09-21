using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Exploration;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    private LocalMapAtlas? explorationMap;
    private CombatContent? mapContent;
    private string MapRoomId => InHub ? "hub" : ActiveEncounterId != "clear" ? ActiveEncounterId :
        story.CurrentState.Exploration is { } current ? Content.Data.Exploration.Single(e => e.Id == current.Id).EncounterId : "clear.act." + View.Act;
    public LocalMapView? LocalMap => explorationMap?.View(MapRoomId, Room);
    public CampaignRuntimeResult EnableExplorationMap() => Execute(new(CampaignRuntimeAction.EnableExplorationMap));
    internal static RoomDefinition? ResolveMapRoom(CombatContent content, string id)
    {
        if (id == "hub" || id is "clear.act.1" or "clear.act.2" or "clear.act.3" or "clear.act.4" or "clear.act.5") return content.Room;
        var encounter = content.Campaign?.Encounters.FirstOrDefault(e => e.Id == id);
        return encounter is null ? null : encounter.Room ?? content.Room;
    }
    private RoomDefinition? ResolveMapRoom(string id) => ResolveMapRoom(mapContent ??= CombatContent.Parse(combatJson), id);
    private void RestoreExplorationMap(LocalMapAtlasState? source) => explorationMap = LocalMapAtlas.Restore(source, ResolveMapRoom);
    internal void RevealExplorationMap()
    {
        if (explorationMap is null) return;
        var player = Combat.View.Actors.Single(a => a.Id == 1);
        if (player.Health > 0) explorationMap.Reveal(MapRoomId, Room, player.Position);
    }
}
