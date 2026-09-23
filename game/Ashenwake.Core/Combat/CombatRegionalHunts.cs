using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Combat;

// Versioned by the encounter IDs. Existing combat catalogs and unused archives retain their identities.
internal static class RegionalHuntCombat
{
    private static string Primary(string encounter) => RegionalHuntCatalog.Contracts.Single(c => c.EncounterId == encounter).PrimaryEnemyId;
    internal static readonly RoomDefinition Arena = new(10500, 8500, new(-7000, 0), new(4000, 0), []);
    internal static CampaignCombatEncounter? Find(string id) => id switch
    {
        "regional.trail" => new(id, "The hunting trail", "", 0, [], Arena),
        "regional.pallbearer" => new(id, "The Cinder Pallbearer", "SonicLanes", 0,
            [new(Primary(id), new(1800, 0), ["Dirgebound"]),
             new("enemy.memory_archer", new(4200, -1700), []), new("enemy.memory_archer", new(4200, 1700), [])], Arena),
        "regional.briarwidow" => new(id, "Briarwidow of the Maw", "PoisonLanes", 0,
            [new(Primary(id), new(2400, 0), ["Hunter"]),
             new("enemy.bloom_carrier", new(4500, -2200), []), new("enemy.bloom_carrier", new(4500, 2200), [])], Arena),
        "regional.kilnmaw" => new(id, "Kilnmaw the Unquenched", "Conveyor", 0,
            [new(Primary(id), new(2000, 0), ["Martyr"]), new("enemy.heat_tender", new(4300, 0), []),
             new("enemy.emberling", new(4100, -2500), []), new("enemy.emberling", new(4100, 2500), [])], Arena),
        _ => null
    };
}

public sealed partial class CombatSession
{
    internal void SealRegionalHuntVictory()
    {
        if (RegionalHuntCombat.Find(_state.EncounterId) is null || Player.Health <= 0 ||
            _state.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidOperationException("Only an observed regional hunt victory may seal its arena.");
        _state.Campaign!.Hazards.Clear();
        _state.Campaign.SuppressedFragmentId = ""; _state.Campaign.SuppressedUntil = Tick;
        _state.Projectiles.Clear(); _state.Areas.Clear();
        foreach (var actor in _state.Actors) { actor.Pending = null; actor.Statuses.Clear(); }
    }
}
