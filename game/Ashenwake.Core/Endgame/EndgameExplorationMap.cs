using Ashenwake.Core.Content;
using Ashenwake.Core.Exploration;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    private LocalMapAtlas? explorationMap;
    private string MapRoomId(int index) => "run." + State.Run!.Id + ".room." + index;
    public LocalMapView? LocalMap => InRoamingChampion || InSecretChamber || InRegionalHunt ? null : arena is null ? Campaign.LocalMap : explorationMap?.View(MapRoomId(ArenaIndex), Room);
    public EndgameRuntimeResult EnableExplorationMap() => Execute(new(EndgameRuntimeAction.EnableExplorationMap));
    private RoomDefinition? ResolveMapRoom(string id)
    {
        if (manifest?.Rooms is not { Length: > 0 } || State.Run is null || arena is null) return null;
        for (int index = 0; index < manifest.Rooms.Length; index++)
            if (id == MapRoomId(index) && index <= ArenaIndex) return combatContent.RoomFor(manifest, index);
        return null;
    }
    private void RestoreExplorationMap(LocalMapAtlasState? source) => explorationMap = LocalMapAtlas.Restore(source, ResolveMapRoom);
    private void RevealExplorationMap()
    {
        if (InRoamingChampion || InSecretChamber || InRegionalHunt) return;
        if (arena is null) { Campaign.RevealExplorationMap(); return; }
        if (explorationMap is null) return;
        var player = Combat.View.Actors.Single(a => a.Id == 1);
        if (player.Health > 0) explorationMap.Reveal(MapRoomId(ArenaIndex), Room, player.Position);
    }
}
