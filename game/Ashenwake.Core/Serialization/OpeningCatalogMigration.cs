using Ashenwake.Core.Exploration;
using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Serialization;

/// <summary>Reconstructs the exact published campaign catalogs preceding authored opening
/// rooms. Callers authenticate and restore the original archive before applying any changes.</summary>
internal static class OpeningCatalogMigration
{
    private const string CurrentCampaign = "campaign.grey_march.2", CurrentCombat = "campaign-combat.grey_march.2";
    private const string CryptDiscovery = "discovery.widow_crypt";
    private static string Frozen(string name)
    {
        using var stream = typeof(OpeningCatalogMigration).Assembly.GetManifestResourceStream("Ashenwake.PreviousOpening." + name + ".json")
            ?? throw new InvalidOperationException("Missing authenticated opening migration catalog.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    // Explicit multi-build imports may intentionally supply a maintained old combat
    // bundle as their intermediate target. That bundle must retain its matching story
    // until the importer moves both catalogs to the current build together.
    internal static CampaignContent CampaignForCombat(string combatJson, CampaignContent campaign)
        => campaign.Capture().Version == CurrentCampaign && CombatContent.Parse(combatJson).Campaign?.Version == "campaign.combat.1"
            ? CampaignContent.Parse(Frozen("Campaign")) : campaign;

    internal static bool TryPrevious(string combatJson, ProgressionContent policy, CampaignContent campaign,
        out string previousCombat, out ProgressionContent previousPolicy, out CampaignContent previousCampaign)
    {
        previousCombat = combatJson; previousPolicy = policy; previousCampaign = campaign;
        var node = JsonNode.Parse(combatJson)!;
        string? version = node["campaign"]?["version"]?.GetValue<string>();
        bool changedCombat = version == CurrentCombat, changedCampaign = campaign.Capture().Version == CurrentCampaign;
        if (!changedCombat && !changedCampaign) return false;
        // An unrelated campaign overlay is never substituted with a fabricated predecessor.
        if (version is not (CurrentCombat or "campaign.combat.1")) return false;
        if (changedCombat)
        {
            var oldOverlay = JsonNode.Parse(Frozen("Combat"))!;
            var currentEnemies = node["campaign"]!["enemies"]!.AsArray();
            var campaignIds = currentEnemies.Select(e => e!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var enemies = node["enemies"]!.AsArray();
            int offset = Enumerable.Range(0, enemies.Count).First(i => campaignIds.Contains(enemies[i]!["id"]!.GetValue<string>()));
            for (int i = enemies.Count - 1; i >= 0; i--)
                if (campaignIds.Contains(enemies[i]!["id"]!.GetValue<string>())) enemies.RemoveAt(i);
            foreach (var enemy in oldOverlay["enemies"]!.AsArray()) enemies.Insert(offset++, enemy!.DeepClone());
            node["campaign"] = oldOverlay;
            string[] versions = node["contentVersion"]!.GetValue<string>().Split('+');
            if (versions.Count(v => v == CurrentCombat) != 1) return false;
            node["contentVersion"] = string.Join('+', versions.Select(v => v == CurrentCombat ? "campaign.combat.1" : v));
            previousCombat = node.ToJsonString();
        }
        if (changedCampaign) previousCampaign = CampaignContent.Parse(Frozen("Campaign"));
        previousPolicy = PreviousPolicy(policy);
        return true;
    }

    internal static ProgressionContent PreviousPolicy(ProgressionContent policy)
    {
        var source = policy.Capture();
        return source.DiscoveryIds.Contains(CryptDiscovery, StringComparer.Ordinal)
            ? ProgressionContent.Create(source with { DiscoveryIds = source.DiscoveryIds.Where(id => id != CryptDiscovery).ToArray() }) : policy;
    }

    internal static CampaignRuntimeSnapshot Rebind(CampaignRuntimeSnapshot original, string combatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        var state = LegendaryCatalogMigration.Rebind(original, combatJson, adventure, policy, campaign);
        var content = CombatContent.Parse(combatJson);
        return state with
        {
            Campaign = state.Campaign with { ContentHash = campaign.Hash },
            ExplorationMap = LocalMapAtlas.Rebind(state.ExplorationMap, id => CampaignRuntimeSession.ResolveMapRoom(content, id)),
            Combat = Relocate(state.Combat, content, state.ActiveEncounterId),
            ClearedRooms = state.ClearedRooms is null ? null : new SortedDictionary<string, CombatSnapshot>(
                state.ClearedRooms.ToDictionary(pair => pair.Key, pair => Relocate(pair.Value, content, pair.Key)), StringComparer.Ordinal)
        };
    }

    internal static EndgameRuntimeSnapshot Rebind(EndgameRuntimeSnapshot original, string combatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        var state = LegendaryCatalogMigration.Rebind(original, combatJson, adventure, policy, campaign);
        return state with { Campaign = Rebind(original.Campaign, combatJson, adventure, EndgameProgression.Resolve(policy), campaign) };
    }

    private static CombatSnapshot Relocate(CombatSnapshot snapshot, CombatContent content, string activeEncounter)
    {
        string encounterId = snapshot.EncounterId == "clear" ? activeEncounter : snapshot.EncounterId;
        var room = content.Campaign?.Encounters.FirstOrDefault(e => e.Id == encounterId)?.Room;
        if (room is null) return snapshot;
        var state = JsonData.Copy(snapshot) with { RoomEncounterId = snapshot.EncounterId == "clear" ? encounterId : null };
        var spatial = new SpatialWorld(room);
        Position RelocatePoint(Position point, int radius, int actorId = 0)
        {
            if (spatial.CanOccupy(point, radius)) return point;
            // Search a bounded lattice and exact obstacle edges, keeping the nearest legal
            // candidate with an explicit integer tie-break. Existing legal points never move.
            var xs = new SortedSet<int> { Math.Clamp(point.X, -room.HalfWidth + radius, room.HalfWidth - radius), room.PlayerSpawn.X };
            var zs = new SortedSet<int> { Math.Clamp(point.Z, -room.HalfDepth + radius, room.HalfDepth - radius), room.PlayerSpawn.Z };
            for (int x = -room.HalfWidth + radius; x <= room.HalfWidth - radius; x += 560) xs.Add(x);
            for (int z = -room.HalfDepth + radius; z <= room.HalfDepth - radius; z += 560) zs.Add(z);
            foreach (var obstacle in room.Obstacles)
            { xs.Add(obstacle.MinX - radius - 1); xs.Add(obstacle.MaxX + radius + 1); zs.Add(obstacle.MinZ - radius - 1); zs.Add(obstacle.MaxZ + radius + 1); }
            return (from x in xs
                    from z in zs
                    let candidate = new Position(x, z)
                    where spatial.CanOccupy(candidate, radius) && (actorId == 0 || state.Actors.All(other => other.Id == actorId || other.Health == 0 ||
                        Position.DistanceSquared(candidate, other.Position) >= 4L * CombatSession.ActorRadius * CombatSession.ActorRadius))
                    orderby Position.DistanceSquared(point, candidate), x, z
                    select (Position?)candidate).FirstOrDefault()
                ?? throw new InvalidDataException("No safe position in the migrated opening room.");
        }
        foreach (var actor in state.Actors.OrderBy(a => a.Id))
        {
            actor.Position = RelocatePoint(actor.Position, CombatSession.ActorRadius, actor.Id);
            if (actor.Pending is { } pending) actor.Pending = pending with { Target = RelocatePoint(pending.Target, 0) };
        }
        for (int i = 0; i < state.Loot.Count; i++) state.Loot[i] = state.Loot[i] with { Position = RelocatePoint(state.Loot[i].Position, 0) };
        for (int i = 0; i < state.Areas.Count; i++) state.Areas[i] = state.Areas[i] with { Position = RelocatePoint(state.Areas[i].Position, 0) };
        for (int i = 0; i < state.Projectiles.Count; i++) state.Projectiles[i] = state.Projectiles[i] with
        { Position = RelocatePoint(state.Projectiles[i].Position, 0), Target = RelocatePoint(state.Projectiles[i].Target, 0) };
        // Campaign hazard endpoints only require room bounds, which this authored upgrade preserves.
        return state;
    }
}
