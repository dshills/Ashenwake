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

/// <summary>Reconstructs exact published campaign catalogs preceding authored regional
/// rooms. Callers authenticate and restore the original archive before applying any changes.</summary>
internal static class OpeningCatalogMigration
{
    private sealed record Release(string Campaign, string Combat, string PreviousCombat, string Resource, string Discovery);
    private static readonly Release[] Releases =
    [
        new("campaign.midgame_depth.9", "campaign-combat.midgame_depth.9", "campaign-combat.opening_depth.8", "PreviousMidgameDepth", ""),
        new("campaign.opening_depth.8", "campaign-combat.opening_depth.8", "campaign-combat.pacing.7", "PreviousOpeningDepth", ""),
        new("campaign.pacing.7", "campaign-combat.pacing.7", "campaign-combat.hollow.6", "PreviousPacing", ""),
        new("campaign.hollow.6", "campaign-combat.hollow.6", "campaign-combat.spine.5", "PreviousHollow", "discovery.unremembered_vault"),
        new("campaign.spine.5", "campaign-combat.spine.5", "campaign-combat.cinder.4", "PreviousSpine", "discovery.oathkeeper_archive"),
        new("campaign.cinder.4", "campaign-combat.cinder.4", "campaign-combat.verdant.3", "PreviousCinder", "discovery.sealed_foundry"),
        new("campaign.verdant.3", "campaign-combat.verdant.3", "campaign-combat.grey_march.2", "PreviousVerdant", "discovery.briar_shrine"),
        new("campaign.grey_march.2", "campaign-combat.grey_march.2", "campaign.combat.1", "PreviousOpening", "discovery.widow_crypt")
    ];
    private static string Frozen(string name, Release release)
    {
        using var stream = typeof(OpeningCatalogMigration).Assembly.GetManifestResourceStream("Ashenwake." + release.Resource + "." + name + ".json")
            ?? throw new InvalidOperationException("Missing authenticated regional migration catalog.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    // Explicit multi-build imports may intentionally supply a maintained old combat
    // bundle as their intermediate target. That bundle must retain its matching story
    // until the importer moves both catalogs to the current build together.
    internal static CampaignContent CampaignForCombat(string combatJson, CampaignContent campaign)
    {
        string version = campaign.Capture().Version;
        int current = Array.FindIndex(Releases, release => release.Campaign == version);
        if (current < 0) return campaign;
        string? combatVersion = CombatContent.Parse(combatJson).Campaign?.Version;
        var predecessor = Releases.Skip(current).FirstOrDefault(release => release.PreviousCombat == combatVersion);
        return predecessor is null ? campaign : CampaignContent.Parse(Frozen("Campaign", predecessor));
    }

    internal static bool TryPrevious(string combatJson, ProgressionContent policy, CampaignContent campaign,
        out string previousCombat, out ProgressionContent previousPolicy, out CampaignContent previousCampaign)
    {
        previousCombat = combatJson; previousPolicy = policy; previousCampaign = campaign;
        var node = JsonNode.Parse(combatJson)!;
        string? version = node["campaign"]?["version"]?.GetValue<string>();
        string storyVersion = campaign.Capture().Version;
        var release = Releases.FirstOrDefault(candidate => version == candidate.Combat || storyVersion == candidate.Campaign);
        if (release is null) return false;
        bool changedCombat = version == release.Combat, changedCampaign = storyVersion == release.Campaign;
        // An unrelated campaign overlay is never substituted with a fabricated predecessor.
        if (version != "campaign.combat.1" && !Releases.Any(candidate => candidate.Combat == version)) return false;
        if (changedCombat)
        {
            var oldOverlay = JsonNode.Parse(Frozen("Combat", release))!;
            var currentEnemies = node["campaign"]!["enemies"]!.AsArray();
            var campaignIds = currentEnemies.Select(e => e!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var enemies = node["enemies"]!.AsArray();
            int offset = Enumerable.Range(0, enemies.Count).First(i => campaignIds.Contains(enemies[i]!["id"]!.GetValue<string>()));
            for (int i = enemies.Count - 1; i >= 0; i--)
                if (campaignIds.Contains(enemies[i]!["id"]!.GetValue<string>())) enemies.RemoveAt(i);
            foreach (var enemy in oldOverlay["enemies"]!.AsArray()) enemies.Insert(offset++, enemy!.DeepClone());
            node["campaign"] = oldOverlay;
            string[] versions = node["contentVersion"]!.GetValue<string>().Split('+');
            if (versions.Count(v => v == release.Combat) != 1) return false;
            node["contentVersion"] = string.Join('+', versions.Select(v => v == release.Combat ? release.PreviousCombat : v));
            previousCombat = node.ToJsonString();
        }
        if (changedCampaign) previousCampaign = CampaignContent.Parse(Frozen("Campaign", release));
        previousPolicy = WithoutDiscovery(policy, release.Discovery);
        return true;
    }

    internal static ProgressionContent PreviousPolicy(ProgressionContent policy)
    {
        var discoveries = policy.Capture().DiscoveryIds;
        var release = Releases.FirstOrDefault(candidate => discoveries.Contains(candidate.Discovery, StringComparer.Ordinal));
        return release is null ? policy : WithoutDiscovery(policy, release.Discovery);
    }

    private static ProgressionContent WithoutDiscovery(ProgressionContent policy, string discovery)
    {
        var source = policy.Capture();
        return source.DiscoveryIds.Contains(discovery, StringComparer.Ordinal)
            ? ProgressionContent.Create(source with { DiscoveryIds = source.DiscoveryIds.Where(id => id != discovery).ToArray() }) : policy;
    }

    internal static CampaignRuntimeSnapshot Rebind(CampaignRuntimeSnapshot original, string combatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        var state = LegendaryCatalogMigration.Rebind(original, combatJson, adventure, policy, campaign);
        var content = CombatContent.Parse(combatJson);
        if (campaign.Capture().Exploration.Any(e => e.Id == "event.unremembered_vault") &&
            state.Campaign.CurrentAct == 5 && !state.Campaign.InHub && state.ActiveEncounterId == "clear" && state.Campaign.Exploration is null)
        {
            // Earlier builds used a generic room while waiting on the final choice or
            // revisiting a completed act. Bind those authenticated saves to a secured room.
            string secured = state.Campaign.CompletedEncounters.Contains("campaign.identity_memory") &&
                !state.Campaign.CompletedEncounters.Contains("campaign.breach_heart") ? "campaign.identity_memory" : "campaign.repeating_rooms";
            if (state.Campaign.CompletedEncounters.Contains(secured)) state = state with { ActiveEncounterId = secured };
        }
        if (campaign.Capture().Exploration.Any(e => e.Id == "event.oathkeeper_archive") &&
            state.Campaign.CurrentAct == 4 && !state.Campaign.InHub)
        {
            // Published generic cleared arenas gain a secured physical room. A Memory
            // entered from that generic arena returns to the latest secured main room.
            bool hallSecured = state.Campaign.CompletedEncounters.Contains("campaign.contract_hall");
            string secured = hallSecured && !state.Campaign.CompletedEncounters.Contains("campaign.covenant_warden")
                ? "campaign.contract_hall" : "campaign.bone_causeway";
            if (state.ActiveEncounterId == "clear" && state.Campaign.Exploration is null && state.Campaign.CompletedEncounters.Contains(secured))
                state = state with { ActiveEncounterId = secured };
            if (state.Campaign.Exploration?.Id == "event.divine_memory" && state.ExplorationReturnEncounter == "clear")
            {
                string parent = hallSecured ? "campaign.contract_hall" : "campaign.bone_causeway";
                if (state.Campaign.CompletedEncounters.Contains(parent)) state = state with { ExplorationReturnEncounter = parent };
            }
        }
        if (campaign.Capture().Exploration.Any(e => e.Id == CampaignRuntimeSession.FoundryEvent) &&
            state.Campaign.CurrentAct == 3 && !state.Campaign.InHub)
        {
            // Published Act III used a generic cleared arena after storm departure,
            // or when returning from the hub before a choice/after region completion.
            // Give that authenticated context a secured physical room before restore.
            string secured = state.Campaign.CompletedEncounters.Contains("campaign.extraction_floor") &&
                !state.Campaign.CompletedEncounters.Contains("campaign.furnace_spindle") ? "campaign.extraction_floor" : "campaign.cinder_pack";
            if (state.Campaign.CompletedEncounters.Contains(secured))
            {
                if (state.ActiveEncounterId == "clear" && state.Campaign.Exploration is null)
                    state = state with { ActiveEncounterId = secured };
                if (state.Campaign.Exploration?.Id == CampaignRuntimeSession.StormEvent && state.ExplorationReturnEncounter == "clear")
                    state = state with { ExplorationReturnEncounter = secured };
            }
        }
        state = state with
        {
            Campaign = state.Campaign with { ContentHash = campaign.Hash },
            ExplorationMap = LocalMapAtlas.Rebind(state.ExplorationMap, id => CampaignRuntimeSession.ResolveMapRoom(content, id)),
            Combat = Relocate(state.Combat, content, state.ActiveEncounterId == "clear" && state.Campaign.Exploration is { } exploration
                ? campaign.Capture().Exploration.Single(e => e.Id == exploration.Id).EncounterId : state.ActiveEncounterId),
            ClearedRooms = state.ClearedRooms is null ? null : new SortedDictionary<string, CombatSnapshot>(
                state.ClearedRooms.ToDictionary(pair => pair.Key, pair => Relocate(pair.Value, content, pair.Key)), StringComparer.Ordinal)
        };
        return CampaignPacingMigration.Rebind(original, state, combatJson, adventure, policy, campaign);
    }

    internal static EndgameRuntimeSnapshot Rebind(EndgameRuntimeSnapshot original, string combatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        var state = LegendaryCatalogMigration.Rebind(original, combatJson, adventure, policy, campaign);
        var rebound = state with { Campaign = Rebind(original.Campaign, combatJson, adventure, EndgameProgression.Resolve(policy), campaign) };
        return CampaignPacingMigration.RebindEndgameArena(original, rebound, combatJson, adventure, policy, campaign);
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
                ?? throw new InvalidDataException("No safe position in the migrated campaign room.");
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
